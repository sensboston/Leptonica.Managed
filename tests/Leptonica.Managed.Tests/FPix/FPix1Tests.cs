using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class FPix1Tests
{
    private static FPix MakeFPix(int w, int h)
    {
        FPix fpix = FPix.Create(w, h);
        fpix.SetResolution(300, 150);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                fpix.SetPixel(x, y, x * 1.5f - y * 0.25f);
        return fpix;
    }

    private static DPix MakeDPix(int w, int h)
    {
        DPix dpix = DPix.Create(w, h);
        dpix.SetResolution(72, 96);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dpix.SetPixel(x, y, x * 1.5 - y * 0.125 + 1e-10);
        return dpix;
    }

    /*------------------------------- FPix ------------------------------*/

    [Fact]
    public void FPixCreate_ZeroedWithWpl()
    {
        FPix fpix = FPix.Create(7, 3);
        fpix.GetDimensions(out int w, out int h);
        Assert.Equal((7, 3), (w, h));
        Assert.Equal(7, fpix.Wpl);
        Assert.Equal(21, fpix.Data.Length);
        Assert.All(fpix.Data, v => Assert.Equal(0.0f, v));
    }

    [Fact]
    public void FPixCreate_Errors()
    {
        Assert.Throws<ArgumentException>(() => FPix.Create(0, 5));
        Assert.Throws<ArgumentException>(() => FPix.Create(5, 0));
        Assert.Throws<ArgumentException>(() => FPix.Create(1 << 15, 1 << 14));
    }

    [Fact]
    public void FPixCreateTemplate_CopiesResolutionNotData()
    {
        FPix fpix = MakeFPix(4, 5);
        FPix t = fpix.CreateTemplate();
        t.GetDimensions(out int w, out int h);
        t.GetResolution(out int xres, out int yres);
        Assert.Equal((4, 5, 300, 150), (w, h, xres, yres));
        Assert.All(t.Data, v => Assert.Equal(0.0f, v));
    }

    [Fact]
    public void FPixCopyAndClone()
    {
        FPix fpix = MakeFPix(4, 5);
        FPix clone = fpix.Clone();
        Assert.Same(fpix, clone);
        FPix copy = fpix.Copy();
        Assert.NotSame(fpix, copy);
        Assert.Equal(fpix.Data, copy.Data);
        copy.GetResolution(out int xres, out int yres);
        Assert.Equal((300, 150), (xres, yres));
        copy.SetPixel(0, 0, 99f);
        fpix.GetPixel(0, 0, out float v);
        Assert.Equal(0f, v);
    }

    [Fact]
    public void FPixPixelAccess_OutsideFailsSilently()
    {
        FPix fpix = FPix.Create(3, 2);
        Assert.True(fpix.SetPixel(2, 1, 4.5f));
        Assert.True(fpix.GetPixel(2, 1, out float val));
        Assert.Equal(4.5f, val);
        Assert.Equal(4.5f, fpix.Data[1 * 3 + 2]);
        Assert.False(fpix.SetPixel(3, 0, 1f));
        Assert.False(fpix.SetPixel(0, -1, 1f));
        Assert.False(fpix.GetPixel(0, 2, out val));
        Assert.Equal(0f, val);
    }

    [Fact]
    public void FPixSerialization_ExactBytes()
    {
        FPix fpix = FPix.Create(2, 1);
        fpix.SetResolution(10, 20);
        fpix.SetPixel(0, 0, 1.0f);
        fpix.SetPixel(1, 0, -2.0f);
        byte[] data = fpix.WriteMem();

        byte[] expected = Concat(
            Encoding.ASCII.GetBytes("\nFPix Version 2\nw = 2, h = 1, nbytes = 8\nxres = 10, yres = 20\n"),
            BitConverter.GetBytes(1.0f), BitConverter.GetBytes(-2.0f),
            new byte[] { (byte)'\n' });
        Assert.Equal(expected, data);
    }

    [Fact]
    public void FPixSerialization_RoundTrip()
    {
        FPix fpix = MakeFPix(5, 4);
        FPix back = FPix.ReadMem(fpix.WriteMem());
        back.GetDimensions(out int w, out int h);
        back.GetResolution(out int xres, out int yres);
        Assert.Equal((5, 4, 300, 150), (w, h, xres, yres));
        Assert.Equal(fpix.Data, back.Data);
    }

    [Fact]
    public void FPixSerialization_DataStartingWithWhitespace()
    {
        /* float whose first byte is '\n' (0x0a) must not be swallowed by the header scan */
        FPix fpix = FPix.Create(1, 1);
        float f = BitConverter.Int32BitsToSingle(0x0a0b0c0a);
        fpix.SetPixel(0, 0, f);
        FPix back = FPix.ReadMem(fpix.WriteMem());
        Assert.Equal(BitConverter.SingleToInt32Bits(f), BitConverter.SingleToInt32Bits(back.Data[0]));
    }

    [Fact]
    public void FPixSerialization_NonSeekableStream()
    {
        FPix fpix = MakeFPix(3, 3);
        using NonSeekable ns = new NonSeekable(fpix.WriteMem());
        FPix back = FPix.ReadStream(ns);
        Assert.Equal(fpix.Data, back.Data);
    }

    [Fact]
    public void FPixSerialization_Errors()
    {
        Assert.Throws<ArgumentException>(() => FPix.ReadMem(Encoding.ASCII.GetBytes("garbage")));
        Assert.Throws<ArgumentException>(() => FPix.ReadMem(
            Encoding.ASCII.GetBytes("\nFPix Version 1\nw = 1, h = 1, nbytes = 4\nxres = 0, yres = 0\nabcd\n")));
        Assert.Throws<ArgumentException>(() => FPix.ReadMem(
            Encoding.ASCII.GetBytes("\nFPix Version 2\nw = 1, h = 1, nbytes = 4\n")));
        /* truncated data */
        Assert.Throws<ArgumentException>(() => FPix.ReadMem(
            Encoding.ASCII.GetBytes("\nFPix Version 2\nw = 1, h = 1, nbytes = 4\nxres = 0, yres = 0\nab")));
        /* nbytes larger than the data array */
        Assert.Throws<ArgumentException>(() => FPix.ReadMem(
            Encoding.ASCII.GetBytes("\nFPix Version 2\nw = 1, h = 1, nbytes = 8\nxres = 0, yres = 0\nabcdefgh\n")));
    }

    [Fact]
    public void FPixWriteRead_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fpix1_{Guid.NewGuid():N}.fp");
        try {
            FPix fpix = MakeFPix(6, 2);
            fpix.Write(path);
            FPix back = FPix.Read(path);
            Assert.Equal(fpix.Data, back.Data);
        } finally {
            File.Delete(path);
        }
        Assert.Throws<ArgumentException>(() => FPix.Read(path));
    }

    [Fact]
    public void FPixEndianByteSwap_LittleEndian()
    {
        FPix fpix = MakeFPix(2, 2);
        Assert.Same(fpix, FPix.EndianByteSwap(fpix, fpix));
        Assert.Same(fpix, FPix.EndianByteSwap(null, fpix));
        Assert.Throws<ArgumentException>(() => FPix.EndianByteSwap(MakeFPix(2, 2), fpix));
    }

    [Fact]
    public void FPixPrintStream_Format()
    {
        FPix fpix = FPix.Create(4, 2);
        for (int i = 0; i < 8; i++)
            fpix.Data[i] = i + 0.5f;
        using MemoryStream ms = new MemoryStream();
        fpix.PrintStream(ms, 2);
        string expected =
            "\nFPix: w = 4, h = 2\n" +
            "val[0, 0] = 0.500000   val[0, 2] = 2.500000   \n" +
            "\n";
        Assert.Equal(expected, Encoding.ASCII.GetString(ms.ToArray()));

        using MemoryStream ms1 = new MemoryStream();
        FPix.Create(4, 1).PrintStream(ms1, 1);
        Assert.Equal(
            "\nFPix: w = 4, h = 1\n" +
            "val[0, 0] = 0.000000   val[0, 1] = 0.000000   val[0, 2] = 0.000000   \n" +
            "val[0, 3] = 0.000000   \n" +
            "\n",
            Encoding.ASCII.GetString(ms1.ToArray()));
        Assert.Throws<ArgumentException>(() => fpix.PrintStream(ms, 0));
    }

    /*------------------------------- FPixa -----------------------------*/

    [Fact]
    public void FPixaAddAndGet()
    {
        FPixa fpixa = FPixa.Create(0);
        Assert.Equal(20, fpixa.Nalloc);
        Assert.Equal(0, fpixa.Count);

        FPix a = MakeFPix(3, 2);
        fpixa.AddFPix(a, L.L_INSERT);
        fpixa.AddFPix(a, L.L_COPY);
        fpixa.AddFPix(a, L.L_CLONE);
        Assert.Equal(3, fpixa.Count);
        Assert.Same(a, fpixa.GetFPix(0, L.L_CLONE));
        Assert.NotSame(a, fpixa.GetFPix(1, L.L_CLONE));
        Assert.Same(a, fpixa.GetFPix(2, L.L_CLONE));
        Assert.NotSame(a, fpixa.GetFPix(0, L.L_COPY));
        Assert.Equal(a.Data, fpixa.GetData(1));
        fpixa.GetFPixDimensions(1, out int w, out int h);
        Assert.Equal((3, 2), (w, h));

        Assert.Throws<ArgumentException>(() => fpixa.GetFPix(3, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => fpixa.GetFPix(-1, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => fpixa.GetFPix(0, 99));
        Assert.Throws<ArgumentException>(() => fpixa.AddFPix(a, 99));
        Assert.Throws<ArgumentException>(() => fpixa.GetData(3));
        Assert.Throws<ArgumentException>(() => fpixa.GetFPixDimensions(3, out _, out _));
    }

    [Fact]
    public void FPixaExtendsArray()
    {
        FPixa fpixa = FPixa.Create(2);
        for (int i = 0; i < 5; i++)
            fpixa.AddFPix(FPix.Create(1, 1), L.L_INSERT);
        Assert.Equal(5, fpixa.Count);
        Assert.Equal(8, fpixa.Nalloc);
    }

    [Fact]
    public void FPixaCopy()
    {
        FPixa fpixa = FPixa.Create(4);
        fpixa.AddFPix(MakeFPix(2, 2), L.L_INSERT);
        fpixa.AddFPix(MakeFPix(3, 1), L.L_INSERT);

        Assert.Same(fpixa, fpixa.Copy(L.L_CLONE));

        FPixa copy = fpixa.Copy(L.L_COPY);
        Assert.NotSame(fpixa, copy);
        Assert.Equal(2, copy.Count);
        Assert.NotSame(fpixa.GetFPix(0, L.L_CLONE), copy.GetFPix(0, L.L_CLONE));
        Assert.Equal(fpixa.GetData(1), copy.GetData(1));

        FPixa cc = fpixa.Copy(L.L_COPY_CLONE);
        Assert.Same(fpixa.GetFPix(1, L.L_CLONE), cc.GetFPix(1, L.L_CLONE));

        Assert.Throws<ArgumentException>(() => fpixa.Copy(L.L_INSERT));
    }

    [Fact]
    public void FPixaPixelAccess()
    {
        FPixa fpixa = FPixa.Create(0);
        fpixa.AddFPix(FPix.Create(3, 3), L.L_INSERT);
        Assert.True(fpixa.SetPixel(0, 1, 2, 7.25f));
        Assert.True(fpixa.GetPixel(0, 1, 2, out float val));
        Assert.Equal(7.25f, val);
        Assert.False(fpixa.SetPixel(0, 3, 0, 1f));
        Assert.False(fpixa.GetPixel(0, 0, 3, out val));
        Assert.Equal(0f, val);
        Assert.Throws<ArgumentException>(() => fpixa.SetPixel(1, 0, 0, 1f));
        Assert.Throws<ArgumentException>(() => fpixa.GetPixel(-1, 0, 0, out _));
    }

    /*------------------------------- DPix ------------------------------*/

    [Fact]
    public void DPixCreate_ZeroedWithWpl()
    {
        DPix dpix = DPix.Create(7, 3);
        dpix.GetDimensions(out int w, out int h);
        Assert.Equal((7, 3), (w, h));
        Assert.Equal(7, dpix.Wpl);
        Assert.Equal(21, dpix.Data.Length);
        Assert.All(dpix.Data, v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void DPixCreate_Errors()
    {
        Assert.Throws<ArgumentException>(() => DPix.Create(0, 5));
        Assert.Throws<ArgumentException>(() => DPix.Create(5, -1));
        Assert.Throws<ArgumentException>(() => DPix.Create(1 << 14, 1 << 14));
    }

    [Fact]
    public void DPixCopyCloneTemplate()
    {
        DPix dpix = MakeDPix(4, 3);
        Assert.Same(dpix, dpix.Clone());
        DPix copy = dpix.Copy();
        Assert.NotSame(dpix, copy);
        Assert.Equal(dpix.Data, copy.Data);
        copy.GetResolution(out int xres, out int yres);
        Assert.Equal((72, 96), (xres, yres));
        DPix t = dpix.CreateTemplate();
        t.GetResolution(out xres, out yres);
        Assert.Equal((72, 96), (xres, yres));
        Assert.All(t.Data, v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void DPixPixelAccess_OutsideFailsSilently()
    {
        DPix dpix = DPix.Create(3, 2);
        Assert.True(dpix.SetPixel(1, 1, 2.5));
        Assert.True(dpix.GetPixel(1, 1, out double val));
        Assert.Equal(2.5, val);
        Assert.False(dpix.SetPixel(-1, 0, 1.0));
        Assert.False(dpix.GetPixel(3, 1, out val));
        Assert.Equal(0.0, val);
    }

    [Fact]
    public void DPixSerialization_ExactBytes()
    {
        DPix dpix = DPix.Create(1, 2);
        dpix.SetResolution(1, 2);
        dpix.SetPixel(0, 0, 0.5);
        dpix.SetPixel(0, 1, -3.0);
        byte[] expected = Concat(
            Encoding.ASCII.GetBytes("\nDPix Version 2\nw = 1, h = 2, nbytes = 16\nxres = 1, yres = 2\n"),
            BitConverter.GetBytes(0.5), BitConverter.GetBytes(-3.0),
            new byte[] { (byte)'\n' });
        Assert.Equal(expected, dpix.WriteMem());
    }

    [Fact]
    public void DPixSerialization_RoundTrip()
    {
        DPix dpix = MakeDPix(5, 4);
        DPix back = DPix.ReadMem(dpix.WriteMem());
        back.GetResolution(out int xres, out int yres);
        Assert.Equal((72, 96), (xres, yres));
        Assert.Equal(dpix.Data, back.Data);
    }

    [Fact]
    public void DPixSerialization_Errors()
    {
        Assert.Throws<ArgumentException>(() => DPix.ReadMem(Encoding.ASCII.GetBytes("\nFPix Version 2\n")));
        Assert.Throws<ArgumentException>(() => DPix.ReadMem(
            Encoding.ASCII.GetBytes("\nDPix Version 3\nw = 1, h = 1, nbytes = 8\nxres = 0, yres = 0\n")));
        Assert.Throws<ArgumentException>(() => DPix.ReadMem(
            Encoding.ASCII.GetBytes("\nDPix Version 2\nw = 1, h = 1, nbytes = 8\nxres = 0, yres = 0\nabc")));
    }

    [Fact]
    public void DPixWriteRead_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dpix1_{Guid.NewGuid():N}.dp");
        try {
            DPix dpix = MakeDPix(3, 3);
            dpix.Write(path);
            DPix back = DPix.Read(path);
            Assert.Equal(dpix.Data, back.Data);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void DPixEndianByteSwap_LittleEndian()
    {
        DPix dpix = MakeDPix(2, 2);
        Assert.Same(dpix, DPix.EndianByteSwap(dpix, dpix));
        Assert.Same(dpix, DPix.EndianByteSwap(null, dpix));
        Assert.Throws<ArgumentException>(() => DPix.EndianByteSwap(MakeDPix(2, 2), dpix));
    }

    /*------------------------------ helpers ----------------------------*/

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream ms = new MemoryStream();
        foreach (byte[] p in parts)
            ms.Write(p, 0, p.Length);
        return ms.ToArray();
    }

    private sealed class NonSeekable : MemoryStream
    {
        public NonSeekable(byte[] data) : base(data) { }
        public override bool CanSeek => false;
    }
}
