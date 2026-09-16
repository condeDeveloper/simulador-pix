namespace SimuladorPix.Core.Dominio;

/// <summary>Devedor de uma cobrança ou pagador de um Pix: nome e CPF ou CNPJ.</summary>
public sealed record Pessoa(string Nome, string Documento)
{
    public static Pessoa Criar(string? nome, string? documento)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim().Length > 200) throw new DominioException("nome obrigatório (até 200 caracteres)");
        return new Pessoa(nome.Trim(), Dominio.Documento.Normalizar(documento));
    }

    public bool PessoaJuridica => Documento.Length == 14;
}
