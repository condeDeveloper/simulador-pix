using System.Globalization;
using System.Text;

namespace SimuladorPix.Core.Dominio;

/// <summary>
/// Payload EMV-MPM do Pix ("copia e cola" / QR Code), conforme o Manual de Padrões para Iniciação do Pix do BCB.
/// Monta e lê a estrutura TLV (id de 2 dígitos + tamanho de 2 dígitos + valor) e valida o CRC do campo 63.
/// </summary>
public static class BrCode
{
    public const string GuiPix = "br.gov.bcb.pix";

    public sealed record Dados(ChavePix Chave, string NomeRecebedor, string Cidade, decimal? Valor = null, string? Txid = null,
        string? Descricao = null, bool Estatico = true, string? Url = null);

    public static string Gerar(Dados d)
    {
        var nome = Ascii(d.NomeRecebedor, 25);
        var cidade = Ascii(d.Cidade, 15);
        if (nome.Length == 0 || cidade.Length == 0) throw new DominioException("nome do recebedor e cidade são obrigatórios");
        var txid = string.IsNullOrWhiteSpace(d.Txid) ? "***" : d.Txid!;
        if (txid != "***" && !Txid.Valido(txid)) throw new DominioException("txid inválido");

        var conta = new StringBuilder(Tlv("00", GuiPix));
        if (d.Url is not null) conta.Append(Tlv("25", d.Url));
        else conta.Append(Tlv("01", d.Chave.Valor));
        if (!string.IsNullOrWhiteSpace(d.Descricao) && d.Url is null) conta.Append(Tlv("02", Ascii(d.Descricao!, 72 - conta.Length)));

        var sb = new StringBuilder();
        sb.Append(Tlv("00", "01"));
        if (!d.Estatico) sb.Append(Tlv("01", "12")); // ponto de iniciação: 12 = QR dinâmico (uso único)
        sb.Append(Tlv("26", conta.ToString()));
        sb.Append(Tlv("52", "0000"));
        sb.Append(Tlv("53", "986"));
        if (d.Valor is { } v)
        {
            if (v <= 0) throw new DominioException("valor do BR Code deve ser positivo");
            sb.Append(Tlv("54", v.ToString("0.00", CultureInfo.InvariantCulture)));
        }
        sb.Append(Tlv("58", "BR"));
        sb.Append(Tlv("59", nome));
        sb.Append(Tlv("60", cidade));
        sb.Append(Tlv("62", Tlv("05", txid)));
        sb.Append("6304");
        sb.Append(Crc16.Hex(sb.ToString()));
        return sb.ToString();
    }

    /// <summary>Lê o payload em um dicionário id → valor (o campo 26 é devolvido bruto). Valida o CRC.</summary>
    public static IReadOnlyDictionary<string, string> Ler(string payload)
    {
        if (payload.Length < 8) throw new DominioException("BR Code muito curto");
        var corpo = payload[..^4];
        var crc = payload[^4..];
        if (!corpo.EndsWith("6304", StringComparison.Ordinal)) throw new DominioException("BR Code sem campo CRC");
        if (!string.Equals(Crc16.Hex(corpo), crc, StringComparison.OrdinalIgnoreCase)) throw new DominioException("CRC do BR Code não confere");
        return LerTlv(payload);
    }

    /// <summary>Decompõe uma sequência TLV.</summary>
    public static IReadOnlyDictionary<string, string> LerTlv(string s)
    {
        var d = new Dictionary<string, string>();
        var i = 0;
        while (i + 4 <= s.Length)
        {
            var id = s.Substring(i, 2);
            if (!int.TryParse(s.AsSpan(i + 2, 2), out var tam)) throw new DominioException($"tamanho inválido no campo {id}");
            if (i + 4 + tam > s.Length) throw new DominioException($"campo {id} maior que o payload");
            d[id] = s.Substring(i + 4, tam);
            i += 4 + tam;
        }
        return d;
    }

    private static string Tlv(string id, string valor)
    {
        if (valor.Length > 99) throw new DominioException($"campo {id} excede 99 caracteres");
        return id + valor.Length.ToString("00") + valor;
    }

    /// <summary>Remove acentos e limita ao tamanho, como exigem os campos 59 e 60.</summary>
    internal static string Ascii(string texto, int max)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalizado)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (c < 128) sb.Append(c);
        }
        var r = sb.ToString().Trim();
        return r.Length > max ? r[..max].TrimEnd() : r;
    }
}
