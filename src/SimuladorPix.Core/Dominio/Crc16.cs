using System.Text;

namespace SimuladorPix.Core.Dominio;

/// <summary>CRC-16/CCITT-FALSE (polinômio 0x1021, inicial 0xFFFF), o mesmo do campo 63 do BR Code.</summary>
public static class Crc16
{
    private static readonly ushort[] Tabela = Gerar();

    public static ushort Calcular(ReadOnlySpan<byte> dados)
    {
        ushort crc = 0xFFFF;
        foreach (var b in dados) crc = (ushort)((crc << 8) ^ Tabela[(crc >> 8) ^ b]);
        return crc;
    }

    /// <summary>CRC do texto em UTF-8, como quatro dígitos hexadecimais maiúsculos.</summary>
    public static string Hex(string texto) => Calcular(Encoding.UTF8.GetBytes(texto)).ToString("X4");

    private static ushort[] Gerar()
    {
        var t = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            t[i] = crc;
        }
        return t;
    }
}
