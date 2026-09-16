using SimuladorPix.Core.Dominio;
using SimuladorPix.Core.Portas;

namespace SimuladorPix.Core.Aplicacao;

public sealed record CriarCobranca(ChavePix Chave, decimal Valor, int? ExpiracaoSegundos, Pessoa? Devedor, string? SolicitacaoPagador);

public sealed record RevisarCobranca(decimal? Valor, Pessoa? Devedor, string? SolicitacaoPagador, int? ExpiracaoSegundos);

/// <summary>Casos de uso de cobrança imediata (cob).</summary>
public sealed class CobrancaService
{
    private readonly ICobrancaRepositorio _cobrancas;
    private readonly IUnidadeDeTrabalho _uow;
    private readonly IRelogio _relogio;
    private readonly ParticipanteOptions _participante;

    public CobrancaService(ICobrancaRepositorio cobrancas, IUnidadeDeTrabalho uow, IRelogio relogio, ParticipanteOptions participante)
    {
        _cobrancas = cobrancas;
        _uow = uow;
        _relogio = relogio;
        _participante = participante;
    }

    /// <summary>
    /// PUT /cob/{txid}: cria a cobrança. Se o txid já existir com os mesmos dados, devolve a existente
    /// (idempotente); se existir com dados diferentes, é conflito.
    /// </summary>
    public async Task<(Cobranca Cobranca, bool Criada)> CriarAsync(string txid, CriarCobranca cmd, CancellationToken ct = default)
    {
        var existente = await _cobrancas.ObterAsync(txid, ct);
        if (existente is not null)
        {
            var igual = existente.Chave == cmd.Chave && existente.Valor == cmd.Valor
                        && existente.Devedor == cmd.Devedor && existente.SolicitacaoPagador == LimparTexto(cmd.SolicitacaoPagador);
            if (igual) return (existente, false);
            throw new ConflitoException($"txid {txid} já existe com dados diferentes; use PATCH para revisar");
        }
        var agora = _relogio.Agora;
        var cob = new Cobranca(txid, cmd.Chave, cmd.Valor, cmd.ExpiracaoSegundos ?? _participante.ExpiracaoPadraoSegundos, agora, cmd.Devedor, cmd.SolicitacaoPagador);
        cob.DefinirLocation($"{_participante.BaseLocation}/{Guid.NewGuid():N}");
        await _cobrancas.AdicionarAsync(cob, ct);
        await _uow.ConfirmarAsync(ct);
        return (cob, true);
    }

    /// <summary>POST /cob: cria com txid gerado pelo PSP.</summary>
    public Task<(Cobranca Cobranca, bool Criada)> CriarComTxidGeradoAsync(CriarCobranca cmd, CancellationToken ct = default) => CriarAsync(Txid.Gerar(), cmd, ct);

    public async Task<Cobranca> ObterAsync(string txid, CancellationToken ct = default)
    {
        var cob = await _cobrancas.ObterAsync(txid, ct) ?? throw new NaoEncontradoException("cobrança", txid);
        if (cob.ExpirarSeVencida(_relogio.Agora)) await _uow.ConfirmarAsync(ct);
        return cob;
    }

    public async Task<Cobranca> RevisarAsync(string txid, RevisarCobranca cmd, CancellationToken ct = default)
    {
        var cob = await ObterAsync(txid, ct);
        cob.Revisar(cmd.Valor, cmd.Devedor, cmd.SolicitacaoPagador, cmd.ExpiracaoSegundos, _relogio.Agora);
        await _uow.ConfirmarAsync(ct);
        return cob;
    }

    public async Task<Cobranca> RemoverAsync(string txid, CancellationToken ct = default)
    {
        var cob = await ObterAsync(txid, ct);
        cob.Remover(_relogio.Agora);
        await _uow.ConfirmarAsync(ct);
        return cob;
    }

    public Task<IReadOnlyList<Cobranca>> ListarAsync(DateTimeOffset inicio, DateTimeOffset fim, string? chave, StatusCobranca? status, int pagina, int tamanho, CancellationToken ct = default)
    {
        if (fim < inicio) throw new DominioException("fim anterior ao início");
        return _cobrancas.ListarAsync(inicio, fim, chave, status, Math.Max(0, pagina), Math.Clamp(tamanho, 1, 1000), ct);
    }

    /// <summary>Payload EMV (copia e cola) do QR dinâmico da cobrança.</summary>
    public string BrCodeDe(Cobranca cob) => BrCode.Gerar(new BrCode.Dados(cob.Chave, _participante.NomeRecebedor, _participante.Cidade, cob.Valor, cob.Txid,
        cob.SolicitacaoPagador, Estatico: false, Url: cob.Location));

    /// <summary>Expira em lote as cobranças ativas vencidas. Devolve quantas mudaram.</summary>
    public async Task<int> ExpirarVencidasAsync(int limite = 500, CancellationToken ct = default)
    {
        var agora = _relogio.Agora;
        var lista = await _cobrancas.AtivasVencidasAsync(agora, limite, ct);
        var n = 0;
        foreach (var c in lista) if (c.ExpirarSeVencida(agora)) n++;
        if (n > 0) await _uow.ConfirmarAsync(ct);
        return n;
    }

    private static string? LimparTexto(string? s) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > 140 ? s.Trim()[..140] : s.Trim());
}
