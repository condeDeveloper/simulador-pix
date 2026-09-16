using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Tests.Api;

public class ApiTests : IClassFixture<ApiTests.Fabrica>
{
    public sealed class Fabrica : WebApplicationFactory<Program>
    {
        private readonly string _banco = Path.Combine(Path.GetTempPath(), $"pix-teste-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pix"] = $"Data Source={_banco}",
                ["Seguranca:ApiKey"] = "chave-de-teste",
                ["Workers:IntervaloSegundos"] = "3600",
            }));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_banco); } catch { /* arquivo temporário */ }
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public ApiTests(Fabrica fabrica)
    {
        _http = fabrica.CreateClient();
        _http.DefaultRequestHeaders.Add("x-api-key", "chave-de-teste");
    }

    private static async Task<JsonElement> Corpo(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(Json);

    [Fact]
    public async Task SemApiKeyRecebe401()
    {
        var anonimo = new HttpClient { BaseAddress = _http.BaseAddress };
        var r = await _http.GetAsync("/saude");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        _http.DefaultRequestHeaders.Remove("x-api-key");
        try
        {
            (await _http.GetAsync("/api/webhook")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally { _http.DefaultRequestHeaders.Add("x-api-key", "chave-de-teste"); }
        anonimo.Dispose();
    }

    [Fact]
    public async Task FluxoCompletoCobrancaPagamentoDevolucao()
    {
        var txid = Txid.Gerar();
        var cob = new { calendario = new { expiracao = 3600 }, devedor = new { cpf = "52998224725", nome = "Fulano de Tal" }, valor = new { original = "150.00" }, chave = "loja@exemplo.com", solicitacaoPagador = "Pedido 42" };

        var criada = await _http.PutAsJsonAsync($"/api/cob/{txid}", cob);
        criada.StatusCode.Should().Be(HttpStatusCode.Created);
        var c = await Corpo(criada);
        c.GetProperty("status").GetString().Should().Be("ATIVA");
        c.GetProperty("valor").GetProperty("original").GetString().Should().Be("150.00");
        var brcode = c.GetProperty("pixCopiaECola").GetString()!;
        BrCode.Ler(brcode)["54"].Should().Be("150.00");

        // PUT repetido com os mesmos dados é idempotente (200); com dados diferentes é conflito (409)
        (await _http.PutAsJsonAsync($"/api/cob/{txid}", cob)).StatusCode.Should().Be(HttpStatusCode.OK);
        var diferente = await _http.PutAsJsonAsync($"/api/cob/{txid}", new { valor = new { original = "1.00" }, chave = "loja@exemplo.com" });
        diferente.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // qrcode decomposto
        var qr = await Corpo(await _http.GetAsync($"/api/cob/{txid}/qrcode"));
        qr.GetProperty("campos").GetProperty("58").GetString().Should().Be("BR");

        // revisão
        var rev = await _http.PatchAsJsonAsync($"/api/cob/{txid}", new { valor = new { original = "200.00" } });
        rev.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Corpo(rev)).GetProperty("revisao").GetInt32().Should().Be(1);

        // pagamento com valor errado é recusado; com valor certo conclui
        var errado = await _http.PostAsJsonAsync($"/api/simulacao/pagar/{txid}", new { pagador = new { nome = "Fulano", cpf = "52998224725" }, valor = "150.00" });
        errado.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var pago = await _http.PostAsJsonAsync($"/api/simulacao/pagar/{txid}", new { pagador = new { nome = "Fulano", cpf = "52998224725" }, infoPagador = "obrigado" });
        pago.StatusCode.Should().Be(HttpStatusCode.Created);
        var pix = await Corpo(pago);
        var e2e = pix.GetProperty("endToEndId").GetString()!;
        e2e.Should().StartWith("E12345678");
        pix.GetProperty("valor").GetString().Should().Be("200.00");

        var consulta = await Corpo(await _http.GetAsync($"/api/cob/{txid}"));
        consulta.GetProperty("status").GetString().Should().Be("CONCLUIDA");
        consulta.GetProperty("pix")[0].GetProperty("endToEndId").GetString().Should().Be(e2e);

        // segundo pagamento não é aceito
        (await _http.PostAsJsonAsync($"/api/simulacao/pagar/{txid}", new { pagador = new { nome = "Fulano", cpf = "52998224725" } })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // devolução parcial, idempotente, e limite
        var dev = await _http.PutAsJsonAsync($"/api/pix/{e2e}/devolucao/dev1", new { valor = "50.00" });
        dev.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Corpo(dev)).GetProperty("status").GetString().Should().Be("EM_PROCESSAMENTO");
        (await _http.PutAsJsonAsync($"/api/pix/{e2e}/devolucao/dev2", new { valor = "150.01" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _http.PutAsJsonAsync($"/api/pix/{e2e}/devolucao/dev1", new { valor = "60.00" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var lista = await Corpo(await _http.GetAsync($"/api/pix?inicio={Uri.EscapeDataString("2026-01-01T00:00:00Z")}&fim={Uri.EscapeDataString("2100-01-01T00:00:00Z")}&txid={txid}"));
        lista.GetProperty("pix").GetArrayLength().Should().Be(1);
        lista.GetProperty("pix")[0].GetProperty("devolucoes").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ChaveInvalidaExpiradaERemocao()
    {
        var invalida = await _http.PostAsJsonAsync("/api/cob", new { valor = new { original = "10.00" }, chave = "nao-e-chave" });
        invalida.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Corpo(invalida)).GetProperty("title").GetString().Should().Contain("chave Pix inválida");

        var criada = await Corpo(await _http.PostAsJsonAsync("/api/cob", new { valor = new { original = "10.00" }, chave = "+5511999998888" }));
        var txid = criada.GetProperty("txid").GetString()!;
        txid.Should().HaveLength(32);

        var removida = await _http.PatchAsJsonAsync($"/api/cob/{txid}", new { status = "REMOVIDA_PELO_USUARIO_RECEBEDOR" });
        removida.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Corpo(removida)).GetProperty("status").GetString().Should().Be("REMOVIDA_PELO_USUARIO_RECEBEDOR");
        (await _http.PostAsJsonAsync($"/api/simulacao/pagar/{txid}", new { pagador = new { nome = "X", cpf = "52998224725" } })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _http.GetAsync("/api/cob/naoexiste00000000000000000000000")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WebhookConfiguradoRecebeNotificacaoAssinada()
    {
        var chave = "11222333000181";
        var wh = await _http.PutAsJsonAsync($"/api/webhook/{chave}", new { webhookUrl = _http.BaseAddress + "api/simulacao/receptor", segredo = "segredo-de-teste" });
        wh.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var httpForaDeLocalhost = await _http.PutAsJsonAsync("/api/webhook/loja@exemplo.com", new { webhookUrl = "http://exemplo.com/x" });
        httpForaDeLocalhost.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var criada = await Corpo(await _http.PostAsJsonAsync("/api/cob", new { valor = new { original = "25.50" }, chave }));
        var txid = criada.GetProperty("txid").GetString()!;
        await _http.PostAsJsonAsync($"/api/simulacao/pagar/{txid}", new { pagador = new { nome = "Beltrano", cnpj = "45723174000110" } });

        var entregas = await Corpo(await _http.GetAsync($"/api/webhook/{chave}/entregas"));
        var resumo = string.Join(" | ", entregas.EnumerateArray().Select(e => e.GetProperty("criacao").GetString() + " " + e.GetProperty("corpo").GetString()));
        entregas.GetArrayLength().Should().Be(1, resumo);
        var pendente = entregas[0];
        pendente.GetProperty("evento").GetString().Should().Be("pix");
        pendente.GetProperty("status").GetString().Should().Be("PENDENTE");
        var corpo = pendente.GetProperty("corpo").GetString()!;
        Webhook.AssinaturaValida("segredo-de-teste", corpo, pendente.GetProperty("assinatura").GetString()).Should().BeTrue();
        corpo.Should().Contain(txid).And.Contain("\"valor\":\"25.50\"");
    }
}
