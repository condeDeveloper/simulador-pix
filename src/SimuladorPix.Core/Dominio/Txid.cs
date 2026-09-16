using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SimuladorPix.Core.Dominio;

/// <summary>Identificador da cobrança: 26 a 35 caracteres alfanuméricos, como na API Pix.</summary>
public static partial class Txid
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static bool Valido(string? txid) => txid is not null && Formato().IsMatch(txid);

    public static string Validar(string? txid) => Valido(txid) ? txid! : throw new DominioException("txid deve ter de 26 a 35 caracteres alfanuméricos");

    public static string Gerar(int tamanho = 32)
    {
        if (tamanho is < 26 or > 35) throw new ArgumentOutOfRangeException(nameof(tamanho));
        return string.Create(tamanho, Alfabeto, (span, alf) => { for (var i = 0; i < span.Length; i++) span[i] = alf[RandomNumberGenerator.GetInt32(alf.Length)]; });
    }

    [GeneratedRegex("^[A-Za-z0-9]{26,35}$")]
    private static partial Regex Formato();
}
