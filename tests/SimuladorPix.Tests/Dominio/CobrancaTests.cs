using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Tests.Dominio;

public class CobrancaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly ChavePix Chave = ChavePix.Parse("loja@exemplo.com");
    private static readonly string Tx = "AbCdEfGhIjKlMnOpQrStUvWxYz0123";

    private static Cobranca Nova(decimal valor = 100m, int expiracao = 3600) => new(Tx, Chave, valor, expiracao, T0, Pessoa.Criar("Fulano", "529.982.247-25"), "Pedido 42");

    private static Pagamento Pag(decimal valor, DateTimeOffset? quando = null) =>
        new(EndToEndId.Gerar("12345678", quando ?? T0), Tx, valor, quando ?? T0, Pessoa.Criar("Fulano", "52998224725"), null);

    [Fact]
    public void NasceAtivaComExpiracao()
    {
        var c = Nova();
        c.Status.Should().Be(StatusCobranca.Ativa);
        c.ExpiraEm.Should().Be(T0.AddHours(1));
        c.Vencida(T0.AddMinutes(59)).Should().BeFalse();
        c.Vencida(T0.AddHours(1)).Should().BeTrue();
        c.Devedor!.Documento.Should().Be("52998224725");
    }

    [Fact]
    public void ValidaValorEExpiracao()
    {
        var zero = () => new Cobranca(Tx, Chave, 0, 60, T0, null, null);
        zero.Should().Throw<DominioException>();
        var centavos = () => new Cobranca(Tx, Chave, 1.005m, 60, T0, null, null);
        centavos.Should().Throw<DominioException>();
        var exp = () => new Cobranca(Tx, Chave, 10, 0, T0, null, null);
        exp.Should().Throw<DominioException>();
        var txid = () => new Cobranca("curto", Chave, 10, 60, T0, null, null);
        txid.Should().Throw<DominioException>();
    }

    [Fact]
    public void ConcluiComPagamentoDoValorExato()
    {
        var c = Nova(100m);
        var errado = () => c.Concluir(Pag(99.99m), T0.AddMinutes(5));
        errado.Should().Throw<DominioException>().WithMessage("*difere*");
        var p = Pag(100m);
        c.Concluir(p, T0.AddMinutes(5));
        c.Status.Should().Be(StatusCobranca.Concluida);
        c.EndToEndIdPagamento.Should().Be(p.EndToEndId);
        var denovo = () => c.Concluir(Pag(100m), T0.AddMinutes(6));
        denovo.Should().Throw<DominioException>().WithMessage("*não está ativa*");
    }

    [Fact]
    public void ExpiraENaoAceitaPagamentoDepois()
    {
        var c = Nova(100m, 60);
        c.ExpirarSeVencida(T0.AddSeconds(59)).Should().BeFalse();
        var act = () => c.Concluir(Pag(100m, T0.AddSeconds(61)), T0.AddSeconds(61));
        act.Should().Throw<DominioException>().WithMessage("*expirada*");
        c.Status.Should().Be(StatusCobranca.Expirada);
    }

    [Fact]
    public void RevisarIncrementaRevisaoERemoverEncerra()
    {
        var c = Nova();
        c.Revisar(150m, null, "Novo pedido", 7200, T0.AddMinutes(1));
        c.Revisao.Should().Be(1);
        c.Valor.Should().Be(150m);
        c.SolicitacaoPagador.Should().Be("Novo pedido");
        c.ExpiraEm.Should().Be(T0.AddHours(2));
        c.Remover(T0.AddMinutes(2));
        c.Status.Should().Be(StatusCobranca.RemovidaPeloUsuarioRecebedor);
        c.Revisao.Should().Be(2);
        var act = () => c.Revisar(1m, null, null, null, T0.AddMinutes(3));
        act.Should().Throw<DominioException>();
    }

    [Fact]
    public void DevolucoesRespeitamSaldoEIdempotencia()
    {
        var p = Pag(100m);
        var d1 = p.SolicitarDevolucao("dev1", 30m, "D1234567820260915120000000000A1", T0.AddMinutes(1));
        d1.Status.Should().Be(StatusDevolucao.EmProcessamento);
        p.SaldoDevolvivel.Should().Be(70m);

        p.SolicitarDevolucao("dev1", 30m, "x", T0.AddMinutes(2)).Should().BeSameAs(d1); // idempotente
        var outroValor = () => p.SolicitarDevolucao("dev1", 31m, "x", T0.AddMinutes(2));
        outroValor.Should().Throw<ConflitoException>();

        var excede = () => p.SolicitarDevolucao("dev2", 70.01m, "x", T0.AddMinutes(3));
        excede.Should().Throw<DominioException>().WithMessage("*excede*");

        p.SolicitarDevolucao("dev2", 70m, "x", T0.AddMinutes(3));
        p.SaldoDevolvivel.Should().Be(0m);
        var nada = () => p.SolicitarDevolucao("dev3", 0.01m, "x", T0.AddMinutes(4));
        nada.Should().Throw<DominioException>();

        var tarde = () => Pag(50m).SolicitarDevolucao("d", 1m, "x", T0.AddDays(91));
        tarde.Should().Throw<DominioException>().WithMessage("*90 dias*");
    }
}
