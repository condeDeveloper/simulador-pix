namespace SimuladorPix.Core.Aplicacao;

/// <summary>Identidade do participante simulado (PSP recebedor).</summary>
public sealed class ParticipanteOptions
{
    public const string Secao = "Participante";

    /// <summary>ISPB do PSP, 8 dígitos. Vai no endToEndId.</summary>
    public string Ispb { get; set; } = "00000000";
    public string NomeRecebedor { get; set; } = "Recebedor Simulado";
    public string Cidade { get; set; } = "SAO PAULO";
    /// <summary>Base das locations dos QR dinâmicos (ex.: pix.exemplo.com/qr/v2).</summary>
    public string BaseLocation { get; set; } = "pix.simulador.local/qr/v2";
    /// <summary>Expiração padrão da cobrança em segundos (1 hora).</summary>
    public int ExpiracaoPadraoSegundos { get; set; } = 3600;
}
