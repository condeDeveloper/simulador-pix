namespace SimuladorPix.Core.Dominio;

/// <summary>Violação de regra de negócio. Vira 422 na API.</summary>
public class DominioException : Exception
{
    public DominioException(string mensagem) : base(mensagem) { }
}

/// <summary>Recurso inexistente. Vira 404 na API.</summary>
public sealed class NaoEncontradoException : DominioException
{
    public NaoEncontradoException(string recurso, string identificador) : base($"{recurso} não encontrado: {identificador}") { }
}

/// <summary>Estado conflitante. Vira 409 na API.</summary>
public sealed class ConflitoException : DominioException
{
    public ConflitoException(string mensagem) : base(mensagem) { }
}
