using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Core.Portas;

public interface ICobrancaRepositorio
{
    Task<Cobranca?> ObterAsync(string txid, CancellationToken ct = default);
    Task AdicionarAsync(Cobranca cobranca, CancellationToken ct = default);
    Task<IReadOnlyList<Cobranca>> ListarAsync(DateTimeOffset inicio, DateTimeOffset fim, string? chave, StatusCobranca? status, int pagina, int tamanho, CancellationToken ct = default);
    Task<IReadOnlyList<Cobranca>> AtivasVencidasAsync(DateTimeOffset agora, int limite, CancellationToken ct = default);
}

public interface IPagamentoRepositorio
{
    Task<Pagamento?> ObterAsync(string endToEndId, CancellationToken ct = default);
    Task AdicionarAsync(Pagamento pagamento, CancellationToken ct = default);
    Task<IReadOnlyList<Pagamento>> ListarAsync(DateTimeOffset inicio, DateTimeOffset fim, string? txid, int pagina, int tamanho, CancellationToken ct = default);
    Task<IReadOnlyList<Pagamento>> ComDevolucoesEmProcessamentoAsync(int limite, CancellationToken ct = default);
}

public interface IWebhookRepositorio
{
    Task<Webhook?> ObterAsync(string chave, CancellationToken ct = default);
    Task AdicionarAsync(Webhook webhook, CancellationToken ct = default);
    Task RemoverAsync(Webhook webhook, CancellationToken ct = default);
    Task<IReadOnlyList<Webhook>> ListarAsync(CancellationToken ct = default);
}

public interface IEntregaRepositorio
{
    Task AdicionarAsync(EntregaWebhook entrega, CancellationToken ct = default);
    Task<IReadOnlyList<EntregaWebhook>> ProntasAsync(DateTimeOffset agora, int limite, CancellationToken ct = default);
    Task<IReadOnlyList<EntregaWebhook>> PorChaveAsync(string chave, int limite, CancellationToken ct = default);
}

/// <summary>Confirma as mudanças pendentes (SaveChanges).</summary>
public interface IUnidadeDeTrabalho
{
    Task ConfirmarAsync(CancellationToken ct = default);
}
