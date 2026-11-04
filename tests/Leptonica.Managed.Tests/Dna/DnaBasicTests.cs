using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class DnaBasicTests
{
    private static string Text(byte[] data) => Encoding.ASCII.GetString(data);
    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    private static L_Dna Make(params double[] values)
    {
        L_Dna da = L_Dna.Create(0);
        foreach (double v in values)
            da.AddNumber(v);
        return da;
    }

    private static void AssertValues(L_Dna da, params double[] expected)
    {
        Assert.Equal(expected.Length, da.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], da.GetDValue(i));
    }

    /*------------------------------ creation ------------------------------*/

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-3, 50)]
    [InlineData(100000001, 50)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Create_AllocatedSize(int n, int expectedAlloc)
    {
        L_Dna da = L_Dna.Create(n);
        Assert.Equal(expectedAlloc, da.Nalloc);
        Assert.Equal(0, da.Count);
        da.GetParameters(out double startx, out double delx);
        Assert.Equal((0.0, 1.0), (startx, delx));
    }

    [Fact]
    public void CreateFromIArray_CopiesValues()
    {
        int[] src = { 3, -4, 5 };
        L_Dna da = L_Dna.CreateFromIArray(src, 3);
        AssertValues(da, 3, -4, 5);
        src[0] = 99;
        Assert.Equal(3, da.GetIValue(0));
        Assert.Throws<ArgumentException>(() => L_Dna.CreateFromIArray(src, 0));
    }

    [Fact]
    public void CreateFromDArray_CopyAndInsert()
    {
        double[] src = { 1.5, 2.5, 3.5 };
        L_Dna copy = L_Dna.CreateFromDArray(src, 3, L.L_COPY);
        src[0] = 99;
        AssertValues(copy, 1.5, 2.5, 3.5);

        double[] owned = { 1.0, 2.0 };
        L_Dna inserted = L_Dna.CreateFromDArray(owned, 2, L.L_INSERT);
        AssertValues(inserted, 1.0, 2.0);
        Assert.Same(owned, inserted.GetDArray(L.L_NOCOPY));
        owned[1] = 7.0;
        Assert.Equal(7.0, inserted.GetDValue(1));

        Assert.Throws<ArgumentException>(() => L_Dna.CreateFromDArray(src, 0, L.L_COPY));
        Assert.Throws<ArgumentException>(() => L_Dna.CreateFromDArray(src, 3, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => L_Dna.CreateFromDArray(src, 4, L.L_COPY));
    }

    [Fact]
    public void MakeSequence_EvenlySpaced()
    {
        L_Dna da = L_Dna.MakeSequence(1.0, 0.5, 4);
        AssertValues(da, 1.0, 1.5, 2.0, 2.5);
        Assert.Equal(0, L_Dna.MakeSequence(0.0, 1.0, 0).Count);
    }

    /*------------------------------ add / extend ------------------------------*/

    [Fact]
    public void AddNumber_ExtendsByDoubling()
    {
        L_Dna da = L_Dna.Create(2);
        da.AddNumber(1);
        da.AddNumber(2);
        Assert.Equal(2, da.Nalloc);
        da.AddNumber(3);
        Assert.Equal(4, da.Nalloc);
        for (int i = 4; i < 9; i++)
            da.AddNumber(i);
        AssertValues(da, 1, 2, 3, 4, 5, 6, 7, 8);
        Assert.Equal(8, da.Nalloc);
        da.AddNumber(9);
        Assert.Equal(16, da.Nalloc);
    }

    [Fact]
    public void Empty_KeepsAllocation()
    {
        L_Dna da = Make(1, 2, 3);
        int nalloc = da.Nalloc;
        da.Empty();
        Assert.Equal(0, da.Count);
        Assert.Equal(nalloc, da.Nalloc);
        Assert.Throws<ArgumentException>(() => da.GetDValue(0));
    }

    /*------------------------------ insert / remove / replace ------------------------------*/

    [Fact]
    public void InsertNumber_ShiftsUp()
    {
        L_Dna da = Make(1, 2, 3);
        da.InsertNumber(0, 0.5);
        da.InsertNumber(2, 1.5);
        da.InsertNumber(5, 4.0);
        AssertValues(da, 0.5, 1, 1.5, 2, 3, 4.0);
        Assert.Throws<ArgumentException>(() => da.InsertNumber(-1, 0));
        Assert.Throws<ArgumentException>(() => da.InsertNumber(7, 0));
        Assert.Equal(6, da.Count);
    }

    [Fact]
    public void InsertNumber_ExtendsWhenFull()
    {
        L_Dna da = L_Dna.Create(2);
        da.AddNumber(1);
        da.AddNumber(3);
        da.InsertNumber(1, 2);
        Assert.Equal(4, da.Nalloc);
        AssertValues(da, 1, 2, 3);
    }

    [Fact]
    public void RemoveNumber_ShiftsDown()
    {
        L_Dna da = Make(1, 2, 3, 4);
        da.RemoveNumber(1);
        AssertValues(da, 1, 3, 4);
        da.RemoveNumber(2);
        AssertValues(da, 1, 3);
        da.RemoveNumber(0);
        AssertValues(da, 3);
        Assert.Throws<ArgumentException>(() => da.RemoveNumber(1));
        Assert.Throws<ArgumentException>(() => da.RemoveNumber(-1));
    }

    [Fact]
    public void ReplaceNumber_ChecksIndex()
    {
        L_Dna da = Make(1, 2, 3);
        da.ReplaceNumber(1, 9.25);
        AssertValues(da, 1, 9.25, 3);
        Assert.Throws<ArgumentException>(() => da.ReplaceNumber(3, 0));
        Assert.Throws<ArgumentException>(() => da.ReplaceNumber(-1, 0));
    }

    /*------------------------------ accessors ------------------------------*/

    [Fact]
    public void SetCount_ReallocsAndZeroFills()
    {
        L_Dna da = L_Dna.Create(2);
        da.AddNumber(5);
        da.Count = 6;
        Assert.Equal(6, da.Count);
        Assert.Equal(6, da.Nalloc);
        AssertValues(da, 5, 0, 0, 0, 0, 0);

        da.Count = 2;
        Assert.Equal(2, da.Count);
        Assert.Equal(6, da.Nalloc);
        da.Count = 0;
        Assert.Equal(0, da.Count);
    }

    [Fact]
    public void GetIValue_RoundsAwayFromZero()
    {
        L_Dna da = Make(2.4, 2.5, -2.4, -2.5, 0.0, -0.4, 3e9 / 3e9);
        Assert.Equal(new[] { 2, 3, -2, -3, 0, 0, 1 }, da.GetIArray());
        Assert.Equal(-3, da.GetIValue(3));
        Assert.Throws<ArgumentException>(() => da.GetIValue(7));
        Assert.Throws<ArgumentException>(() => da.GetIValue(-1));
    }

    [Fact]
    public void SetValueAndShiftValue()
    {
        L_Dna da = Make(1, 2, 3);
        da.SetValue(0, 10.5);
        da.ShiftValue(1, -0.25);
        da.ShiftValue(2, 4);
        AssertValues(da, 10.5, 1.75, 7);
        Assert.Throws<ArgumentException>(() => da.SetValue(3, 0));
        Assert.Throws<ArgumentException>(() => da.ShiftValue(-1, 0));
        Assert.Throws<ArgumentException>(() => da.GetDValue(3));
    }

    [Fact]
    public void GetDArray_CopyAndNoCopy()
    {
        L_Dna da = L_Dna.Create(8);
        da.AddNumber(1);
        da.AddNumber(2);

        double[] copy = da.GetDArray(L.L_COPY);
        Assert.Equal(new[] { 1.0, 2.0 }, copy);
        copy[0] = 99;
        Assert.Equal(1.0, da.GetDValue(0));

        double[] bare = da.GetDArray(L.L_NOCOPY);
        Assert.Equal(8, bare.Length);
        bare[1] = 42;
        Assert.Equal(42.0, da.GetDValue(1));
    }

    [Fact]
    public void Parameters_GetSetCopy()
    {
        L_Dna da = Make(1, 2);
        da.SetParameters(2.5, 0.25);
        da.GetParameters(out double startx, out double delx);
        Assert.Equal((2.5, 0.25), (startx, delx));

        L_Dna other = Make(3);
        other.CopyParameters(da);
        other.GetParameters(out startx, out delx);
        Assert.Equal((2.5, 0.25), (startx, delx));
    }

    /*------------------------------ copy / clone ------------------------------*/

    [Fact]
    public void CopyAndClone()
    {
        L_Dna da = L_Dna.Create(100);
        da.AddNumber(1);
        da.AddNumber(2);
        da.SetParameters(3.0, 4.0);

        L_Dna copy = da.Copy();
        Assert.NotSame(da, copy);
        AssertValues(copy, 1, 2);
        Assert.Equal(2, copy.Nalloc);   /* unused slots are not copied */
        copy.GetParameters(out double startx, out double delx);
        Assert.Equal((3.0, 4.0), (startx, delx));

        copy.SetValue(0, 50);
        Assert.Equal(1.0, da.GetDValue(0));

        L_Dna clone = da.Clone();
        Assert.Same(da, clone);
        clone.SetValue(1, 60);
        Assert.Equal(60.0, da.GetDValue(1));
    }

    /*------------------------------ serialization ------------------------------*/

    [Fact]
    public void WriteMem_ExactText()
    {
        L_Dna da = Make(1.5, -2, 0.1);
        Assert.Equal(
            "\nL_Dna Version 1\n" +
            "Number of numbers = 3\n" +
            "  [0] = 1.500000\n" +
            "  [1] = -2.000000\n" +
            "  [2] = 0.100000\n" +
            "\n",
            Text(da.WriteMem()));
    }

    [Fact]
    public void WriteMem_WithParametersAndEmpty()
    {
        L_Dna da = Make(1e20, 1.0 / 3.0);
        da.SetParameters(2.5, 0.125);
        Assert.Equal(
            "\nL_Dna Version 1\n" +
            "Number of numbers = 2\n" +
            "  [0] = 100000000000000000000.000000\n" +
            "  [1] = 0.333333\n" +
            "\n" +
            "startx = 2.500000, delx = 0.125000\n",
            Text(da.WriteMem()));

        L_Dna empty = L_Dna.Create(0);
        Assert.Equal("\nL_Dna Version 1\nNumber of numbers = 0\n\n", Text(empty.WriteMem()));

        /* each parameter alone is enough to write the optional line */
        empty.SetParameters(0.0, 2.0);
        Assert.EndsWith("\nstartx = 0.000000, delx = 2.000000\n", Text(empty.WriteMem()));
        empty.SetParameters(-1.0, 1.0);
        Assert.EndsWith("\nstartx = -1.000000, delx = 1.000000\n", Text(empty.WriteMem()));
    }

    [Fact]
    public void WriteStream_NullWritesToStderr()
    {
        TextWriter saved = Console.Error;
        StringWriter sw = new StringWriter();
        Console.SetError(sw);
        try {
            Make(2).WriteStream(null);
        } finally {
            Console.SetError(saved);
        }
        Assert.Equal("\nL_Dna Version 1\nNumber of numbers = 1\n  [0] = 2.000000\n\n", sw.ToString());
    }

    [Fact]
    public void WriteReadMem_RoundTrip()
    {
        L_Dna da = Make(1.5, -2, 0.1, 123456789.125, 1e-7, 0.0);
        da.SetParameters(-3.5, 0.5);
        L_Dna back = L_Dna.ReadMem(da.WriteMem());

        Assert.Equal(da.Count, back.Count);
        /* %f keeps 6 decimals: compare through the text, as the C round trip does */
        Assert.Equal(Text(da.WriteMem()), Text(back.WriteMem()));
        Assert.Equal(1.5, back.GetDValue(0));
        Assert.Equal(0.1, back.GetDValue(2));
        Assert.Equal(0.0, back.GetDValue(4));   /* 1e-7 is written as 0.000000 */
        back.GetParameters(out double startx, out double delx);
        Assert.Equal((-3.5, 0.5), (startx, delx));
    }

    [Fact]
    public void WriteRead_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dna_{Guid.NewGuid():N}.dna");
        try {
            L_Dna da = Make(4, 5.5, -6);
            da.Write(path);
            Assert.Equal(Text(da.WriteMem()), File.ReadAllText(path));
            AssertValues(L_Dna.Read(path), 4, 5.5, -6);
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => L_Dna.Read(path));
    }

    [Fact]
    public void ReadMem_EmptyAndBadInput()
    {
        Assert.Equal(0, L_Dna.ReadMem(Bytes("\nL_Dna Version 1\nNumber of numbers = 0\n\n")).Count);

        Assert.Throws<ArgumentException>(() => L_Dna.ReadMem(Bytes("")));
        Assert.Throws<ArgumentException>(() => L_Dna.ReadMem(Bytes("\nNuma Version 1\n")));
        Assert.Throws<ArgumentException>(() => L_Dna.ReadMem(Bytes("\nL_Dna Version 2\nNumber of numbers = 0\n")));
        Assert.Throws<ArgumentException>(() => L_Dna.ReadMem(Bytes("\nL_Dna Version 1\nNumber of numbers = -1\n")));
        Assert.Throws<ArgumentException>(() => L_Dna.ReadMem(Bytes("\nL_Dna Version 1\nNumber of numbers = 100000001\n")));
        Assert.Throws<ArgumentException>(() => L_Dna.ReadMem(Bytes("\nL_Dna Version 1\nNumber of numbers = 2\n  [0] = 1.0\n")));
    }

    [Fact]
    public void ReadMem_ToleratesWhitespaceAndLongInput()
    {
        /* extra blank lines and spaces are skipped by fscanf whitespace directives */
        L_Dna da = L_Dna.ReadMem(Bytes("\n\nL_Dna Version 1\n\nNumber of numbers = 2\n  [0] = 1e2\n  [1] =   -0.5  \n\n\nstartx = 1, delx = 2\n"));
        AssertValues(da, 100, -0.5);
        da.GetParameters(out double startx, out double delx);
        Assert.Equal((1.0, 2.0), (startx, delx));

        /* more data than one scan window */
        L_Dna big = L_Dna.MakeSequence(0.0, 0.5, 1000);
        L_Dna back = L_Dna.ReadMem(big.WriteMem());
        Assert.Equal(1000, back.Count);
        Assert.Equal(499.5, back.GetDValue(999));
    }

    [Fact]
    public void ReadStream_LeavesFollowingTextInSeekableStream()
    {
        byte[] data = Bytes("\nL_Dna Version 1\nNumber of numbers = 1\n  [0] = 1.0\n\nTRAILER");
        using MemoryStream ms = new MemoryStream(data);
        AssertValues(L_Dna.ReadStream(ms), 1.0);
        Assert.Equal("TRAILER", Encoding.ASCII.GetString(data, (int)ms.Position, data.Length - (int)ms.Position));
    }

    /*------------------------------ Dnaa ------------------------------*/

    private static L_Dnaa MakeDnaa()
    {
        L_Dnaa daa = L_Dnaa.Create(0);
        daa.AddDna(Make(1, 2), L.L_INSERT);
        L_Dna second = Make(3.25);
        second.SetParameters(1.0, 2.0);
        daa.AddDna(second, L.L_INSERT);
        daa.AddDna(L_Dna.Create(0), L.L_INSERT);
        return daa;
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1000001, 50)]
    [InlineData(5, 5)]
    public void DnaaCreate_AllocatedSize(int n, int expectedAlloc)
    {
        L_Dnaa daa = L_Dnaa.Create(n);
        Assert.Equal(expectedAlloc, daa.Nalloc);
        Assert.Equal(0, daa.Count);
    }

    [Fact]
    public void DnaaCreateFull_FillsWithDnas()
    {
        L_Dnaa daa = L_Dnaa.CreateFull(3, 4);
        Assert.Equal(3, daa.Count);
        Assert.Equal(0, daa.GetDnaCount(2));
        daa.AddNumber(1, 5.5);
        daa.AddNumber(1, 6.5);
        Assert.Equal(2, daa.GetDnaCount(1));
        Assert.Equal(2, daa.GetNumberCount());
        Assert.Equal(6.5, daa.GetValue(1, 1));
    }

    [Fact]
    public void DnaaAddDna_CopyFlags()
    {
        L_Dnaa daa = L_Dnaa.Create(1);
        L_Dna da = Make(1, 2);
        daa.AddDna(da, L.L_COPY);
        daa.AddDna(da, L.L_CLONE);
        daa.AddDna(da, L.L_INSERT);   /* forces the ptr array to extend */
        Assert.Equal(3, daa.Count);
        Assert.Equal(4, daa.Nalloc);
        Assert.NotSame(da, daa.GetDna(0, L.L_CLONE));
        Assert.Same(da, daa.GetDna(1, L.L_CLONE));
        Assert.Same(da, daa.GetDna(2, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => daa.AddDna(da, 17));
    }

    [Fact]
    public void DnaaGetDna_CopyCloneAndErrors()
    {
        L_Dnaa daa = MakeDnaa();
        L_Dna copy = daa.GetDna(0, L.L_COPY);
        L_Dna clone = daa.GetDna(0, L.L_CLONE);
        Assert.NotSame(copy, clone);
        copy.SetValue(0, 100);
        Assert.Equal(1.0, daa.GetValue(0, 0));
        clone.SetValue(0, 200);
        Assert.Equal(200.0, daa.GetValue(0, 0));

        Assert.Throws<ArgumentException>(() => daa.GetDna(3, L.L_COPY));
        Assert.Throws<ArgumentException>(() => daa.GetDna(-1, L.L_COPY));
        Assert.Throws<ArgumentException>(() => daa.GetDna(0, 9));
        Assert.Throws<ArgumentException>(() => daa.GetDnaCount(3));
        Assert.Throws<ArgumentException>(() => daa.GetValue(0, 2));
        Assert.Throws<ArgumentException>(() => daa.GetValue(3, 0));
        Assert.Throws<ArgumentException>(() => daa.AddNumber(3, 1.0));
    }

    [Fact]
    public void DnaaReplaceDna_AndCounts()
    {
        L_Dnaa daa = MakeDnaa();
        Assert.Equal(3, daa.Count);
        Assert.Equal(2, daa.GetDnaCount(0));
        Assert.Equal(3, daa.GetNumberCount());

        daa.ReplaceDna(2, Make(7, 8, 9));
        Assert.Equal(6, daa.GetNumberCount());
        Assert.Equal(9.0, daa.GetValue(2, 2));
        Assert.Throws<ArgumentException>(() => daa.ReplaceDna(3, Make(1)));
        Assert.Throws<ArgumentException>(() => daa.ReplaceDna(-1, Make(1)));
    }

    [Fact]
    public void DnaaTruncate_DropsTrailingEmptyDnas()
    {
        L_Dnaa daa = MakeDnaa();
        daa.AddDna(L_Dna.Create(0), L.L_INSERT);
        Assert.Equal(4, daa.Count);
        daa.Truncate();
        Assert.Equal(2, daa.Count);
        Assert.Equal(50, daa.Nalloc);   /* allocation is unchanged */

        L_Dnaa empties = L_Dnaa.CreateFull(3, 0);
        empties.Truncate();
        Assert.Equal(0, empties.Count);
    }

    [Fact]
    public void DnaaWriteMem_ExactText()
    {
        L_Dnaa daa = MakeDnaa();
        Assert.Equal(
            "\nL_Dnaa Version 1\n" +
            "Number of L_Dna = 3\n" +
            "\n" +
            "L_Dna[0]:\nL_Dna Version 1\nNumber of numbers = 2\n  [0] = 1.000000\n  [1] = 2.000000\n\n" +
            "L_Dna[1]:\nL_Dna Version 1\nNumber of numbers = 1\n  [0] = 3.250000\n\nstartx = 1.000000, delx = 2.000000\n" +
            "L_Dna[2]:\nL_Dna Version 1\nNumber of numbers = 0\n\n",
            Text(daa.WriteMem()));
    }

    [Fact]
    public void DnaaWriteReadMem_RoundTrip()
    {
        L_Dnaa daa = MakeDnaa();
        byte[] data = daa.WriteMem();
        L_Dnaa back = L_Dnaa.ReadMem(data);

        Assert.Equal(3, back.Count);
        Assert.Equal(Text(data), Text(back.WriteMem()));
        Assert.Equal(3.25, back.GetValue(1, 0));
        back.GetDna(1, L.L_CLONE).GetParameters(out double startx, out double delx);
        Assert.Equal((1.0, 2.0), (startx, delx));
        Assert.Equal(0, back.GetDnaCount(2));
    }

    [Fact]
    public void DnaaWriteRead_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dnaa_{Guid.NewGuid():N}.dnaa");
        try {
            L_Dnaa daa = MakeDnaa();
            daa.Write(path);
            Assert.Equal(Text(daa.WriteMem()), File.ReadAllText(path));
            Assert.Equal(3, L_Dnaa.Read(path).Count);
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => L_Dnaa.Read(path));
    }

    [Fact]
    public void DnaaReadMem_EmptyAndBadInput()
    {
        Assert.Equal(0, L_Dnaa.ReadMem(Bytes("\nL_Dnaa Version 1\nNumber of L_Dna = 0\n\n")).Count);

        Assert.Throws<ArgumentException>(() => L_Dnaa.ReadMem(Bytes("")));
        Assert.Throws<ArgumentException>(() => L_Dnaa.ReadMem(Bytes("\nL_Dnaa Version 2\nNumber of L_Dna = 0\n\n")));
        Assert.Throws<ArgumentException>(() => L_Dnaa.ReadMem(Bytes("\nL_Dnaa Version 1\nNumber of L_Dna = -1\n\n")));
        Assert.Throws<ArgumentException>(() => L_Dnaa.ReadMem(Bytes("\nL_Dnaa Version 1\nNumber of L_Dna = 1000001\n\n")));
        Assert.Throws<ArgumentException>(() => L_Dnaa.ReadMem(Bytes("\nL_Dnaa Version 1\nNumber of L_Dna = 1\n\nBogus[0]:")));
        Assert.Throws<ArgumentException>(() => L_Dnaa.ReadMem(Bytes("\nL_Dnaa Version 1\nNumber of L_Dna = 1\n\nL_Dna[0]:\nL_Dna Version 9\n")));
    }
}
