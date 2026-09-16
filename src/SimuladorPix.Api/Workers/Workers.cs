using SimuladorPix.Core.Aplicacao;

namespace SimuladorPix.Api.Workers;

/// <summary>Roda periodicamente: entrega webhooks, liquida devoluções e expira cobranças vencidas.</summary>
public sealed class ProcessadorPeriodico : BackgroundService
{
    private readonly IServiceScopeFactory _escopos;
    private readonly ILogger<ProcessadorPeriodico> _log;
    private readonly TimeSpan _intervalo;

    public ProcessadorPeriodico(IServiceScopeFactory escopos, IConfiguration config, ILogger<ProcessadorPeriodico> log)
    {
        _escopos = escopos;
        _log = log;
        _intervalo = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Workers:IntervaloSegundos", 2)));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_intervalo);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await RodarUmaVezAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e) { _log.LogWarning(e, "ciclo do processador falhou"); }
        }
    }

    public async Task RodarUmaVezAsync(CancellationToken ct)
    {
        await using var escopo = _escopos.CreateAsyncScope();
        var sp = escopo.ServiceProvider;
        var expiradas = await sp.GetRequiredService<CobrancaService>().ExpirarVencidasAsync(ct: ct);
        var devolucoes = await sp.GetRequiredService<PixService>().LiquidarDevolucoesPendentesAsync(ct: ct);
        var (entregues, falhas) = await sp.GetRequiredService<WebhookService>().ProcessarPendentesAsync(ct: ct);
        if (expiradas + devolucoes + entregues + falhas > 0)
            _log.LogInformation("ciclo: {Exp} expiradas, {Dev} devoluções liquidadas, {Ok} webhooks entregues, {Falhas} falhas", expiradas, devolucoes, entregues, falhas);
    }
}
