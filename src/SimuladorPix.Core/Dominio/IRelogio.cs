namespace SimuladorPix.Core.Dominio;

public interface IRelogio
{
    DateTimeOffset Agora { get; }
}

public sealed class RelogioDoSistema : IRelogio
{
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}

public sealed class RelogioFixo : IRelogio
{
    public RelogioFixo(DateTimeOffset inicio) => Agora = inicio;
    public DateTimeOffset Agora { get; private set; }
    public void Avancar(TimeSpan delta) => Agora += delta;
    public void Definir(DateTimeOffset momento) => Agora = momento;
}
