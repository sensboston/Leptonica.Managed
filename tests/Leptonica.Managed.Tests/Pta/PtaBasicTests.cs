using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.PtaTests;

[Collection("LeptDebugOK")]
public class PtaBasicTests
{
    private static string Text(byte[] data) => Encoding.ASCII.GetString(data);

    private static Pta MakePta(int count)
    {
        Pta pta = Pta.Create(0);
        for (int i = 0; i < count; i++)
            pta.AddPt(i + 0.5f, 10 * i + 3.25f);
        return pta;
    }

    /*------------------------------- Pta -------------------------------*/

    [Fact]
    public void Create_Sizes()
    {
        Assert.Equal(50, Pta.Create(0).Nalloc);
        Assert.Equal(50, Pta.Create(-3).Nalloc);
        Assert.Equal(50, Pta.Create(100000001).Nalloc);
        Assert.Equal(7, Pta.Create(7).Nalloc);
        Assert.Equal(0, Pta.Create(7).Count);
    }

    [Fact]
    public void AddPt_ExtendsByDoubling()
    {
        Pta pta = Pta.Create(2);
        pta.AddPt(1, 2);
        pta.AddPt(3, 4);
        Assert.Equal(2, pta.Nalloc);
        pta.AddPt(5, 6);
        Assert.Equal(4, pta.Nalloc);
        for (int i = 0; i < 6; i++)
            pta.AddPt(i, i);
        Assert.Equal(9, pta.Count);
        Assert.Equal(16, pta.Nalloc);
        pta.GetPt(2, out float x, out float y);
        Assert.Equal((5f, 6f), (x, y));
    }

    [Fact]
    public void GetPt_GetIPt_SetPt()
    {
        Pta pta = Pta.Create(0);
        pta.AddPt(1.4f, 2.5f);
        pta.AddPt(-0.4f, 3.49f);

        pta.GetPt(0, out float x, out float y);
        Assert.Equal((1.4f, 2.5f), (x, y));
        pta.GetIPt(0, out int ix, out int iy);
        Assert.Equal((1, 3), (ix, iy));
        pta.GetIPt(1, out ix, out iy);
        Assert.Equal((0, 3), (ix, iy));

        pta.SetPt(1, 7.5f, 8f);
        pta.GetPt(1, out x, out y);
        Assert.Equal((7.5f, 8f), (x, y));
    }

    [Fact]
    public void Accessors_InvalidIndex()
    {
        Pta pta = MakePta(3);
        Assert.Throws<ArgumentException>(() => pta.GetPt(3, out _, out _));
        Assert.Throws<ArgumentException>(() => pta.GetPt(-1, out _, out _));
        Assert.Throws<ArgumentException>(() => pta.GetIPt(3, out _, out _));
        Assert.Throws<ArgumentException>(() => pta.SetPt(3, 0, 0));
        Assert.Throws<ArgumentException>(() => pta.SetPt(-1, 0, 0));
    }

    [Fact]
    public void CopyAndClone()
    {
        Pta pta = MakePta(5);
        Pta copy = pta.Copy();
        Pta clone = pta.Clone();
        Assert.NotSame(pta, copy);
        Assert.Same(pta, clone);
        Assert.Equal(pta.Nalloc, copy.Nalloc);
        Assert.Equal(5, copy.Count);

        pta.SetPt(0, 99f, 98f);
        clone.GetPt(0, out float x, out float y);
        Assert.Equal((99f, 98f), (x, y));
        copy.GetPt(0, out x, out y);
        Assert.Equal((0.5f, 3.25f), (x, y));
    }

    [Fact]
    public void CopyRange_RoundsToIntegers()
    {
        Pta pta = MakePta(6);   /* x = i + 0.5, y = 10 i + 3.25 */
        Pta r = pta.CopyRange(1, 3);
        Assert.Equal(3, r.Count);
        r.GetPt(0, out float x, out float y);
        Assert.Equal((2f, 13f), (x, y));       /* (int)(1.5 + 0.5), (int)(13.25 + 0.5) */
        r.GetPt(2, out x, out y);
        Assert.Equal((4f, 33f), (x, y));
    }

    [Fact]
    public void CopyRange_Bounds()
    {
        Pta pta = MakePta(4);
        Assert.Equal(4, pta.CopyRange(-5, 0).Count);     /* istart clamped to 0, iend 0 means to end */
        Assert.Equal(2, pta.CopyRange(2, 100).Count);    /* iend clamped to n - 1 */
        Assert.Equal(1, pta.CopyRange(3, 0).Count);
        Assert.Throws<ArgumentException>(() => pta.CopyRange(4, 0));
        Assert.Throws<ArgumentException>(() => pta.CopyRange(3, 1) /* iend 1 < istart 3 */);
    }

