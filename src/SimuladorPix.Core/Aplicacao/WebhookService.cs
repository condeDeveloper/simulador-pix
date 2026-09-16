using System.Net.Http.Headers;
using System.Text;
using SimuladorPix.Core.Dominio;
using SimuladorPix.Core.Portas;

namespace SimuladorPix.Core.Aplicacao;

/// <summary>Configuração de webhooks por chave e entrega com reentrega exponencial.</summary>
public sealed class WebhookService
{
    public const string HeaderAssinatura = "x-pix-assinatura";
    public const string HeaderEvento = "x-pix-evento";
    public const string HeaderEntrega = "x-pix-entrega";

    private readonly IWebhookRepositorio _webhooks;
    private readonly IEntregaRepositorio _entregas;
    private readonly IUnidadeDeTrabalho _uow;
    private readonly IRelogio _relogio;
    private readonly HttpClient _http;

    public WebhookService(IWebhookRepositorio webhooks, IEntregaRepositorio entregas, IUnidadeDeTrabalho uow, IRelogio relogio, HttpClient http)
    {
        _webhooks = webhooks;
        _entregas = entregas;
        _uow = uow;
        _relogio = relogio;
        _http = http;
    }

    /// <summary>PUT /webhook/{chave}: cria ou atualiza. Devolve o segredo só na criação ou quando informado.</summary>
    public async Task<(Webhook Webhook, bool Criado)> ConfigurarAsync(ChavePix chave, string url, string? segredo, CancellationToken ct = default)
    {
        var existente = await _webhooks.ObterAsync(chave.Valor, ct);
        if (existente is not null)
        {
            existente.Atualizar(url, segredo);
            await _uow.ConfirmarAsync(ct);
            return (existente, false);
        }
        var wh = new Webhook(chave, url, segredo, _relogio.Agora);
        await _webhooks.AdicionarAsync(wh, ct);
        await _uow.ConfirmarAsync(ct);
        return (wh, true);
    }

    public async Task<Webhook> ObterAsync(string chave, CancellationToken ct = default) =>
        await _webhooks.ObterAsync(ChavePix.Parse(chave).Valor, ct) ?? throw new NaoEncontradoException("webhook", chave);

    public Task<IReadOnlyList<Webhook>> ListarAsync(CancellationToken ct = default) => _webhooks.ListarAsync(ct);

    public async Task RemoverAsync(string chave, CancellationToken ct = default)
    {
        var wh = await ObterAsync(chave, ct);
        await _webhooks.RemoverAsync(wh, ct);
        await _uow.ConfirmarAsync(ct);
    }

    public Task<IReadOnlyList<EntregaWebhook>> EntregasAsync(string chave, int limite = 50, CancellationToken ct = default) =>
        _entregas.PorChaveAsync(ChavePix.Parse(chave).Valor, Math.Clamp(limite, 1, 500), ct);

    /// <summary>Worker: envia as entregas prontas. Sucesso = 2xx. Falha reagenda com backoff até o limite.</summary>
    public async Task<(int Entregues, int Falhas)> ProcessarPendentesAsync(int limite = 50, CancellationToken ct = default)
    {
        var agora = _relogio.Agora;
        var prontas = await _entregas.ProntasAsync(agora, limite, ct);
        int ok = 0, falhas = 0;
        foreach (var e in prontas)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, e.Url) { Content = new StringContent(e.Corpo, Encoding.UTF8, "application/json") };
                req.Headers.TryAddWithoutValidation(HeaderAssinatura, e.Assinatura);
                req.Headers.TryAddWithoutValidation(HeaderEvento, e.Evento);
                req.Headers.TryAddWithoutValidation(HeaderEntrega, e.Id.ToString());
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SimuladorPix", "1.0"));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var resp = await _http.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode) { e.RegistrarSucesso(_relogio.Agora); ok++; }
                else { e.RegistrarFalha($"HTTP {(int)resp.StatusCode}", _relogio.Agora); falhas++; }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                e.RegistrarFalha(ex.GetType().Name + ": " + ex.Message, _relogio.Agora);
                falhas++;
            }
        }
        if (prontas.Count > 0) await _uow.ConfirmarAsync(ct);
        return (ok, falhas);
    }
}
