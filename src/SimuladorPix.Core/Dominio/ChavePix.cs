using System.Text.RegularExpressions;

namespace SimuladorPix.Core.Dominio;

public enum TipoChave
{
    Cpf,
    Cnpj,
    Email,
    Telefone,
    /// <summary>Chave aleatória (UUID) gerada pelo DICT.</summary>
    Aleatoria,
}

/// <summary>Chave Pix normalizada, no formato que vai para o DICT e para o BR Code.</summary>
public sealed partial record ChavePix(TipoChave Tipo, string Valor)
{
    public static ChavePix Parse(string? entrada)
    {
        var s = (entrada ?? string.Empty).Trim();
        if (s.Length == 0) throw new DominioException("chave Pix obrigatória");

        if (Guid.TryParseExact(s, "D", out var guid)) return new ChavePix(TipoChave.Aleatoria, guid.ToString("D").ToLowerInvariant());

        if (s.Contains('@'))
        {
            var email = s.ToLowerInvariant();
            if (email.Length > 77 || !Email().IsMatch(email)) throw new DominioException("chave de e-mail inválida");
            return new ChavePix(TipoChave.Email, email);
        }

        if (s.StartsWith('+'))
        {
            var digitos = Documento.SoDigitos(s);
            if (!digitos.StartsWith("55") || digitos.Length is < 12 or > 13) throw new DominioException("chave de telefone deve ser +55 DDD número");
            return new ChavePix(TipoChave.Telefone, "+" + digitos);
        }

        var doc = Documento.SoDigitos(s);
        if (doc.Length == 11 && Documento.CpfValido(doc)) return new ChavePix(TipoChave.Cpf, doc);
        if (doc.Length == 14 && Documento.CnpjValido(doc)) return new ChavePix(TipoChave.Cnpj, doc);
        if (doc.Length is 10 or 11 && doc == s) throw new DominioException("telefone como chave precisa do prefixo +55");

        throw new DominioException("chave Pix inválida: use CPF, CNPJ, e-mail, telefone (+55...) ou chave aleatória");
    }

    public static bool TentarParse(string? entrada, out ChavePix? chave)
    {
        try { chave = Parse(entrada); return true; }
        catch (DominioException) { chave = null; return false; }
    }

    public static ChavePix NovaAleatoria() => new(TipoChave.Aleatoria, Guid.NewGuid().ToString("D"));

    public override string ToString() => Valor;

    [GeneratedRegex(@"^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}$")]
    private static partial Regex Email();
}
