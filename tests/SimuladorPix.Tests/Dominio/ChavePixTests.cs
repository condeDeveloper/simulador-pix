using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Tests.Dominio;

public class ChavePixTests
{
    [Theory]
    [InlineData("529.982.247-25", TipoChave.Cpf, "52998224725")]
    [InlineData("11.222.333/0001-81", TipoChave.Cnpj, "11222333000181")]
    [InlineData("Loja@Exemplo.com", TipoChave.Email, "loja@exemplo.com")]
    [InlineData("+55 (11) 99999-8888", TipoChave.Telefone, "+5511999998888")]
    [InlineData("123E4567-E12B-12D1-A456-426655440000", TipoChave.Aleatoria, "123e4567-e12b-12d1-a456-426655440000")]
    public void ReconheceENormalizaCadaTipo(string entrada, TipoChave tipo, string valor)
    {
        var c = ChavePix.Parse(entrada);
        c.Tipo.Should().Be(tipo);
        c.Valor.Should().Be(valor);
    }

    [Theory]
    [InlineData("529.982.247-26")]
    [InlineData("11.222.333/0001-82")]
    [InlineData("sem-arroba.com")]
    [InlineData("+1 555 123 4567")]
    [InlineData("11999998888")]
    [InlineData("")]
    public void RejeitaChavesInvalidas(string entrada)
    {
        var act = () => ChavePix.Parse(entrada);
        act.Should().Throw<DominioException>();
        ChavePix.TentarParse(entrada, out var chave).Should().BeFalse();
        chave.Should().BeNull();
    }

    [Fact]
    public void ChaveAleatoriaNovaEhUuid()
    {
        var c = ChavePix.NovaAleatoria();
        c.Tipo.Should().Be(TipoChave.Aleatoria);
        Guid.TryParse(c.Valor, out _).Should().BeTrue();
    }

    [Fact]
    public void EndToEndIdSegueOFormatoDoSpi()
    {
        var e2e = EndToEndId.Gerar("12345678", new DateTimeOffset(2026, 9, 15, 22, 30, 0, TimeSpan.Zero));
        e2e.Should().HaveLength(32).And.StartWith("E12345678202609152230");
        EndToEndId.Valido(e2e).Should().BeTrue();
        EndToEndId.GerarDevolucao("12345678", DateTimeOffset.UtcNow).Should().StartWith("D12345678");
        EndToEndId.Valido("E123").Should().BeFalse();
        var act = () => EndToEndId.Gerar("123", DateTimeOffset.UtcNow);
        act.Should().Throw<DominioException>();
    }

    [Fact]
    public void TxidGeradoEValido()
    {
        var t = Txid.Gerar();
        t.Should().HaveLength(32);
        Txid.Valido(t).Should().BeTrue();
        Txid.Valido("curto").Should().BeFalse();
        Txid.Valido("com-hifen-nao-pode-000000000000000").Should().BeFalse();
    }
}