    [Fact]
    public void Empty_ResetsCountOnly()
    {
        Pta pta = MakePta(60);
        int nalloc = pta.Nalloc;
        pta.Empty();
        Assert.Equal(0, pta.Count);
        Assert.Equal(nalloc, pta.Nalloc);
        pta.AddPt(1, 2);
        Assert.Equal(1, pta.Count);
    }

    [Fact]
    public void InsertPt()
    {
        Pta pta = Pta.Create(0);
        pta.AddPt(1, 1);
        pta.AddPt(3, 3);
        pta.InsertPt(1, 2, 20);
        pta.InsertPt(0, 0, 0);
        pta.InsertPt(4, 4, 4);   /* append */
        Assert.Equal(5, pta.Count);
        for (int i = 0; i < 5; i++) {
            pta.GetPt(i, out float x, out _);
            Assert.Equal((float)i, x);
        }
        pta.GetPt(2, out _, out float y);
        Assert.Equal(20f, y);

        Assert.Throws<ArgumentException>(() => pta.InsertPt(-1, 0, 0));
        Assert.Throws<ArgumentException>(() => pta.InsertPt(6, 0, 0));
        Assert.Equal(5, pta.Count);
    }

    [Fact]
    public void InsertPt_WhenArrayIsFull()
    {
        Pta pta = Pta.Create(2);
        pta.AddPt(1, 1);
        pta.AddPt(2, 2);
        pta.InsertPt(0, 0, 0);
        Assert.Equal(3, pta.Count);
        Assert.Equal(4, pta.Nalloc);
        for (int i = 0; i < 3; i++) {
            pta.GetPt(i, out float x, out _);
            Assert.Equal((float)i, x);
        }
    }

    [Fact]
    public void RemovePt()
    {
        Pta pta = MakePta(5);
        pta.RemovePt(1);
        Assert.Equal(4, pta.Count);
        pta.GetPt(1, out float x, out _);
        Assert.Equal(2.5f, x);
        pta.RemovePt(3);
        Assert.Equal(3, pta.Count);
        pta.GetPt(2, out x, out _);
        Assert.Equal(3.5f, x);

        Assert.Throws<ArgumentException>(() => pta.RemovePt(3));
        Assert.Throws<ArgumentException>(() => pta.RemovePt(-1));
        Assert.Equal(3, pta.Count);
    }

    /*------------------------------ Pta I/O ----------------------------*/

    [Fact]
    public void ExactTextFormat_Float()
    {
        Pta pta = Pta.Create(0);
        pta.AddPt(1.5f, -2.25f);
        pta.AddPt(0.1f, 1234567.5f);
        pta.AddPt(0f, 1e-7f);
        Assert.Equal(
            "\n Pta Version 1\n" +
            " Number of pts = 3; format = float\n" +
            "   (1.500000, -2.250000)\n" +
            "   (0.100000, 1234567.500000)\n" +
            "   (0.000000, 0.000000)\n",
            Text(pta.WriteMem(0)));
    }

    [Fact]
    public void ExactTextFormat_Integer()
    {
        Pta pta = Pta.Create(0);
        pta.AddPt(1.5f, -2.25f);
        pta.AddPt(3.4f, 7f);
        /* (int)(x + 0.5) truncates toward zero: -2.25 + 0.5 = -1.75 gives -1 */
        Assert.Equal(
            "\n Pta Version 1\n" +
            " Number of pts = 2; format = integer\n" +
            "   (2, -1)\n" +
            "   (3, 7)\n",
            Text(pta.WriteMem(1)));
    }

