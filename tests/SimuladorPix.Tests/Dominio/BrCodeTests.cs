using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Tests.Dominio;

public class BrCodeTests
{
    [Fact]
    public void Crc16CcittFalseVetorConhecido()
    {
        // vetor de verificação padrão do CRC-16/CCITT-FALSE
        Crc16.Hex("123456789").Should().Be("29B1");
        Crc16.Hex("").Should().Be("FFFF");
    }

    [Fact]
    public void ExemploDoManualDoBcbTemCrcValido()
    {
        // exemplo de QR estático do Manual de Padrões para Iniciação do Pix
        const string payload = "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D";
        var campos = BrCode.Ler(payload);
        campos["00"].Should().Be("01");
        campos["53"].Should().Be("986");
        campos["58"].Should().Be("BR");
        campos["59"].Should().Be("Fulano de Tal");
        BrCode.LerTlv(campos["26"])["00"].Should().Be(BrCode.GuiPix);
        BrCode.LerTlv(campos["26"])["01"].Should().Be("123e4567-e12b-12d1-a456-426655440000");
    }

    [Fact]
    public void GeraQrEstaticoIgualAoDoManual()
    {
        var chave = ChavePix.Parse("123e4567-e12b-12d1-a456-426655440000");
        var payload = BrCode.Gerar(new BrCode.Dados(chave, "Fulano de Tal", "BRASILIA"));
        payload.Should().Be("00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D");
    }

    [Fact]
    public void GeraQrDinamicoComValorTxidELocation()
    {
        var chave = ChavePix.Parse("loja@exemplo.com");
        var txid = "AbCdEfGhIjKlMnOpQrStUvWxYz0123";
        var payload = BrCode.Gerar(new BrCode.Dados(chave, "Loja Simulada LTDA", "São Paulo", 123.45m, txid, Estatico: false, Url: "pix.simulador.local/qr/v2/abc"));
        var campos = BrCode.Ler(payload);
        campos["01"].Should().Be("12");
        campos["54"].Should().Be("123.45");
        campos["60"].Should().Be("Sao Paulo"); // sem acento
        BrCode.LerTlv(campos["62"])["05"].Should().Be(txid);
        BrCode.LerTlv(campos["26"])["25"].Should().Be("pix.simulador.local/qr/v2/abc");
        BrCode.LerTlv(campos["26"]).Should().NotContainKey("01"); // dinâmico usa location, não chave
    }

    [Fact]
    public void DetectaCrcErrado()
    {
        var payload = BrCode.Gerar(new BrCode.Dados(ChavePix.Parse("11122233396"), "Fulano", "RIO"));
        var corrompido = payload[..^1] + (payload[^1] == 'A' ? 'B' : 'A');
        var act = () => BrCode.Ler(corrompido);
        act.Should().Throw<DominioException>().WithMessage("*CRC*");
    }

    [Fact]
    public void TruncaNomeECidadeNosLimites()
    {
        var payload = BrCode.Gerar(new BrCode.Dados(ChavePix.Parse("+5511999998888"), "Nome Muito Comprido Para Caber No Campo", "Cidade Comprida Demais"));
        var campos = BrCode.Ler(payload);
        campos["59"].Length.Should().BeLessThanOrEqualTo(25);
        campos["60"].Length.Should().BeLessThanOrEqualTo(15);
    }
}
