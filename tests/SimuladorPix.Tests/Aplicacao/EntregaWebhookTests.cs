using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SimuladorPix.Core.Aplicacao;
using SimuladorPix.Core.Dominio;
using SimuladorPix.Infra;

namespace SimuladorPix.Tests.Aplicacao;

/// <summary>Exercita o worker de entrega com um HttpClient falso e o banco SQLite em memória.</summary>
public class EntregaWebhookTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly PixDbContext _db;
    private readonly RelogioFixo _relogio = new(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

    public EntregaWebhookTests()
    {
        _conexao.Open();
        _db = new PixDbContext(new DbContextOptionsBuilder<PixDbContext>().UseSqlite(_conexao).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conexao.Dispose(); }

    private sealed class RespostaFixa : HttpMessageHandler
    {
        public HttpStatusCode Codigo { get; set; } = HttpStatusCode.OK;
        public List<HttpRequestMessage> Recebidas { get; } = new();
        public List<string> Corpos { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Recebidas.Add(request);
            Corpos.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(Codigo);
        }
    }

    private WebhookService Servico(RespostaFixa handler) =>
        new(new WebhookRepositorio(_db), new EntregaRepositorio(_db), _db, _relogio, new HttpClient(handler));

    [Fact]
    public async Task EntregaComSucessoEnviaHeadersEAssinatura()
    {
        var handler = new RespostaFixa();
        var svc = Servico(handler);
        var (wh, _) = await svc.ConfigurarAsync(ChavePix.Parse("loja@exemplo.com"), "https://exemplo.com/pix", "s3gr3do");
        _db.Entregas.Add(new EntregaWebhook(wh, "pix", "{\"pix\":[]}", _relogio.Agora));
        await _db.SaveChangesAsync();

        var (ok, falhas) = await svc.ProcessarPendentesAsync();
        ok.Should().Be(1);
        falhas.Should().Be(0);
        var req = handler.Recebidas.Single();
        req.RequestUri!.ToString().Should().Be("https://exemplo.com/pix");
        req.Headers.GetValues(WebhookService.HeaderEvento).Single().Should().Be("pix");
        req.Headers.GetValues(WebhookService.HeaderAssinatura).Single().Should().Be(Webhook.Assinar("s3gr3do", "{\"pix\":[]}"));
        handler.Corpos.Single().Should().Be("{\"pix\":[]}");
        (await _db.Entregas.SingleAsync()).Status.Should().Be(StatusEntrega.Entregue);
    }

    [Fact]
    public async Task FalhaReagendaComBackoffENaoReenviaAntesDaHora()
    {
        var handler = new RespostaFixa { Codigo = HttpStatusCode.InternalServerError };
        var svc = Servico(handler);
        var (wh, _) = await svc.ConfigurarAsync(ChavePix.Parse("loja@exemplo.com"), "https://exemplo.com/pix", "s");
        _db.Entregas.Add(new EntregaWebhook(wh, "pix", "{}", _relogio.Agora));
        await _db.SaveChangesAsync();

        (await svc.ProcessarPendentesAsync()).Falhas.Should().Be(1);
        var e = await _db.Entregas.SingleAsync();
        e.Tentativas.Should().Be(1);
        e.UltimoErro.Should().Be("HTTP 500");
        e.ProximaTentativa.Should().Be(_relogio.Agora.AddSeconds(5));

        // ainda não é hora: nada é enviado
        _relogio.Avancar(TimeSpan.FromSeconds(4));
        (await svc.ProcessarPendentesAsync()).Falhas.Should().Be(0);
        handler.Recebidas.Should().HaveCount(1);

        // passou o atraso: tenta de novo e agora dá certo
        _relogio.Avancar(TimeSpan.FromSeconds(1));
        handler.Codigo = HttpStatusCode.OK;
        (await svc.ProcessarPendentesAsync()).Entregues.Should().Be(1);
        handler.Recebidas.Should().HaveCount(2);
    }

    [Fact]
    public async Task ErroDeRedeTambemReagenda()
    {
        var handler = new HandlerQueExplode();
        var svc = new WebhookService(new WebhookRepositorio(_db), new EntregaRepositorio(_db), _db, _relogio, new HttpClient(handler));
        var (wh, _) = await svc.ConfigurarAsync(ChavePix.Parse("loja@exemplo.com"), "https://exemplo.com/pix", "s");
        _db.Entregas.Add(new EntregaWebhook(wh, "pix", "{}", _relogio.Agora));
        await _db.SaveChangesAsync();
        (await svc.ProcessarPendentesAsync()).Falhas.Should().Be(1);
        (await _db.Entregas.SingleAsync()).UltimoErro.Should().StartWith("HttpRequestException");
    }

    private sealed class HandlerQueExplode : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("conexão recusada");
    }
}
