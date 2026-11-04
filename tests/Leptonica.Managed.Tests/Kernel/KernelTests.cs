using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class KernelTests
{
    private static string Text(byte[] data) => Encoding.ASCII.GetString(data);

    private static byte[] Serialize(L_Kernel kel)
    {
        using MemoryStream ms = new MemoryStream();
        kel.WriteStream(ms);
        return ms.ToArray();
    }

    private static L_Kernel Make(int h, int w, int cy, int cx, params float[] values)
    {
        L_Kernel kel = L_Kernel.Create(h, w);
        kel.SetOrigin(cy, cx);
        int k = 0;
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                kel.SetElement(i, j, values[k++]);
        return kel;
    }

    private static string TempFile()
    {
        return Path.Combine(Path.GetTempPath(), "kernel_" + Guid.NewGuid().ToString("N") + ".txt");
    }

    /*------------------------------ creation ------------------------------*/

    [Fact]
    public void Create_AllZeroAndSized()
    {
        L_Kernel kel = L_Kernel.Create(3, 4);
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 4, 0, 0), (sy, sx, cy, cx));
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 4; j++)
                Assert.Equal(0.0f, kel.GetElement(i, j));
    }

    [Fact]
    public void Create_RejectsBadSizes()
    {
        Assert.Throws<ArgumentException>(() => L_Kernel.Create(0, 3));
        Assert.Throws<ArgumentException>(() => L_Kernel.Create(3, 0));
        Assert.Throws<ArgumentException>(() => L_Kernel.Create(-1, 3));
        Assert.Throws<ArgumentException>(() => L_Kernel.Create(1 << 15, 1 << 15));   /* >= 2^29 elements */
        Assert.Throws<ArgumentException>(() => L_Kernel.Create(1, 100001));          /* row too long */
        Assert.Throws<ArgumentException>(() => L_Kernel.Create(100001, 1));
    }

    [Fact]
    public void Create2dFloatArray_Bounds()
    {
        float[][] a = L_Kernel.Create2dFloatArray(2, 5);
        Assert.Equal(2, a.Length);
        Assert.Equal(5, a[1].Length);
        Assert.Throws<ArgumentException>(() => L_Kernel.Create2dFloatArray(0, 5));
        Assert.Throws<ArgumentException>(() => L_Kernel.Create2dFloatArray(5, 0));
        Assert.Throws<ArgumentException>(() => L_Kernel.Create2dFloatArray(5, 100001));
        Assert.NotNull(L_Kernel.Create2dFloatArray(1, 100000));
    }

    [Fact]
    public void Copy_IsDeep()
    {
        L_Kernel kels = Make(2, 2, 1, 0, 1f, 2f, 3f, 4f);
        L_Kernel keld = kels.Copy();
        kels.SetElement(0, 0, 99f);
        kels.SetOrigin(0, 0);
        keld.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 2, 1, 0), (sy, sx, cy, cx));
        Assert.Equal(1f, keld.GetElement(0, 0));
        Assert.Equal(4f, keld.GetElement(1, 1));
    }

    /*------------------------------ accessors -----------------------------*/

    [Fact]
    public void ElementAccess_Bounds()
    {
        L_Kernel kel = L_Kernel.Create(2, 3);
        kel.SetElement(1, 2, 7.5f);
        Assert.Equal(7.5f, kel.GetElement(1, 2));
        Assert.Throws<ArgumentException>(() => kel.GetElement(2, 0));
        Assert.Throws<ArgumentException>(() => kel.GetElement(0, 3));
        Assert.Throws<ArgumentException>(() => kel.GetElement(-1, 0));
        Assert.Throws<ArgumentException>(() => kel.SetElement(0, -1, 1f));
        Assert.Throws<ArgumentException>(() => kel.SetElement(2, 0, 1f));
    }

    [Fact]
    public void SumAndMinMax()
    {
        L_Kernel kel = Make(2, 3, 0, 0, 1f, -2.5f, 0.25f, 3f, 0f, 100.125f);
        Assert.Equal(101.875f, kel.GetSum());
        kel.GetMinMax(out float min, out float max);
        Assert.Equal(-2.5f, min);
        Assert.Equal(100.125f, max);
    }

    /*------------------------ normalization, inversion --------------------*/

    [Fact]
    public void Normalize_ScalesToRequestedSum()
    {
        L_Kernel kels = Make(2, 2, 1, 1, 1f, 2f, 3f, 4f);
        L_Kernel keld = kels.Normalize(1.0f);
        float factor = 1.0f / 10.0f;
        keld.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 2, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(factor * 1f, keld.GetElement(0, 0));
        Assert.Equal(factor * 2f, keld.GetElement(0, 1));
        Assert.Equal(factor * 3f, keld.GetElement(1, 0));
        Assert.Equal(factor * 4f, keld.GetElement(1, 1));
        Assert.Equal(1.0f, keld.GetSum(), 5);

        L_Kernel kel2 = kels.Normalize(5.0f);
        Assert.Equal(0.5f * 3f, kel2.GetElement(1, 0));
        Assert.Equal(10f, kels.GetSum());   /* source unchanged */
    }

    [Fact]
    public void Normalize_NullSumReturnsCopy()
    {
        L_Kernel kels = Make(1, 3, 0, 1, -1f, 0f, 1f);
        L_Kernel keld = kels.Normalize(1.0f);
        Assert.NotSame(kels, keld);
        Assert.Equal(Text(Serialize(kels)), Text(Serialize(keld)));
    }

    [Fact]
    public void Invert_ReversesAboutTheOrigin()
    {
        L_Kernel kels = Make(2, 3, 0, 1, 1f, 2f, 3f, 4f, 5f, 6f);
        L_Kernel keld = kels.Invert();
        keld.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 3, 1, 1), (sy, sx, cy, cx));
        float[] expected = { 6f, 5f, 4f, 3f, 2f, 1f };
        int k = 0;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(expected[k++], keld.GetElement(i, j));
        Assert.Equal(1f, kels.GetElement(0, 0));   /* source unchanged */
    }

    /*----------------------------- parametric kernels ---------------------*/

    [Fact]
    public void FlatKernel_IsUniform()
    {
        L_Kernel kel = L_Kernel.MakeFlatKernel(3, 3, 1, 1);
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 3, 1, 1), (sy, sx, cy, cx));
        float normval = 1.0f / 9.0f;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(normval, kel.GetElement(i, j));
        Assert.Equal(1.0f, kel.GetSum(), 5);

        L_Kernel kel2 = L_Kernel.MakeFlatKernel(2, 4, 0, 3);
        Assert.Equal(0.125f, kel2.GetElement(1, 2));
    }

    [Fact]
    public void GaussianKernel_HandCheckedValues()
    {
        L_Kernel kel = L_Kernel.MakeGaussianKernel(1, 2, 1.0f, 1.0f);
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 5, 1, 2), (sy, sx, cy, cx));
        Assert.Equal(1.0f, kel.GetElement(1, 2));                          /* exp(0) */
        Assert.Equal(MathF.Exp(-0.5f), kel.GetElement(0, 2), 6);           /* d2 = 1 */
        Assert.Equal(0.60653066f, kel.GetElement(1, 1), 6);
        Assert.Equal(0.36787945f, kel.GetElement(0, 1), 6);                /* d2 = 2 */
        Assert.Equal(0.13533528f, kel.GetElement(1, 0), 6);                /* d2 = 4: exp(-2) */
        Assert.Equal(kel.GetElement(0, 1), kel.GetElement(2, 3));          /* symmetry */

        L_Kernel kel2 = L_Kernel.MakeGaussianKernel(1, 1, 2.0f, 10.0f);
        Assert.Equal(10.0f, kel2.GetElement(1, 1));
        Assert.Equal(10.0f * 0.88249690f, kel2.GetElement(0, 1), 5);       /* exp(-1/8) */
    }

    [Fact]
    public void GaussianKernelSep_ProductEqualsFullKernel()
    {
        L_Kernel.MakeGaussianKernelSep(2, 3, 1.5f, 8.0f, out L_Kernel kelx, out L_Kernel kely);
        kelx.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((1, 7, 0, 3), (sy, sx, cy, cx));
        kely.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((5, 1, 2, 0), (sy, sx, cy, cx));
        Assert.Equal(8.0f, kelx.GetElement(0, 3));
        Assert.Equal(1.0f, kely.GetElement(2, 0));

        L_Kernel full = L_Kernel.MakeGaussianKernel(2, 3, 1.5f, 8.0f);
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 7; j++)
                Assert.Equal(full.GetElement(i, j), kely.GetElement(i, 0) * kelx.GetElement(0, j), 4);
    }

    [Fact]
    public void DoGKernel_HandCheckedValues()
    {
        /* ratio 1 is a no-op: both gaussians are the same */
        L_Kernel kel = L_Kernel.MakeDoGKernel(2, 2, 1.0f, 1.0f);
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((5, 5, 2, 2), (sy, sx, cy, cx));
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                Assert.Equal(0.0f, kel.GetElement(i, j));

        /* center value: (1/2 - 1/8) / pi */
        L_Kernel kel2 = L_Kernel.MakeDoGKernel(1, 1, 1.0f, 2.0f);
        Assert.Equal(0.375f / 3.1415926535f, kel2.GetElement(1, 1), 6);
        /* d2 = 1: (0.5 exp(-0.5) - 0.125 exp(-0.125)) / pi */
        float expected = (0.5f * MathF.Exp(-0.5f) - 0.125f * MathF.Exp(-0.125f)) / 3.1415926535f;
        Assert.Equal(expected, kel2.GetElement(0, 1), 6);
        Assert.Equal(kel2.GetElement(0, 1), kel2.GetElement(1, 0));
        /* null total sum, approximately, for a large enough support */
        L_Kernel kel3 = L_Kernel.MakeDoGKernel(12, 12, 1.5f, 2.0f);
        Assert.Equal(0.0f, kel3.GetSum(), 3);
    }

    /*----------------------------- serialization --------------------------*/

    private static string Row(params string[] cells)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string c in cells)
            sb.Append(c.PadLeft(15));
        return sb.Append('\n').ToString();
    }

    [Fact]
    public void WriteStream_ExactText()
    {
        L_Kernel kel = Make(2, 3, 1, 2, 1f, -2.5f, 0.25f, 3f, 0f, 100.125f);
        string expected = "  Kernel Version 2\n" +
                          "  sy = 2, sx = 3, cy = 1, cx = 2\n" +
                          Row("1.0000", "-2.5000", "0.2500") +
                          Row("3.0000", "0.0000", "100.1250") +
                          "\n";
        Assert.Equal(expected, Text(Serialize(kel)));
        Assert.Equal("  Kernel Version 2\n  sy = 2, sx = 3, cy = 1, cx = 2\n" +
                     "         1.0000        -2.5000         0.2500\n" +
                     "         3.0000         0.0000       100.1250\n\n",
                     Text(Serialize(kel)));
    }

    [Fact]
    public void WriteStream_RoundsToFourDecimals()
    {
        L_Kernel kel = Make(1, 3, 0, 0, 0.12345f, 1f / 3f, -0.00004f);
        string expected = "  Kernel Version 2\n  sy = 1, sx = 3, cy = 0, cx = 0\n" +
                          Row("0.1235", "0.3333", "-0.0000") + "\n";
        Assert.Equal(expected, Text(Serialize(kel)));
    }

    [Fact]
    public void ReadStream_RoundTrip()
    {
        L_Kernel kel = Make(2, 3, 1, 2, 1f, -2.5f, 0.25f, 3f, 0f, 100.125f);
        byte[] bytes = Serialize(kel);
        using MemoryStream ms = new MemoryStream(bytes);
        L_Kernel back = L_Kernel.ReadStream(ms);
        back.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 3, 1, 2), (sy, sx, cy, cx));
        Assert.Equal(Text(bytes), Text(Serialize(back)));
        Assert.Equal(-2.5f, back.GetElement(0, 1));
        Assert.Equal(100.125f, back.GetElement(1, 2));
        Assert.Equal(bytes.Length, ms.Position);   /* all text consumed, including the final blank line */
    }

    [Fact]
    public void ReadStream_TwoKernelsInOneStream()
    {
        L_Kernel a = Make(1, 2, 0, 1, 1.5f, 2.5f);
        L_Kernel b = Make(2, 1, 1, 0, -1f, 4f);
        using MemoryStream ms = new MemoryStream();
        a.WriteStream(ms);
        b.WriteStream(ms);
        ms.Position = 0;
        L_Kernel a2 = L_Kernel.ReadStream(ms);
        L_Kernel b2 = L_Kernel.ReadStream(ms);
        Assert.Equal(Text(Serialize(a)), Text(Serialize(a2)));
        Assert.Equal(Text(Serialize(b)), Text(Serialize(b2)));
    }

    [Fact]
    public void ReadStream_RejectsBadInput()
    {
        static void Check(string text)
        {
            using MemoryStream ms = new MemoryStream(Encoding.ASCII.GetBytes(text));
            Assert.Throws<ArgumentException>(() => L_Kernel.ReadStream(ms));
        }
        Check("");
        Check("  Not a kernel 2\n");
        Check("  Kernel Version 3\n  sy = 1, sx = 1, cy = 0, cx = 0\n         1.0000\n\n");
        Check("  Kernel Version 2\n  sy = 1, sx = 1\n");
        Check("  Kernel Version 2\n  sy = 0, sx = 1, cy = 0, cx = 0\n");
        Check("  Kernel Version 2\n  sy = 100001, sx = 1, cy = 0, cx = 0\n");
        Check("  Kernel Version 2\n  sy = -1, sx = 1, cy = 0, cx = 0\n");   /* negative is > MaxArraySize as unsigned */
    }

    [Fact]
    public void WriteRead_ThroughFile()
    {
        string path = TempFile();
        try {
            L_Kernel kel = Make(3, 2, 2, 1, 1f, 2f, 3f, 4f, 5f, 6.75f);
            kel.Write(path);
            Assert.Equal(Text(Serialize(kel)), File.ReadAllText(path, Encoding.ASCII));
            L_Kernel back = L_Kernel.Read(path);
            Assert.Equal(Text(Serialize(kel)), Text(Serialize(back)));
        } finally {
            File.Delete(path);
        }
        Assert.ThrowsAny<ArgumentException>(() => L_Kernel.Read(path));
    }

    /*------------------------- creation from text / pix -------------------*/

    [Fact]
    public void CreateFromString_ParsesRowMajor()
    {
        L_Kernel kel = L_Kernel.CreateFromString(3, 3, 1, 1,
            " 20   50   20 " +
            " 70  140   70 " +
            " 20   50   20 ");
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 3, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(20f, kel.GetElement(0, 0));
        Assert.Equal(140f, kel.GetElement(1, 1));
        Assert.Equal(70f, kel.GetElement(1, 2));
        Assert.Equal(20f, kel.GetElement(2, 2));
        Assert.Equal(460f, kel.GetSum());
    }

    [Fact]
    public void CreateFromString_FloatsAndNegatives()
    {
        L_Kernel kel = L_Kernel.CreateFromString(1, 4, 0, 2, "\t-1.5 2\n0.25   -255");
        Assert.Equal(-1.5f, kel.GetElement(0, 0));
        Assert.Equal(2f, kel.GetElement(0, 1));
        Assert.Equal(0.25f, kel.GetElement(0, 2));
        Assert.Equal(-255f, kel.GetElement(0, 3));
    }

    [Fact]
    public void CreateFromString_RejectsBadArguments()
    {
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromString(0, 3, 0, 0, "1 2 3"));
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromString(1, 0, 0, 0, "1 2 3"));
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromString(1, 3, 1, 0, "1 2 3"));   /* cy */
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromString(1, 3, 0, 3, "1 2 3"));   /* cx */
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromString(1, 3, 0, 0, "1 2"));     /* count */
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromString(1, 3, 0, 0, "1 2 3 4"));
    }

    [Fact]
    public void ParseStringForNumbers_IntsAndFloats()
    {
        Numa na = L_Kernel.ParseStringForNumbers("1.5, 2 ;-3e1", " ,;");
        Assert.Equal(3, na.Count);
        Assert.Equal(1.5f, na.GetFValue(0));
        Assert.Equal(2f, na.GetFValue(1));
        Assert.Equal(-30f, na.GetFValue(2));

        Numa nb = L_Kernel.ParseStringForNumbers("  7\t8\n9  ", " \t\n");
        Assert.Equal(3, nb.Count);
        Assert.Equal(9f, nb.GetFValue(2));

        /* not a number: atof() gives 0 */
        Numa nc = L_Kernel.ParseStringForNumbers("4 x 5", " ");
        Assert.Equal(3, nc.Count);
        Assert.Equal(0f, nc.GetFValue(1));
    }

    [Fact]
    public void CreateFromFile_SkipsCommentsAndStopsAtBlankLine()
    {
        string path = TempFile();
        try {
            File.WriteAllText(path,
                "# small 3x3 kernel\n" +
                "# another comment\n" +
                "3 3\n" +
                "1 1\n" +
                "25.5   51    24.3\n" +
                "70.2  146.3  73.4\n" +
                "20     50.9  18.4\n" +
                "\n" +
                "99 99 99\n");
            L_Kernel kel = L_Kernel.CreateFromFile(path);
            kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
            Assert.Equal((3, 3, 1, 1), (sy, sx, cy, cx));
            Assert.Equal(25.5f, kel.GetElement(0, 0));
            Assert.Equal(24.3f, kel.GetElement(0, 2));
            Assert.Equal(146.3f, kel.GetElement(1, 1));
            Assert.Equal(18.4f, kel.GetElement(2, 2));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateFromFile_DataEndsAtEndOfFileOrComment()
    {
        string path = TempFile();
        try {
            File.WriteAllText(path, "2 2\r\n0 0\r\n1 2\r\n3 4\r\n# trailing\r\n5 6\r\n");
            L_Kernel kel = L_Kernel.CreateFromFile(path);
            Assert.Equal(10f, kel.GetSum());
            Assert.Equal(3f, kel.GetElement(1, 0));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateFromFile_RejectsBadFiles()
    {
        string path = TempFile();
        try {
            Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromFile(path));   /* missing */
            File.WriteAllText(path, "");
            Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromFile(path));   /* empty */
            File.WriteAllText(path, "hello\n1 1\n5\n");
            Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromFile(path));   /* h,w */
            File.WriteAllText(path, "1 1\nx\n5\n");
            Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromFile(path));   /* cy,cx */
            File.WriteAllText(path, "2 2\n0 0\n1 2 3\n");
            Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromFile(path));   /* count */
            File.WriteAllText(path, "100001 1\n0 0\n1\n");
            Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromFile(path));   /* too large */
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateFromPix_UsesPixelValues()
    {
        Pix pix = Pix.Create(3, 2, 8);
        byte[] vals = { 0, 10, 255, 7, 8, 9 };
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
                pix.SetPixel(j, i, vals[i * 3 + j]);
        L_Kernel kel = L_Kernel.CreateFromPix(pix, 1, 2);
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 3, 1, 2), (sy, sx, cy, cx));
        Assert.Equal(255f, kel.GetElement(0, 2));
        Assert.Equal(8f, kel.GetElement(1, 1));
    }

    [Fact]
    public void CreateFromPix_RejectsBadInput()
    {
        Pix pix8 = Pix.Create(3, 2, 8);
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromPix(Pix.Create(3, 2, 1), 0, 0));
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromPix(pix8, 2, 0));
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromPix(pix8, 0, 3));
        Assert.Throws<ArgumentException>(() => L_Kernel.CreateFromPix(pix8, -1, 0));
    }

    /*------------------------------- display ------------------------------*/

    [Fact]
    public void DisplayInPix_OnePixelPerElement()
    {
        /* max |value| is 2, so the scale is 127.5 */
        L_Kernel kel = Make(2, 2, 0, 0, 1f, -2f, 0.5f, 0f);
        Pix pix = kel.DisplayInPix(1, 0);
        pix.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((2, 2, 8), (w, h, d));
        pix.GetPixel(0, 0, out uint v00);
        pix.GetPixel(1, 0, out uint v10);
        pix.GetPixel(0, 1, out uint v01);
        pix.GetPixel(1, 1, out uint v11);
        Assert.Equal(127u, v00);   /* (int)127.5 */
        Assert.Equal(255u, v10);
        Assert.Equal(63u, v01);    /* (int)63.75 */
        Assert.Equal(0u, v11);
    }

    [Fact]
    public void DisplayInPix_AllZeroKernelIsRejected()
    {
        Assert.Throws<ArgumentException>(() => L_Kernel.Create(2, 2).DisplayInPix(1, 0));
    }
}