    [Fact]
    public void ExactTextFormat_Empty()
    {
        Assert.Equal("\n Pta Version 1\n Number of pts = 0; format = float\n",
            Text(Pta.Create(0).WriteMem(0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RoundTrip(int type)
    {
        Pta pta = Pta.Create(0);
        for (int i = 0; i < 120; i++)
            pta.AddPt(i * 0.5f + 7f, 1000f - 3.25f * i);
        byte[] data = pta.WriteMem(type);
        Pta back = Pta.ReadMem(data);
        Assert.Equal(pta.Count, back.Count);
        for (int i = 0; i < pta.Count; i++) {
            if (type == 0) {
                pta.GetPt(i, out float x, out float y);
                back.GetPt(i, out float bx, out float by);
                Assert.Equal((x, y), (bx, by));
            } else {
                pta.GetIPt(i, out int x, out int y);
                back.GetIPt(i, out int bx, out int by);
                Assert.Equal((x, y), (bx, by));
            }
        }
        Assert.Equal(data, back.WriteMem(type));
    }

    [Fact]
    public void RoundTrip_Empty()
    {
        Pta back = Pta.ReadMem(Pta.Create(0).WriteMem(0));
        Assert.Equal(0, back.Count);
    }

    [Fact]
    public void RoundTrip_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pta_{Guid.NewGuid():N}.pta");
        try {
            Pta pta = MakePta(4);
            pta.Write(path, 0);
            Pta back = Pta.Read(path);
            Assert.Equal(Text(pta.WriteMem(0)), Text(back.WriteMem(0)));
            Assert.Throws<LeptonicaException>(() => Pta.Read(path + ".missing"));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteDebug_IsDisabledByDefault()
    {
        using var debug = new LeptDebugScope(false);
        string path = Path.Combine(Path.GetTempPath(), $"pta_{Guid.NewGuid():N}.pta");
        MakePta(2).WriteDebug(path, 0);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Read_AnyTypeOtherThanFloatIsInteger()
    {
        string text = "\n Pta Version 1\n Number of pts = 1; format = whatever\n   (4, 5)\n";
        Pta pta = Pta.ReadMem(Encoding.ASCII.GetBytes(text));
        pta.GetPt(0, out float x, out float y);
        Assert.Equal((4f, 5f), (x, y));
    }

    [Fact]
    public void Read_Errors()
    {
        static void Check(string text) =>
            Assert.Throws<ArgumentException>(() => Pta.ReadMem(Encoding.ASCII.GetBytes(text)));

        Check("");
        Check("garbage");
        Check("\n Pta Version 2\n Number of pts = 0; format = float\n");
        Check("\n Pta Version 1\n Number of pts = -1; format = float\n");
        Check("\n Pta Version 1\n Number of pts = 100000001; format = float\n");
        Check("\n Pta Version 1\n Number of pts = 2; format = float\n   (1.0, 2.0)\n");
        Check("\n Pta Version 1\n Number of pts = 1; format = integer\n   (1.5, 2)\n");
    }

    [Fact]
    public void ReadStream_LeavesSeekableStreamAfterTheData()
    {
        byte[] data = MakePta(2).WriteMem(0);
        byte[] tail = Encoding.ASCII.GetBytes("TAIL");
        byte[] all = new byte[data.Length + tail.Length];
        data.CopyTo(all, 0);
        tail.CopyTo(all, data.Length);

        using MemoryStream ms = new MemoryStream(all);
        Pta.ReadStream(ms);
        Assert.Equal(data.Length, ms.Position);
    }

    /*------------------------------- Ptaa ------------------------------*/

    [Fact]
    public void Ptaa_Create_AddPta()
    {
        Assert.Equal(50, Ptaa.Create(0).Nalloc);
        Assert.Equal(50, Ptaa.Create(10000001).Nalloc);

        Ptaa ptaa = Ptaa.Create(2);
        Pta a = MakePta(2);
        ptaa.AddPta(a, L.L_INSERT);
        ptaa.AddPta(a, L.L_COPY);
        ptaa.AddPta(a, L.L_CLONE);
        Assert.Equal(3, ptaa.Count);
        Assert.Equal(4, ptaa.Nalloc);
        Assert.Same(a, ptaa.GetPta(0, L.L_CLONE));
        Assert.NotSame(a, ptaa.GetPta(1, L.L_CLONE));
        Assert.Same(a, ptaa.GetPta(2, L.L_CLONE));
        Assert.NotSame(a, ptaa.GetPta(0, L.L_COPY));
        Assert.Throws<ArgumentException>(() => ptaa.AddPta(a, 17));
        Assert.Throws<ArgumentException>(() => ptaa.GetPta(0, L.L_INSERT));
        Assert.Throws<ArgumentException>(() => ptaa.GetPta(3, L.L_CLONE));
    }

    [Fact]
    public void Ptaa_GetPt_AddPt()
    {
        Ptaa ptaa = Ptaa.Create(0);
        ptaa.AddPta(MakePta(3), L.L_INSERT);
        ptaa.AddPt(0, 9.5f, 8.5f);
        ptaa.GetPt(0, 3, out float x, out float y);
        Assert.Equal((9.5f, 8.5f), (x, y));
        Assert.Throws<ArgumentException>(() => ptaa.GetPt(0, 4, out _, out _));
        Assert.Throws<ArgumentException>(() => ptaa.GetPt(1, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => ptaa.AddPt(1, 0, 0));
    }

    [Fact]
    public void Ptaa_InitFull_ReplacePta()
    {
        Ptaa ptaa = Ptaa.Create(3);
        Pta pta = MakePta(2);
        ptaa.InitFull(pta);
        Assert.Equal(3, ptaa.Count);
        for (int i = 0; i < 3; i++) {
            Pta p = ptaa.GetPta(i, L.L_CLONE);
            Assert.NotSame(pta, p);
            Assert.Equal(2, p.Count);
        }
        Pta other = MakePta(5);
        ptaa.ReplacePta(1, other);
        Assert.Same(other, ptaa.GetPta(1, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => ptaa.ReplacePta(3, other));
    }

    [Fact]
    public void Ptaa_Truncate()
    {
        Ptaa ptaa = Ptaa.Create(0);
        ptaa.AddPta(MakePta(2), L.L_INSERT);
        ptaa.AddPta(MakePta(0), L.L_INSERT);
        ptaa.AddPta(MakePta(1), L.L_INSERT);
        ptaa.AddPta(MakePta(0), L.L_INSERT);
        ptaa.AddPta(MakePta(0), L.L_INSERT);
        ptaa.Truncate();
        Assert.Equal(3, ptaa.Count);   /* stops at the last non-empty pta */

        Ptaa allEmpty = Ptaa.Create(0);
        allEmpty.AddPta(MakePta(0), L.L_INSERT);
        allEmpty.AddPta(MakePta(0), L.L_INSERT);
        allEmpty.Truncate();
        Assert.Equal(0, allEmpty.Count);
    }

    [Fact]
    public void Ptaa_ExactTextFormat()
    {
        Ptaa ptaa = Ptaa.Create(0);
        Pta a = Pta.Create(0);
        a.AddPt(1.5f, 2f);
        ptaa.AddPta(a, L.L_INSERT);
        ptaa.AddPta(Pta.Create(0), L.L_INSERT);
        Assert.Equal(
            "\nPtaa Version 1\n" +
            "Number of Pta = 2\n" +
            "\n Pta Version 1\n" +
            " Number of pts = 1; format = float\n" +
            "   (1.500000, 2.000000)\n" +
            "\n Pta Version 1\n" +
            " Number of pts = 0; format = float\n",
            Text(ptaa.WriteMem(0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Ptaa_RoundTrip(int type)
    {
        Ptaa ptaa = Ptaa.Create(0);
        for (int i = 0; i < 4; i++)
            ptaa.AddPta(MakePta(i), L.L_INSERT);   /* includes an empty pta */
        byte[] data = ptaa.WriteMem(type);
        Ptaa back = Ptaa.ReadMem(data);
        Assert.Equal(4, back.Count);
        for (int i = 0; i < 4; i++)
            Assert.Equal(i, back.GetPta(i, L.L_CLONE).Count);
        Assert.Equal(data, back.WriteMem(type));
    }

    [Fact]
    public void Ptaa_RoundTrip_File_And_Empty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ptaa_{Guid.NewGuid():N}.pa");
        try {
            Ptaa ptaa = Ptaa.Create(0);
            ptaa.AddPta(MakePta(3), L.L_INSERT);
            ptaa.Write(path, 0);
            Assert.Equal(Text(ptaa.WriteMem(0)), Text(Ptaa.Read(path).WriteMem(0)));
        } finally {
            File.Delete(path);
        }
        Assert.Equal(0, Ptaa.ReadMem(Ptaa.Create(0).WriteMem(0)).Count);
    }

    [Fact]
    public void Ptaa_Read_Errors()
    {
        static void Check(string text) =>
            Assert.Throws<ArgumentException>(() => Ptaa.ReadMem(Encoding.ASCII.GetBytes(text)));

        Check("");
        Check("\nPtaa Version 2\nNumber of Pta = 0\n");
        Check("\nPtaa Version 1\nNumber of Pta = -1\n");
        Check("\nPtaa Version 1\nNumber of Pta = 10000001\n");
        Check("\nPtaa Version 1\nNumber of Pta = 1\n");
        Check("\nPtaa Version 1\nNumber of Pta = 1\n\n Pta Version 1\n Number of pts = 1; format = float\n");
    }
}
