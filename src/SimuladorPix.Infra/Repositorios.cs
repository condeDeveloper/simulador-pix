using Microsoft.EntityFrameworkCore;
using SimuladorPix.Core.Dominio;
using SimuladorPix.Core.Portas;

namespace SimuladorPix.Infra;

public sealed class CobrancaRepositorio : ICobrancaRepositorio
{
    private readonly PixDbContext _db;
    public CobrancaRepositorio(PixDbContext db) => _db = db;

    public Task<Cobranca?> ObterAsync(string txid, CancellationToken ct = default) => _db.Cobrancas.FirstOrDefaultAsync(c => c.Txid == txid, ct);

    public async Task AdicionarAsync(Cobranca cobranca, CancellationToken ct = default) => await _db.Cobrancas.AddAsync(cobranca, ct);

    public async Task<IReadOnlyList<Cobranca>> ListarAsync(DateTimeOffset inicio, DateTimeOffset fim, string? chave, StatusCobranca? status, int pagina, int tamanho, CancellationToken ct = default)
    {
        var q = _db.Cobrancas.Where(c => c.Criacao >= inicio && c.Criacao <= fim);
        if (chave is not null) q = q.Where(c => c.Chave.Valor == chave);
        if (status is { } s) q = q.Where(c => c.Status == s);
        return await q.OrderByDescending(c => c.Criacao).Skip(pagina * tamanho).Take(tamanho).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Cobranca>> AtivasVencidasAsync(DateTimeOffset agora, int limite, CancellationToken ct = default)
    {
        // a expiração é derivada (criação + segundos); filtra no banco pelo status e refina em memória
        var candidatas = await _db.Cobrancas.Where(c => c.Status == StatusCobranca.Ativa).OrderBy(c => c.Criacao).Take(limite * 4).ToListAsync(ct);
        return candidatas.Where(c => c.Vencida(agora)).Take(limite).ToList();
    }
}

public sealed class PagamentoRepositorio : IPagamentoRepositorio
{
    private readonly PixDbContext _db;
    public PagamentoRepositorio(PixDbContext db) => _db = db;

    public Task<Pagamento?> ObterAsync(string endToEndId, CancellationToken ct = default) => _db.Pagamentos.FirstOrDefaultAsync(p => p.EndToEndId == endToEndId, ct);

    public async Task AdicionarAsync(Pagamento pagamento, CancellationToken ct = default) => await _db.Pagamentos.AddAsync(pagamento, ct);

    public async Task<IReadOnlyList<Pagamento>> ListarAsync(DateTimeOffset inicio, DateTimeOffset fim, string? txid, int pagina, int tamanho, CancellationToken ct = default)
    {
        var q = _db.Pagamentos.Where(p => p.Horario >= inicio && p.Horario <= fim);
        if (txid is not null) q = q.Where(p => p.Txid == txid);
        return await q.OrderByDescending(p => p.Horario).Skip(pagina * tamanho).Take(tamanho).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Pagamento>> ComDevolucoesEmProcessamentoAsync(int limite, CancellationToken ct = default) =>
        await _db.Pagamentos.Where(p => p.Devolucoes.Any(d => d.Status == StatusDevolucao.EmProcessamento)).OrderBy(p => p.Horario).Take(limite).ToListAsync(ct);
}

public sealed class WebhookRepositorio : IWebhookRepositorio
{
    private readonly PixDbContext _db;
    public WebhookRepositorio(PixDbContext db) => _db = db;

    public Task<Webhook?> ObterAsync(string chave, CancellationToken ct = default) => _db.Webhooks.FirstOrDefaultAsync(w => w.Chave == chave, ct);
    public async Task AdicionarAsync(Webhook webhook, CancellationToken ct = default) => await _db.Webhooks.AddAsync(webhook, ct);
    public Task RemoverAsync(Webhook webhook, CancellationToken ct = default) { _db.Webhooks.Remove(webhook); return Task.CompletedTask; }
    public async Task<IReadOnlyList<Webhook>> ListarAsync(CancellationToken ct = default) => await _db.Webhooks.OrderBy(w => w.Chave).ToListAsync(ct);
}

public sealed class EntregaRepositorio : IEntregaRepositorio
{
    private readonly PixDbContext _db;
    public EntregaRepositorio(PixDbContext db) => _db = db;

    public async Task AdicionarAsync(EntregaWebhook entrega, CancellationToken ct = default) => await _db.Entregas.AddAsync(entrega, ct);

    public async Task<IReadOnlyList<EntregaWebhook>> ProntasAsync(DateTimeOffset agora, int limite, CancellationToken ct = default) =>
        await _db.Entregas.Where(e => e.Status == StatusEntrega.Pendente && e.ProximaTentativa <= agora).OrderBy(e => e.ProximaTentativa).Take(limite).ToListAsync(ct);

    public async Task<IReadOnlyList<EntregaWebhook>> PorChaveAsync(string chave, int limite, CancellationToken ct = default) =>
        await _db.Entregas.Where(e => e.Chave == chave).OrderByDescending(e => e.Criacao).Take(limite).ToListAsync(ct);
}
