using System;
using System.Buffers.Binary;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

public class Jp2kHeaderTests
{
    [RegFact]
    public void ReadHeaderJp2kReadsGoldenJp2Files()
    {
        string path = Path.Combine(TestPaths.GoldenDir!, "jp2kio_golden.00.jp2");
        Pix.ReadHeaderJp2k(path, out int w, out int h, out int bps, out int spp, out int codec);
        Assert.True(w > 0 && h > 0);
        Assert.Equal(8, bps);
        Assert.True(spp == 1 || spp == 3 || spp == 4);
        Assert.Equal(L.L_JP2_CODEC, codec);

        Pix.ReadHeader(path, out int fmt, out int w2, out int h2, out int bps2, out int spp2, out bool iscmap);
        Assert.Equal(L.IFF_JP2, fmt);
        Assert.Equal((w, h, bps, spp, false), (w2, h2, bps2, spp2, iscmap));
    }

    [Fact]
    public void ReadHeaderMemJp2kAndResolutionParseJp2AndJ2kHeaders()
    {
        byte[] jp2 = new byte[128];
        byte[] sig = { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A };
        Buffer.BlockCopy(sig, 0, jp2, 0, sig.Length);
        jp2[44] = (byte)'i';
        jp2[45] = (byte)'h';
        jp2[46] = (byte)'d';
        jp2[47] = (byte)'r';
        BinaryPrimitives.WriteInt32BigEndian(jp2.AsSpan(48, 4), 240);
        BinaryPrimitives.WriteInt32BigEndian(jp2.AsSpan(52, 4), 320);
        BinaryPrimitives.WriteUInt16BigEndian(jp2.AsSpan(56, 2), 3);
        jp2[58] = 7; /* bps - 1 */

        jp2[70] = (byte)'r';
        jp2[71] = (byte)'e';
        jp2[72] = (byte)'s';
        jp2[73] = (byte)'c';
        BinaryPrimitives.WriteUInt16BigEndian(jp2.AsSpan(74, 2), 11811);
        BinaryPrimitives.WriteUInt16BigEndian(jp2.AsSpan(76, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(jp2.AsSpan(78, 2), 11811);
        BinaryPrimitives.WriteUInt16BigEndian(jp2.AsSpan(80, 2), 1);
        jp2[82] = 0;
        jp2[83] = 0;

        Pix.ReadHeaderMemJp2k(jp2, out int w, out int h, out int bps, out int spp, out int codec);
        Assert.Equal((320, 240, 8, 3, L.L_JP2_CODEC), (w, h, bps, spp, codec));

        using var ms = new MemoryStream(jp2);
        Assert.True(Pix.FgetJp2kResolution(ms, out int xres, out int yres));
        Assert.Equal((300, 300), (xres, yres));

            /* Codestream J2K */
        byte[] j2k = new byte[128];
        j2k[0] = 0xff; j2k[1] = 0x4f; j2k[2] = 0xff; j2k[3] = 0x51;
        BinaryPrimitives.WriteInt32BigEndian(j2k.AsSpan(8, 4), 640);
        BinaryPrimitives.WriteInt32BigEndian(j2k.AsSpan(12, 4), 480);
        BinaryPrimitives.WriteUInt16BigEndian(j2k.AsSpan(40, 2), 1);
        j2k[42] = 15; /* 16 bps */

        Pix.ReadHeaderMemJp2k(j2k, out w, out h, out bps, out spp, out codec);
        Assert.Equal((640, 480, 16, 1, L.L_J2K_CODEC), (w, h, bps, spp, codec));
    }
}
