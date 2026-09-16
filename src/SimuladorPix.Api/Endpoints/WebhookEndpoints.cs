using System.Collections.Concurrent;
using SimuladorPix.Api.Contratos;
using SimuladorPix.Core.Aplicacao;
using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Api.Endpoints;

public static class WebhookEndpoints
{
    /// <summary>Últimas notificações recebidas pelo receptor de demonstração.</summary>
    private static readonly ConcurrentQueue<object> Recebidas = new();

    public static IEndpointRouteBuilder MapWebhooks(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/webhook").WithTags("Webhooks");

        g.MapPut("/{chave}", async (string chave, WebhookRequest req, WebhookService svc, CancellationToken ct) =>
        {
            var (wh, criado) = await svc.ConfigurarAsync(ChavePix.Parse(chave), req.WebhookUrl, req.Segredo, ct);
            var corpo = new WebhookResponse(wh.Chave, wh.Url, wh.Criacao, criado || req.Segredo is not null ? wh.Segredo : null);
            return criado ? Results.Created($"/api/webhook/{wh.Chave}", corpo) : Results.Ok(corpo);
        }).WithSummary("Configura o webhook da chave. O segredo HMAC é gerado se não for informado e devolvido só na criação");

        g.MapGet("/{chave}", async (string chave, WebhookService svc, CancellationToken ct) =>
        {
            var wh = await svc.ObterAsync(chave, ct);
            return Results.Ok(new WebhookResponse(wh.Chave, wh.Url, wh.Criacao, null));
        }).WithSummary("Consulta o webhook da chave");

        g.MapGet("/", async (WebhookService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListarAsync(ct)).Select(w => new WebhookResponse(w.Chave, w.Url, w.Criacao, null))))
            .WithSummary("Lista webhooks");

        g.MapDelete("/{chave}", async (string chave, WebhookService svc, CancellationToken ct) => { await svc.RemoverAsync(chave, ct); return Results.NoContent(); })
            .WithSummary("Remove o webhook da chave");

        g.MapGet("/{chave}/entregas", async (string chave, int? limite, WebhookService svc, CancellationToken ct) =>
            Results.Ok((await svc.EntregasAsync(chave, limite ?? 50, ct)).Select(EntregaResponse.De)))
            .WithSummary("Histórico de entregas: tentativas, próxima tentativa, erro e assinatura");

        // Receptor de demonstração: aponte o webhook para http://localhost:5000/api/simulacao/receptor e veja as notificações aqui.
        var sim = app.MapGroup("/api/simulacao").WithTags("Simulação do pagador");

        sim.MapPost("/receptor", async (HttpRequest req) =>
        {
            using var leitor = new StreamReader(req.Body);
            var corpo = await leitor.ReadToEndAsync();
            var item = new
            {
                recebidoEm = DateTimeOffset.UtcNow,
                evento = req.Headers[WebhookService.HeaderEvento].ToString(),
                entrega = req.Headers[WebhookService.HeaderEntrega].ToString(),
                assinatura = req.Headers[WebhookService.HeaderAssinatura].ToString(),
                corpo,
            };
            Recebidas.Enqueue(item);
            while (Recebidas.Count > 100 && Recebidas.TryDequeue(out _)) { }
            return Results.Ok(new { ok = true });
        }).WithSummary("Receptor de webhooks para demonstração local (não exige api key)");

        sim.MapGet("/receptor", () => Results.Ok(Recebidas.Reverse()))
            .WithSummary("Notificações recebidas pelo receptor de demonstração");

        return app;
    }
}
