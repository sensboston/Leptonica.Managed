using System;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.UtilsTests;

public class EncodingTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] Random(int seed, int n)
    {
        var rng = new Random(seed);
        byte[] b = new byte[n];
        rng.NextBytes(b);
        return b;
    }

    [Theory]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg==")]
    [InlineData("fooba", "Zm9vYmE=")]
    [InlineData("foobar", "Zm9vYmFy")]
    public void Base64_KnownVectors(string plain, string encoded)
    {
        Assert.Equal(encoded, LeptEncoding.EncodeBase64(Ascii(plain)));
        Assert.Equal(Ascii(plain), LeptEncoding.DecodeBase64(encoded));
    }

    [Fact]
    public void Base64_LineLength()
    {
        string s54 = LeptEncoding.EncodeBase64(Random(1, 54));   /* 72 chars: one full line */
        Assert.Equal(72, s54.Length);
        string s57 = LeptEncoding.EncodeBase64(Random(1, 57));
        Assert.Equal(72 + 1 + 4, s57.Length);
        Assert.Equal('\n', s57[72]);
        string s200 = LeptEncoding.EncodeBase64(Random(1, 200));
        foreach (string line in s200.Split('\n'))
            Assert.True(line.Length <= 72);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(53)]
    [InlineData(54)]
    [InlineData(55)]
    [InlineData(1000)]
    public void Base64_RoundTrip(int n)
    {
        byte[] data = Random(n, n);
        Assert.Equal(data, LeptEncoding.DecodeBase64(LeptEncoding.EncodeBase64(data)));
    }

    [Fact]
    public void Base64_DecodeIgnoresNewlinesAndStopsAtPad()
    {
        Assert.Equal(Ascii("foobar"), LeptEncoding.DecodeBase64("Zm9v\nYmFy\n"));
        Assert.Equal(Ascii("fo"), LeptEncoding.DecodeBase64("Zm8=Zm9v"));
    }

    [Fact]
    public void Base64_InvalidInput()
    {
        Assert.Throws<ArgumentException>(() => LeptEncoding.EncodeBase64(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => LeptEncoding.DecodeBase64(""));
        Assert.Throws<ArgumentException>(() => LeptEncoding.DecodeBase64("Zm9v!"));
        Assert.Throws<ArgumentException>(() => LeptEncoding.DecodeBase64("Zm 9v"));
    }

    [Fact]
    public void Ascii85_KnownVectors()
    {
        Assert.Equal("9jqo^\n~>\n", LeptEncoding.EncodeAscii85(Ascii("Man ")));
        Assert.Equal("9jqo^BlbD-BleB1DJ+*+F(f,q\n~>\n",
                     LeptEncoding.EncodeAscii85(Ascii("Man is distinguished")));
        Assert.Equal("z\n~>\n", LeptEncoding.EncodeAscii85(new byte[4]));
        Assert.Equal(Ascii("Man "), LeptEncoding.DecodeAscii85("9jqo^~>"));
        Assert.Equal(Ascii("Man is distinguished"),
                     LeptEncoding.DecodeAscii85("9jqo^BlbD-BleB1DJ+*+F(f,q~>"));
    }

    [Fact]
    public void Ascii85_PartialChunks()
    {
        /* 1, 2 and 3 trailing bytes produce 2, 3 and 4 characters */
        Assert.Equal("9`\n~>\n", LeptEncoding.EncodeAscii85(Ascii("M")));
        Assert.Equal(Ascii("M"), LeptEncoding.DecodeAscii85("9`~>"));
        Assert.Equal(3, LeptEncoding.EncodeAscii85(Ascii("Ma")).IndexOf('\n'));
        Assert.Equal(4, LeptEncoding.EncodeAscii85(Ascii("Man")).IndexOf('\n'));
        Assert.Equal(Ascii("Ma"), LeptEncoding.DecodeAscii85(LeptEncoding.EncodeAscii85(Ascii("Ma"))));
        Assert.Equal(Ascii("Man"), LeptEncoding.DecodeAscii85(LeptEncoding.EncodeAscii85(Ascii("Man"))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(500)]
    public void Ascii85_RoundTrip(int n)
    {
        byte[] data = Random(n + 100, n);
        data[^1] |= 1;     /* avoid an all-zero final chunk (see Ascii85_ZeroChunkQuirk) */
        string enc = LeptEncoding.EncodeAscii85(data);
        Assert.EndsWith("~>\n", enc);
        foreach (string line in enc.Split('\n'))
            Assert.True(line.Length <= 64);
        Assert.Equal(data, LeptEncoding.DecodeAscii85(enc));
    }

    [Fact]
    public void Ascii85_ZerosAndWhitespace()
    {
        byte[] data = new byte[12];
        data[8] = 1;
        string enc = LeptEncoding.EncodeAscii85(data);
        Assert.StartsWith("zz", enc);
        Assert.Equal(data, LeptEncoding.DecodeAscii85(enc));
        Assert.Equal(Ascii("Man "), LeptEncoding.DecodeAscii85(" 9j\tqo\r\n^ ~>"));
    }

    [Fact]
    public void Ascii85_ZeroChunkQuirk()
    {
        /* A final partial chunk of zero bytes is written as 'z', which decodes to 4 bytes */
        string enc = LeptEncoding.EncodeAscii85(new byte[] { 0 });
        Assert.Equal("z\n~>\n", enc);
        Assert.Equal(4, LeptEncoding.DecodeAscii85(enc).Length);
    }

    [Fact]
    public void Ascii85_InvalidInput()
    {
        Assert.Throws<ArgumentException>(() => LeptEncoding.EncodeAscii85(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => LeptEncoding.DecodeAscii85(""));
    }

    [Fact]
    public void Ascii85WithComp_RoundTrip()
    {
        byte[] data = Ascii(new string('a', 500) + "bcd" + new string('e', 300));
        string enc = LeptEncoding.EncodeAscii85WithComp(data);
        Assert.True(enc.Length < 200);              /* compressed before encoding */
        Assert.Equal(data, LeptEncoding.DecodeAscii85WithComp(enc));
        Assert.Equal(data, LeptEncoding.DecodeAscii85WithComp(enc, enc.Length));
        byte[] rnd = Random(9, 300);
        Assert.Equal(rnd, LeptEncoding.DecodeAscii85WithComp(LeptEncoding.EncodeAscii85WithComp(rnd)));
    }

    [Fact]
    public void PixTextComp_RoundTrip()
    {
        Pix pix = Pix.Create(4, 4, 1);
        byte[] data = Random(3, 120);
        pix.SetTextCompNew(data);
        Assert.NotNull(pix.Text);
        Assert.Equal(data, pix.GetTextCompNew());
    }

    [Fact]
    public void ReformatPacked64_Lines()
    {
        string s = LeptEncoding.ReformatPacked64("AAAABBBBCCCCDD==", 2, 8, true);
        Assert.Equal("  \"AAAABBBB\"\n  \"CCCCDD==\"", s);
        Assert.Equal("AAAABBBB\nCCCCDD==", LeptEncoding.ReformatPacked64("AAAA\nBBBB CCCC\nDD==", 0, 8, false));
        Assert.Equal("   AAAA", LeptEncoding.ReformatPacked64("AAAA", 3, 4, false));
    }

    [Fact]
    public void ReformatPacked64_InvalidInput()
    {
        Assert.Throws<ArgumentException>(() => LeptEncoding.ReformatPacked64("", 0, 8, false));
        Assert.Throws<ArgumentException>(() => LeptEncoding.ReformatPacked64("AAAA", -1, 8, false));
        Assert.Throws<ArgumentException>(() => LeptEncoding.ReformatPacked64("AAAA", 0, 6, false));
    }

    [Fact]
    public void Zlib_RoundTrip()
    {
        byte[] data = Random(2, 1000);
        Assert.Equal(data, ZlibMem.ZlibUncompress(ZlibMem.ZlibCompress(data)));
        Assert.Throws<LeptonicaException>(() => ZlibMem.ZlibUncompress(new byte[] { 1, 2, 3, 4, 5 }));
    }
}
