using SimuladorPix.Core.Dominio;

namespace SimuladorPix.Tests.Dominio;

public class WebhookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AssinaturaHmacConfereEResisteAAlteracao()
    {
        var assinatura = Webhook.Assinar("segredo", "{\"a\":1}");
        assinatura.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        Webhook.AssinaturaValida("segredo", "{\"a\":1}", assinatura).Should().BeTrue();
        Webhook.AssinaturaValida("segredo", "{\"a\":1}", assinatura.ToUpperInvariant()).Should().BeTrue();
        Webhook.AssinaturaValida("segredo", "{\"a\":2}", assinatura).Should().BeFalse();
        Webhook.AssinaturaValida("outro", "{\"a\":1}", assinatura).Should().BeFalse();
        Webhook.AssinaturaValida("segredo", "{\"a\":1}", null).Should().BeFalse();
    }

    [Fact]
    public void ExigeHttpsForaDeLocalhostEGeraSegredo()
    {
        var chave = ChavePix.Parse("loja@exemplo.com");
        var ok = new Webhook(chave, "https://exemplo.com/pix", null, T0);
        ok.Segredo.Should().HaveLength(64);
        var local = new Webhook(chave, "http://localhost:5000/receptor", "meu-segredo", T0);
        local.Segredo.Should().Be("meu-segredo");
        var http = () => new Webhook(chave, "http://exemplo.com/pix", null, T0);
        http.Should().Throw<DominioException>().WithMessage("*https*");
        var lixo = () => new Webhook(chave, "nao é url", null, T0);
        lixo.Should().Throw<DominioException>();
    }

    [Fact]
    public void BackoffExponencialComTeto()
    {
        EntregaWebhook.Atraso(1).Should().Be(TimeSpan.FromSeconds(5));
        EntregaWebhook.Atraso(2).Should().Be(TimeSpan.FromSeconds(10));
        EntregaWebhook.Atraso(3).Should().Be(TimeSpan.FromSeconds(20));
        EntregaWebhook.Atraso(6).Should().Be(TimeSpan.FromSeconds(160));
        EntregaWebhook.Atraso(20).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void EntregaReagendaAteOLimiteEDepoisFalha()
    {
        var wh = new Webhook(ChavePix.Parse("loja@exemplo.com"), "https://exemplo.com/pix", "s", T0);
        var e = new EntregaWebhook(wh, "pix", "{}", T0);
        e.Assinatura.Should().Be(Webhook.Assinar("s", "{}"));
        e.Pronta(T0).Should().BeTrue();

        var agora = T0;
        for (var i = 1; i < EntregaWebhook.MaximoTentativas; i++)
        {
            e.RegistrarFalha("HTTP 500", agora);
            e.Status.Should().Be(StatusEntrega.Pendente);
            e.Tentativas.Should().Be(i);
            e.ProximaTentativa.Should().Be(agora + EntregaWebhook.Atraso(i));
            e.Pronta(agora).Should().BeFalse();
            agora = e.ProximaTentativa;
            e.Pronta(agora).Should().BeTrue();
        }
        e.RegistrarFalha("HTTP 500", agora);
        e.Status.Should().Be(StatusEntrega.Falhou);
        e.Tentativas.Should().Be(EntregaWebhook.MaximoTentativas);
    }

    [Fact]
    public void SucessoEncerraAEntrega()
    {
        var wh = new Webhook(ChavePix.Parse("loja@exemplo.com"), "https://exemplo.com/pix", "s", T0);
        var e = new EntregaWebhook(wh, "pix", "{}", T0);
        e.RegistrarFalha("timeout", T0);
        e.RegistrarSucesso(T0.AddSeconds(5));
        e.Status.Should().Be(StatusEntrega.Entregue);
        e.Tentativas.Should().Be(2);
        e.EntregueEm.Should().Be(T0.AddSeconds(5));
        e.UltimoErro.Should().BeNull();
    }
}
