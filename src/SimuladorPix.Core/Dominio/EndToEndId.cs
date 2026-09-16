using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SimuladorPix.Core.Dominio;

/// <summary>
/// Identificador único de uma transação Pix no SPI: "E" + ISPB do participante (8 dígitos) +
/// data e hora (yyyyMMddHHmm) + 11 caracteres alfanuméricos. Ex.: E12345678202609152230aBcDeF12345
/// </summary>
public static partial class EndToEndId
{
    private const string Alfabeto = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string Gerar(string ispb, DateTimeOffset momento)
    {
        if (ispb is null || ispb.Length != 8 || !ispb.All(char.IsDigit)) throw new DominioException("ISPB deve ter 8 dígitos");
        var sufixo = string.Create(11, Alfabeto, (span, alf) => { for (var i = 0; i < span.Length; i++) span[i] = alf[RandomNumberGenerator.GetInt32(alf.Length)]; });
        return "E" + ispb + momento.ToUniversalTime().ToString("yyyyMMddHHmm") + sufixo;
    }

    public static bool Valido(string? e2e) => e2e is not null && Formato().IsMatch(e2e);

    public static string Validar(string? e2e) => Valido(e2e) ? e2e! : throw new DominioException("endToEndId inválido");

    /// <summary>Identificador de devolução (rtrId): "D" + ISPB + data/hora + 11 caracteres.</summary>
    public static string GerarDevolucao(string ispb, DateTimeOffset momento) => "D" + Gerar(ispb, momento)[1..];

    [GeneratedRegex("^[ED][0-9]{8}[0-9]{12}[a-zA-Z0-9]{11}$")]
    private static partial Regex Formato();
}
