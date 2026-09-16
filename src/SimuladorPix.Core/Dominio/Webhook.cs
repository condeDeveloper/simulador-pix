using System.Security.Cryptography;
using System.Text;

namespace SimuladorPix.Core.Dominio;

/// <summary>Webhook configurado por chave recebedora: URL de destino e segredo para assinatura HMAC.</summary>
public sealed class Webhook
{
    public string Chave { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public string Segredo { get; private set; } = string.Empty;
    public DateTimeOffset Criacao { get; private set; }

    private Webhook() { }

    public Webhook(ChavePix chave, string url, string? segredo, DateTimeOffset criacao)
    {
        Chave = chave.Valor;
        Url = ValidarUrl(url);
        Segredo = string.IsNullOrWhiteSpace(segredo) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() : segredo.Trim();
        Criacao = criacao;
    }

    public void Atualizar(string url, string? segredo)
    {
        Url = ValidarUrl(url);
        if (!string.IsNullOrWhiteSpace(segredo)) Segredo = segredo.Trim();
    }

    /// <summary>Assinatura HMAC-SHA256 do corpo, em hexadecimal minúsculo, enviada no header x-pix-assinatura.</summary>
    public static string Assinar(string segredo, string corpo)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(segredo));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(corpo))).ToLowerInvariant();
    }

    public static bool AssinaturaValida(string segredo, string corpo, string? assinatura) =>
        assinatura is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Assinar(segredo, corpo)), Encoding.UTF8.GetBytes(assinatura.ToLowerInvariant()));

    private static string ValidarUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new DominioException("URL do webhook inválida");
        var local = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local)) throw new DominioException("webhook exige https (http só em localhost)");
        return uri.ToString();
    }
}

public enum StatusEntrega
{
    Pendente,
    Entregue,
    Falhou,
}

/// <summary>Uma notificação a entregar, com tentativas e backoff exponencial.</summary>
public sealed class EntregaWebhook
{
    public const int MaximoTentativas = 8;

    public Guid Id { get; private set; }
    public string Chave { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public string Evento { get; private set; } = string.Empty;
    public string Corpo { get; private set; } = string.Empty;
    public string Assinatura { get; private set; } = string.Empty;
    public int Tentativas { get; private set; }
    public DateTimeOffset ProximaTentativa { get; private set; }
    public StatusEntrega Status { get; private set; }
    public string? UltimoErro { get; private set; }
    public DateTimeOffset Criacao { get; private set; }
    public DateTimeOffset? EntregueEm { get; private set; }

    private EntregaWebhook() { }

    public EntregaWebhook(Webhook webhook, string evento, string corpo, DateTimeOffset agora)
    {
        Id = Guid.NewGuid();
        Chave = webhook.Chave;
        Url = webhook.Url;
        Evento = evento;
        Corpo = corpo;
        Assinatura = Webhook.Assinar(webhook.Segredo, corpo);
        Criacao = agora;
        ProximaTentativa = agora;
        Status = StatusEntrega.Pendente;
    }

    /// <summary>Atraso antes da tentativa n (1-based): 5s, 10s, 20s, 40s, ... limitado a 1 hora.</summary>
    public static TimeSpan Atraso(int tentativa) => TimeSpan.FromSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Max(0, tentativa - 1))));

    public void RegistrarSucesso(DateTimeOffset agora)
    {
        Tentativas++;
        Status = StatusEntrega.Entregue;
        EntregueEm = agora;
        UltimoErro = null;
    }

    public void RegistrarFalha(string erro, DateTimeOffset agora)
    {
        Tentativas++;
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;
        if (Tentativas >= MaximoTentativas) { Status = StatusEntrega.Falhou; return; }
        ProximaTentativa = agora + Atraso(Tentativas);
    }

    public bool Pronta(DateTimeOffset agora) => Status == StatusEntrega.Pendente && ProximaTentativa <= agora;
}
