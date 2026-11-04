using System;
using System.IO;
using System.Text;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

/// <summary>Tests for pnmio.c across ASCII P1..P3, packed P4..P6, and PAM P7.</summary>
public class PnmIoTests
{
    private static Pix MakePattern(int w, int h, int d, bool withAlpha = false)
    {
        Pix pix = Pix.Create(w, h, d);
        if (withAlpha && d == 32)
            pix.Spp = 4;
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                uint v;
                if (d == 32) {
                    int r = (x * 17 + y * 31) & 0xff;
                    int g = (x * 7 + y * 53) & 0xff;
                    int b = (x * 41 + y * 11) & 0xff;
                    if (withAlpha)
                        Pix.ComposeRGBAPixel(r, g, b, (x + y * 3) & 0xff, out v);
                    else
                        Pix.ComposeRGBPixel(r, g, b, out v);
                } else {
                    int mod = 1 << d;
                    v = (uint)((x * 13 + y * 7 + 1) % mod);
                }
                pix.SetPixel(x, y, v);
            }
        }
        return pix;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RoundTripRawAsciiAndPamAllDepths(int d)
    {
        Pix src = MakePattern(19, 11, d);

        /* Raw P4/P5/P6 */
        byte[] raw = Pix.WriteMemPnm(src);
        Pix fromRaw = Pix.ReadMemPnm(raw);
        Assert.True(Pix.Equal(src, fromRaw));
        Assert.Equal(L.IFF_PNM, fromRaw.InputFormat);

        /* ASCII P1/P2/P3 */
        using var msAscii = new MemoryStream();
        Pix.WriteStreamAsciiPnm(msAscii, src);
        msAscii.Position = 0;
        Pix fromAscii = Pix.ReadStreamPnm(msAscii);
        Assert.True(Pix.Equal(src, fromAscii));

        /* PAM P7 (note: upstream case 7 with bps == 16 masks each sample with 0xff) */
        byte[] pam = Pix.WriteMemPam(src);
        Pix fromPam = Pix.ReadMemPnm(pam);
        if (d != 16) {
            Assert.True(Pix.Equal(src, fromPam));
        } else {
            Assert.Equal((src.Width, src.Height, 16), (fromPam.Width, fromPam.Height, fromPam.Depth));
            src.GetPixel(3, 2, out uint sv);
            fromPam.GetPixel(3, 2, out uint pv);
            Assert.Equal(sv & 0xffu, pv);
        }
    }

    [Fact]
    public void RoundTripRgbaAndGrayAlphaPam()
    {
        Pix rgba = MakePattern(13, 9, 32, withAlpha: true);

        /* WriteMemPnm delegates 32 bpp spp == 4 to PAM */
        byte[] pnmBytes = Pix.WriteMemPnm(rgba);
        Pix decoded = Pix.ReadMemPnm(pnmBytes);
        Assert.Equal(4, decoded.Spp);
        Assert.True(Pix.EqualWithAlpha(rgba, decoded, true));

        /* Synthetic DEPTH 2 (grayscale + alpha) PAM */
        byte[] hdr = Encoding.ASCII.GetBytes(
            "P7\n# comment\nWIDTH 2\nHEIGHT 1\nDEPTH 2\nMAXVAL 255\nTUPLTYPE GRAYSCALE_ALPHA\nENDHDR\n");
        byte[] pam2 = new byte[hdr.Length + 4];
        Buffer.BlockCopy(hdr, 0, pam2, 0, hdr.Length);
        pam2[hdr.Length + 0] = 100;
        pam2[hdr.Length + 1] = 200;
        pam2[hdr.Length + 2] = 50;
        pam2[hdr.Length + 3] = 128;

        Pix ga = Pix.ReadMemPnm(pam2);
        Assert.Equal((2, 1, 32, 4), (ga.Width, ga.Height, ga.Depth, ga.Spp));
        Pix.ComposeRGBAPixel(100, 100, 100, 200, out uint p0);
        Pix.ComposeRGBAPixel(50, 50, 50, 128, out uint p1);
        ga.GetPixel(0, 0, out uint a0);
        ga.GetPixel(1, 0, out uint a1);
        Assert.Equal(p0, a0);
        Assert.Equal(p1, a1);
    }

    [Fact]
    public void HeaderParsingAndComments()
    {
        Pix src = MakePattern(15, 7, 8);
        byte[] raw = Pix.WriteMemPnm(src);
        Pix.ReadHeaderMemPnm(raw, out int w, out int h, out int d, out int type, out int bps, out int spp);
        Assert.Equal((15, 7, 8, 5, 8, 1), (w, h, d, type, bps, spp));

        string tmp = Path.Combine(Path.GetTempPath(), "lept_pnm_" + Guid.NewGuid().ToString("N") + ".pnm");
        try {
            File.WriteAllBytes(tmp, raw);
            Pix.ReadHeaderPnm(tmp, out w, out h, out d, out type, out bps, out spp);
            Assert.Equal((15, 7, 8, 5, 8, 1), (w, h, d, type, bps, spp));
        } finally {
            if (File.Exists(tmp)) File.Delete(tmp);
        }

        Assert.Throws<LeptonicaException>(() =>
            Pix.ReadMemPnm(Encoding.ASCII.GetBytes("P9\n1 1\n")));
    }

    [Fact]
    public void AllChecksFromPnmIoRegPassDirectly()
    {
        if (TestPaths.InputDir == null || !Directory.Exists(TestPaths.ProgDir))
            return;

        static Pix RoundTripAsciiThenRaw(Pix p)
        {
            using var ms1 = new MemoryStream();
            Pix.WriteStreamAsciiPnm(ms1, p);
            ms1.Position = 0;
            Pix p2 = Pix.ReadStreamPnm(ms1);
            byte[] raw = Pix.WriteMemPnm(p2);
            return Pix.ReadMemPnm(raw);
        }

        Pix pix1 = TestPaths.ReadInput("char.tif");
        Assert.True(Pix.Equal(pix1, RoundTripAsciiThenRaw(pix1)));
        Assert.True(Pix.Equal(pix1, Pix.ReadMemPnm(Pix.WriteMemPam(pix1))));

        pix1 = TestPaths.ReadInput("weasel8.png");
        Pix pix2 = pix1.ThresholdTo2bpp(4, false);
        Assert.True(Pix.Equal(pix2, RoundTripAsciiThenRaw(pix2)));
        Assert.True(Pix.Equal(pix2, Pix.ReadMemPnm(Pix.WriteMemPam(pix2))));

        Pix pix4 = pix1.ThresholdTo4bpp(16, false);
        Assert.True(Pix.Equal(pix4, RoundTripAsciiThenRaw(pix4)));
        Assert.True(Pix.Equal(pix4, Pix.ReadMemPnm(Pix.WriteMemPam(pix4))));

        Assert.True(Pix.Equal(pix1, RoundTripAsciiThenRaw(pix1)));
        Assert.True(Pix.Equal(pix1, Pix.ReadMemPnm(Pix.WriteMemPam(pix1))));

        pix1 = TestPaths.ReadInput("marge.jpg");
        Assert.True(Pix.Equal(pix1, RoundTripAsciiThenRaw(pix1)));
        Assert.True(Pix.Equal(pix1, Pix.ReadMemPnm(Pix.WriteMemPnm(pix1))));
        Assert.True(Pix.Equal(pix1, Pix.ReadMemPnm(Pix.WriteMemPam(pix1))));

        pix1 = TestPaths.ReadInput("test32-alpha.png");
        Pix pam32 = Pix.ReadMemPnm(Pix.WriteMemPam(pix1));
        Assert.True(Pix.EqualWithAlpha(pix1, Pix.ReadMemPnm(Pix.WriteMemPnm(pam32)), true));
    }
}
