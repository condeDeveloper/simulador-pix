namespace SimuladorPix.Core.Dominio;

public enum StatusCobranca
{
    Ativa,
    Concluida,
    RemovidaPeloUsuarioRecebedor,
    Expirada,
}

/// <summary>Cobrança imediata (cob): QR dinâmico com valor, expiração e devedor opcional. Recebe no máximo um pagamento.</summary>
public sealed class Cobranca
{
    public string Txid { get; private set; } = string.Empty;
    public int Revisao { get; private set; }
    public ChavePix Chave { get; private set; } = null!;
    public decimal Valor { get; private set; }
    public Pessoa? Devedor { get; private set; }
    public string? SolicitacaoPagador { get; private set; }
    public int ExpiracaoSegundos { get; private set; }
    public DateTimeOffset Criacao { get; private set; }
    public StatusCobranca Status { get; private set; }
    public string? EndToEndIdPagamento { get; private set; }
    public string? Location { get; private set; }

    private Cobranca() { }

    public Cobranca(string txid, ChavePix chave, decimal valor, int expiracaoSegundos, DateTimeOffset criacao, Pessoa? devedor, string? solicitacaoPagador)
    {
        Txid = Dominio.Txid.Validar(txid);
        Chave = chave ?? throw new DominioException("chave obrigatória");
        DefinirValor(valor);
        if (expiracaoSegundos <= 0 || expiracaoSegundos > 86400 * 30) throw new DominioException("expiração deve estar entre 1 segundo e 30 dias");
        ExpiracaoSegundos = expiracaoSegundos;
        Criacao = criacao;
        Devedor = devedor;
        SolicitacaoPagador = LimitarSolicitacao(solicitacaoPagador);
        Status = StatusCobranca.Ativa;
        Revisao = 0;
    }

    public DateTimeOffset ExpiraEm => Criacao.AddSeconds(ExpiracaoSegundos);

    public bool Vencida(DateTimeOffset agora) => agora >= ExpiraEm;

    public void DefinirLocation(string location) => Location = location;

    /// <summary>Revisão de cobrança ativa: valor, devedor, solicitação ou expiração. Incrementa a revisão.</summary>
    public void Revisar(decimal? valor, Pessoa? devedor, string? solicitacaoPagador, int? expiracaoSegundos, DateTimeOffset agora)
    {
        ExigirAtiva(agora);
        if (valor is { } v) DefinirValor(v);
        if (devedor is not null) Devedor = devedor;
        if (solicitacaoPagador is not null) SolicitacaoPagador = LimitarSolicitacao(solicitacaoPagador);
        if (expiracaoSegundos is { } e)
        {
            if (e <= 0 || e > 86400 * 30) throw new DominioException("expiração deve estar entre 1 segundo e 30 dias");
            ExpiracaoSegundos = e;
        }
        Revisao++;
    }

    public void Remover(DateTimeOffset agora)
    {
        ExigirAtiva(agora);
        Status = StatusCobranca.RemovidaPeloUsuarioRecebedor;
        Revisao++;
    }

    /// <summary>Marca como expirada se venceu. Devolve true se mudou.</summary>
    public bool ExpirarSeVencida(DateTimeOffset agora)
    {
        if (Status != StatusCobranca.Ativa || !Vencida(agora)) return false;
        Status = StatusCobranca.Expirada;
        return true;
    }

    /// <summary>Um Pix liquidou a cobrança. Valor precisa bater exatamente; cobrança precisa estar ativa e no prazo.</summary>
    public void Concluir(Pagamento pagamento, DateTimeOffset agora)
    {
        ExigirAtiva(agora);
        if (pagamento.Valor != Valor) throw new DominioException($"valor pago ({pagamento.Valor:0.00}) difere do valor da cobrança ({Valor:0.00})");
        Status = StatusCobranca.Concluida;
        EndToEndIdPagamento = pagamento.EndToEndId;
    }

    private void ExigirAtiva(DateTimeOffset agora)
    {
        if (ExpirarSeVencida(agora) || Status == StatusCobranca.Expirada) throw new DominioException($"cobrança {Txid} expirada");
        if (Status != StatusCobranca.Ativa) throw new DominioException($"cobrança {Txid} não está ativa ({Status})");
    }

    private void DefinirValor(decimal valor)
    {
        if (valor <= 0 || valor != decimal.Round(valor, 2)) throw new DominioException("valor deve ser positivo com até duas casas decimais");
        if (valor > 999_999_999.99m) throw new DominioException("valor acima do limite");
        Valor = valor;
    }

    private static string? LimitarSolicitacao(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length > 140 ? t[..140] : t;
    }
}
