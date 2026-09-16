namespace SimuladorPix.Core.Dominio;

/// <summary>CPF e CNPJ: normalização e validação pelos dígitos verificadores.</summary>
public static class Documento
{
    public static bool CpfValido(string? cpf)
    {
        var d = SoDigitos(cpf);
        if (d.Length != 11 || d.Distinct().Count() == 1) return false;
        return Dv(d, 9, 10) == d[9] - '0' && Dv(d, 10, 11) == d[10] - '0';
    }

    public static bool CnpjValido(string? cnpj)
    {
        var d = SoDigitos(cnpj);
        if (d.Length != 14 || d.Distinct().Count() == 1) return false;
        int[] p1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, p2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        return DvCnpj(d, p1) == d[12] - '0' && DvCnpj(d, p2) == d[13] - '0';
    }

    /// <summary>Devolve só os dígitos se for um CPF ou CNPJ válido; senão lança.</summary>
    public static string Normalizar(string? documento)
    {
        var d = SoDigitos(documento);
        if (d.Length == 11 && CpfValido(d)) return d;
        if (d.Length == 14 && CnpjValido(d)) return d;
        throw new DominioException("documento inválido: informe um CPF ou CNPJ válido");
    }

    public static string SoDigitos(string? s) => new((s ?? string.Empty).Where(char.IsDigit).ToArray());

    private static int Dv(string cpf, int n, int peso)
    {
        var soma = 0;
        for (var i = 0; i < n; i++) soma += (cpf[i] - '0') * (peso - i);
        var resto = soma * 10 % 11;
        return resto == 10 ? 0 : resto;
    }

    private static int DvCnpj(string cnpj, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++) soma += (cnpj[i] - '0') * pesos[i];
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
