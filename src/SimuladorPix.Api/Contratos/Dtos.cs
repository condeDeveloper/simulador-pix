using System.Globalization;
using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Api.Contratos;

// Contratos inspirados na API Pix do BCB (valores monetários como string "0.00").

public sealed record DevedorRequest(string? Nome, string? Cpf, string? Cnpj)
{
    public Pessoa? ParaPessoa() => Nome is null && Cpf is null && Cnpj is null ? null : Pessoa.Criar(Nome, Cpf ?? Cnpj);
}

public sealed record CalendarioRequest(int? Expiracao);
public sealed record ValorRequest(string Original);

public sealed record CobrancaRequest(CalendarioRequest? Calendario, DevedorRequest? Devedor, ValorRequest Valor, string Chave, string? SolicitacaoPagador)
{
    public decimal ValorDecimal() => Conversoes.Valor(Valor?.Original);
}

public sealed record RevisaoRequest(CalendarioRequest? Calendario, DevedorRequest? Devedor, ValorRequest? Valor, string? SolicitacaoPagador, string? Status);

public sealed record CobrancaResponse(object Calendario, string Txid, int Revisao, object? Loc, string Location, string Status, object? Devedor, object Valor,
    string Chave, string? SolicitacaoPagador, string? PixCopiaECola, object[]? Pix)
{
    public static CobrancaResponse De(Cobranca c, string? brCode, Pagamento? pagamento = null) => new(
        new { criacao = c.Criacao, expiracao = c.ExpiracaoSegundos },
        c.Txid, c.Revisao,
        c.Location is null ? null : new { location = c.Location, tipoCob = "cob" },
        c.Location ?? string.Empty,
        Conversoes.Status(c.Status),
        c.Devedor is null ? null : Conversoes.Pessoa(c.Devedor),
        new { original = Conversoes.Texto(c.Valor) },
        c.Chave.Valor, c.SolicitacaoPagador, brCode,
        pagamento is null ? null : new object[] { PixResponse.De(pagamento) });
}

public sealed record PagadorRequest(string Nome, string? Cpf, string? Cnpj);
public sealed record PagarRequest(PagadorRequest Pagador, string? InfoPagador, string? Valor);

public sealed record DevolucaoRequest(string Valor);

public sealed record DevolucaoResponse(string Id, string RtrId, string Valor, object Horario, string Status, string? Motivo)
{
    public static DevolucaoResponse De(Devolucao d) => new(d.Id, d.RtrId, Conversoes.Texto(d.Valor),
        new { solicitacao = d.Solicitacao, liquidacao = d.Liquidacao }, Conversoes.StatusDevolucao(d.Status), d.Motivo);
}

public sealed record PixResponse(string EndToEndId, string Txid, string Valor, DateTimeOffset Horario, object Pagador, string? InfoPagador, DevolucaoResponse[] Devolucoes)
{
    public static PixResponse De(Pagamento p) => new(p.EndToEndId, p.Txid, Conversoes.Texto(p.Valor), p.Horario, Conversoes.Pessoa(p.Pagador), p.InfoPagador,
        p.Devolucoes.Select(DevolucaoResponse.De).ToArray());
}

public sealed record WebhookRequest(string WebhookUrl, string? Segredo);

public sealed record WebhookResponse(string Chave, string WebhookUrl, DateTimeOffset Criacao, string? Segredo);

public sealed record EntregaResponse(Guid Id, string Evento, string Url, string Status, int Tentativas, DateTimeOffset Criacao, DateTimeOffset ProximaTentativa, DateTimeOffset? EntregueEm, string? UltimoErro, string Assinatura, string Corpo)
{
    public static EntregaResponse De(EntregaWebhook e) => new(e.Id, e.Evento, e.Url, e.Status.ToString().ToUpperInvariant(), e.Tentativas, e.Criacao, e.ProximaTentativa, e.EntregueEm, e.UltimoErro, e.Assinatura, e.Corpo);
}

public static class Conversoes
{
    public static decimal Valor(string? s)
    {
        if (s is null || !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) throw new DominioException("valor.original deve ser uma string no formato 0.00");
        return v;
    }

    public static string Texto(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public static object Pessoa(Pessoa p) => p.PessoaJuridica ? new { nome = p.Nome, cnpj = p.Documento } : new { nome = p.Nome, cpf = p.Documento };

    public static string Status(StatusCobranca s) => s switch
    {
        StatusCobranca.Ativa => "ATIVA",
        StatusCobranca.Concluida => "CONCLUIDA",
        StatusCobranca.RemovidaPeloUsuarioRecebedor => "REMOVIDA_PELO_USUARIO_RECEBEDOR",
        _ => "EXPIRADA",
    };

    public static string StatusDevolucao(StatusDevolucao s) => s switch
    {
        Core.Dominio.StatusDevolucao.EmProcessamento => "EM_PROCESSAMENTO",
        Core.Dominio.StatusDevolucao.Devolvida => "DEVOLVIDO",
        _ => "NAO_REALIZADO",
    };
}
