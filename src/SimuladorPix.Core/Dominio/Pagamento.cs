namespace SimuladorPix.Core.Dominio;

public enum StatusDevolucao
{
    EmProcessamento,
    Devolvida,
    NaoRealizada,
}

/// <summary>Devolução (refund) total ou parcial de um Pix recebido.</summary>
public sealed class Devolucao
{
    public string Id { get; private set; } = string.Empty;
    public string RtrId { get; private set; } = string.Empty;
    public decimal Valor { get; private set; }
    public StatusDevolucao Status { get; private set; }
    public DateTimeOffset Solicitacao { get; private set; }
    public DateTimeOffset? Liquidacao { get; private set; }
    public string? Motivo { get; private set; }

    private Devolucao() { }

    internal Devolucao(string id, string rtrId, decimal valor, DateTimeOffset solicitacao)
    {
        Id = id; RtrId = rtrId; Valor = valor; Solicitacao = solicitacao; Status = StatusDevolucao.EmProcessamento;
    }

    internal void Liquidar(DateTimeOffset agora) { Status = StatusDevolucao.Devolvida; Liquidacao = agora; }
    internal void Rejeitar(string motivo, DateTimeOffset agora) { Status = StatusDevolucao.NaoRealizada; Motivo = motivo; Liquidacao = agora; }
}

/// <summary>Pix recebido que liquidou uma cobrança. Pode ter várias devoluções, até o valor recebido.</summary>
public sealed class Pagamento
{
    private readonly List<Devolucao> _devolucoes = new();

    public string EndToEndId { get; private set; } = string.Empty;
    public string Txid { get; private set; } = string.Empty;
    public decimal Valor { get; private set; }
    public DateTimeOffset Horario { get; private set; }
    public Pessoa Pagador { get; private set; } = null!;
    public string? InfoPagador { get; private set; }
    public IReadOnlyList<Devolucao> Devolucoes => _devolucoes;

    private Pagamento() { }

    public Pagamento(string endToEndId, string txid, decimal valor, DateTimeOffset horario, Pessoa pagador, string? infoPagador)
    {
        EndToEndId = Dominio.EndToEndId.Validar(endToEndId);
        Txid = Dominio.Txid.Validar(txid);
        if (valor <= 0 || valor != decimal.Round(valor, 2)) throw new DominioException("valor deve ser positivo com até duas casas decimais");
        Valor = valor;
        Horario = horario;
        Pagador = pagador ?? throw new DominioException("pagador obrigatório");
        InfoPagador = string.IsNullOrWhiteSpace(infoPagador) ? null : infoPagador.Trim();
    }

    public decimal ValorDevolvido => _devolucoes.Where(d => d.Status != StatusDevolucao.NaoRealizada).Sum(d => d.Valor);
    public decimal SaldoDevolvivel => Valor - ValorDevolvido;

    /// <summary>Solicita devolução. O id é escolhido pelo recebedor (idempotente: mesmo id devolve a mesma devolução).</summary>
    public Devolucao SolicitarDevolucao(string id, decimal valor, string rtrId, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 35 || !id.All(char.IsLetterOrDigit)) throw new DominioException("id da devolução deve ser alfanumérico com até 35 caracteres");
        var existente = _devolucoes.FirstOrDefault(d => d.Id == id);
        if (existente is not null)
        {
            if (existente.Valor != valor) throw new ConflitoException($"devolução {id} já existe com outro valor");
            return existente;
        }
        if (valor <= 0 || valor != decimal.Round(valor, 2)) throw new DominioException("valor da devolução deve ser positivo com até duas casas decimais");
        if (valor > SaldoDevolvivel) throw new DominioException($"valor da devolução ({valor:0.00}) excede o saldo devolvível ({SaldoDevolvivel:0.00})");
        if (agora > Horario.AddDays(90)) throw new DominioException("prazo de 90 dias para devolução encerrado");
        var d = new Devolucao(id, rtrId, valor, agora);
        _devolucoes.Add(d);
        return d;
    }

    public Devolucao ObterDevolucao(string id) => _devolucoes.FirstOrDefault(d => d.Id == id) ?? throw new NaoEncontradoException("devolução", id);

    internal void LiquidarDevolucao(string id, DateTimeOffset agora) => ObterDevolucao(id).Liquidar(agora);
}
