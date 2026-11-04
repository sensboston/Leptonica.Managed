using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class NumaBasicTests
{
    private static string Text(byte[] data) => Encoding.ASCII.GetString(data);

    private static Numa MakeNuma(params float[] values)
    {
        Numa na = Numa.Create(0);
        foreach (float v in values)
            na.AddNumber(v);
        return na;
    }

    private static float[] Values(Numa na)
    {
        float[] values = new float[na.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = na.GetFValue(i);
        return values;
    }

    /*------------------------------ creation ------------------------------*/

    [Fact]
    public void Create_DefaultAndRequestedSize()
    {
        Assert.Equal(50, Numa.Create(0).Nalloc);
        Assert.Equal(50, Numa.Create(-3).Nalloc);
        Assert.Equal(50, Numa.Create(100000001).Nalloc);
        Numa na = Numa.Create(7);
        Assert.Equal(7, na.Nalloc);
        Assert.Equal(0, na.Count);
        na.GetParameters(out float startx, out float delx);
        Assert.Equal((0.0f, 1.0f), (startx, delx));
    }

    [Fact]
    public void AddNumber_ExtendsArrayByDoubling()
    {
        Numa na = Numa.Create(2);
        for (int i = 0; i < 5; i++)
            na.AddNumber(i * 1.5f);
        Assert.Equal(5, na.Count);
        Assert.Equal(8, na.Nalloc);
        Assert.Equal(new[] { 0f, 1.5f, 3f, 4.5f, 6f }, Values(na));
    }

    [Fact]
    public void CreateFromIArray()
    {
        Numa na = Numa.CreateFromIArray(new[] { 3, -4, 5 }, 3);
        Assert.Equal(new[] { 3f, -4f, 5f }, Values(na));
        Assert.Equal(3, na.Nalloc);
        Assert.Throws<ArgumentException>(() => Numa.CreateFromIArray(new[] { 1 }, 0));
    }

    [Fact]
    public void CreateFromFArray_CopyAndInsert()
    {
        float[] src = { 1.25f, 2.5f, 3.75f };
        Numa copy = Numa.CreateFromFArray(src, 3, L.L_COPY);
        src[0] = 9f;
        Assert.Equal(1.25f, copy.GetFValue(0));

        Numa inserted = Numa.CreateFromFArray(src, 3, L.L_INSERT);
        src[1] = 8f;
        Assert.Equal(new[] { 9f, 8f, 3.75f }, Values(inserted));
        Assert.Same(src, inserted.GetFArray(L.L_NOCOPY));

        Assert.Throws<ArgumentException>(() => Numa.CreateFromFArray(src, 0, L.L_COPY));
        Assert.Throws<ArgumentException>(() => Numa.CreateFromFArray(src, 3, L.L_CLONE));
    }

    [Fact]
    public void CreateFromString()
    {
        Numa na = Numa.CreateFromString("1, 2.5,-3e1,4");
        Assert.Equal(new[] { 1f, 2.5f, -30f, 4f }, Values(na));
        Assert.Throws<ArgumentException>(() => Numa.CreateFromString(""));
        Assert.Throws<ArgumentException>(() => Numa.CreateFromString("1,abc,3"));
    }

    /*------------------------------- accessors ------------------------------*/

    [Fact]
    public void GetIValue_RoundsHalfAwayFromZero()
    {
        Numa na = MakeNuma(1.4f, 1.5f, -1.4f, -1.5f, 0f, 2.49f);
        int[] ivals = new int[na.Count];
        for (int i = 0; i < ivals.Length; i++)
            ivals[i] = na.GetIValue(i);
        Assert.Equal(new[] { 1, 2, -1, -2, 0, 2 }, ivals);
        Assert.Equal(ivals, na.GetIArray());
    }

    [Fact]
    public void Accessors_ThrowOnBadIndex()
    {
        Numa na = MakeNuma(1f, 2f);
        Assert.Throws<ArgumentException>(() => na.GetFValue(-1));
        Assert.Throws<ArgumentException>(() => na.GetFValue(2));
        Assert.Throws<ArgumentException>(() => na.GetIValue(2));
        Assert.Throws<ArgumentException>(() => na.SetValue(2, 1f));
        Assert.Throws<ArgumentException>(() => na.ShiftValue(-1, 1f));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetIArray());
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetFArray(L.L_COPY));
    }

    [Fact]
    public void SetValue_ShiftValue()
    {
        Numa na = MakeNuma(1f, 2f, 3f);
        na.SetValue(1, 10.5f);
        na.ShiftValue(2, -0.25f);
        Assert.Equal(new[] { 1f, 10.5f, 2.75f }, Values(na));
    }

    [Fact]
    public void SetCount_GrowsAndZeroFills()
    {
        Numa na = Numa.Create(4);
        na.AddNumber(1f);
        na.Count = 10;
        Assert.Equal(10, na.Count);
        Assert.Equal(10, na.Nalloc);
        Assert.Equal(new[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }, Values(na));
        na.Count = 2;
        Assert.Equal(2, na.Count);
        Assert.Equal(10, na.Nalloc);
        na.Count = 0;
        Assert.Equal(0, na.Count);
    }

    [Fact]
    public void GetFArray_CopyAndNoCopy()
    {
        Numa na = MakeNuma(1f, 2f, 3f);
        float[] copy = na.GetFArray(L.L_COPY);
        Assert.Equal(new[] { 1f, 2f, 3f }, copy);
        copy[0] = 7f;
        Assert.Equal(1f, na.GetFValue(0));

        float[] bare = na.GetFArray(L.L_NOCOPY);
        bare[0] = 5f;
        Assert.Equal(5f, na.GetFValue(0));
        Assert.True(bare.Length >= na.Nalloc);
    }

    [Fact]
    public void Parameters()
    {
        Numa nas = MakeNuma(1f);
        nas.SetParameters(2.5f, 0.125f);
        Numa nad = MakeNuma(1f);
        nad.CopyParameters(nas);
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal((2.5f, 0.125f), (startx, delx));
    }

    [Fact]
    public void ConvertToSarray()
    {
        Numa na = MakeNuma(3f, 12.6f, -4.4f);
        Sarray sa = na.ConvertToSarray(4, 0, 0, L.L_INTEGER_VALUE);
        Assert.Equal(new[] { "   3", "  13", "  -4" }, StringsOf(sa));
        sa = na.ConvertToSarray(4, 0, 1, L.L_INTEGER_VALUE);
        Assert.Equal(new[] { "0003", "0013", "-004" }, StringsOf(sa));
        sa = na.ConvertToSarray(8, 2, 0, L.L_FLOAT_VALUE);
        Assert.Equal(new[] { "    3.00", "   12.60", "   -4.40" }, StringsOf(sa));
        Assert.Throws<ArgumentException>(() => na.ConvertToSarray(4, 0, 0, 7));
    }

    private static string[] StringsOf(Sarray sa)
    {
        string[] strs = new string[sa.Count];
        for (int i = 0; i < strs.Length; i++)
            strs[i] = sa.GetString(i, L.L_NOCOPY);
        return strs;
    }

    /*---------------------------- copy and clone ----------------------------*/

    [Fact]
    public void CopyAndClone()
    {
        Numa na = Numa.Create(9);
        na.AddNumber(1f);
        na.AddNumber(2f);
        na.SetParameters(3f, 0.5f);

        Numa copy = na.Copy();
        Numa clone = na.Clone();
        Assert.NotSame(na, copy);
        Assert.Same(na, clone);
        Assert.Equal(9, copy.Nalloc);
        copy.GetParameters(out float startx, out float delx);
        Assert.Equal((3f, 0.5f), (startx, delx));

        na.SetValue(0, 100f);
        Assert.Equal(100f, clone.GetFValue(0));
        Assert.Equal(1f, copy.GetFValue(0));
    }

    [Fact]
    public void Empty_KeepsAllocation()
    {
        Numa na = Numa.Create(9);
        na.AddNumber(1f);
        na.Empty();
        Assert.Equal(0, na.Count);
        Assert.Equal(9, na.Nalloc);
    }

    /*-------------------------- insert / remove -----------------------------*/

    [Fact]
    public void InsertNumber()
    {
        Numa na = Numa.Create(2);
        na.AddNumber(1f);
        na.AddNumber(3f);
        na.InsertNumber(1, 2f);
        na.InsertNumber(0, 0f);
        na.InsertNumber(4, 4f);
        Assert.Equal(new[] { 0f, 1f, 2f, 3f, 4f }, Values(na));
        Assert.Equal(8, na.Nalloc);
        Assert.Throws<ArgumentException>(() => na.InsertNumber(-1, 1f));
        Assert.Throws<ArgumentException>(() => na.InsertNumber(6, 1f));
    }

    [Fact]
    public void RemoveNumber()
    {
        Numa na = MakeNuma(0f, 1f, 2f, 3f);
        na.RemoveNumber(1);
        Assert.Equal(new[] { 0f, 2f, 3f }, Values(na));
        na.RemoveNumber(2);
        Assert.Equal(new[] { 0f, 2f }, Values(na));
        Assert.Throws<ArgumentException>(() => na.RemoveNumber(2));
        Assert.Throws<ArgumentException>(() => na.RemoveNumber(-1));
    }

    [Fact]
    public void ReplaceNumber()
    {
        Numa na = MakeNuma(0f, 1f);
        na.ReplaceNumber(1, 7f);
        Assert.Equal(new[] { 0f, 7f }, Values(na));
        Assert.Throws<ArgumentException>(() => na.ReplaceNumber(2, 1f));
    }

    /*------------------------------ serialization ---------------------------*/

    [Fact]
    public void WriteMem_ExactTextFormat()
    {
        Numa na = MakeNuma(1f, -2.5f, 0.1f, 1e10f);
        Assert.Equal(
            "\nNuma Version 1\n" +
            "Number of numbers = 4\n" +
            "  [0] = 1.000000\n" +
            "  [1] = -2.500000\n" +
            "  [2] = 0.100000\n" +
            "  [3] = 10000000000.000000\n" +
            "\n",
            Text(na.WriteMem()));
    }

    [Fact]
    public void WriteMem_WithParameters()
    {
        Numa na = MakeNuma(5f);
        na.SetParameters(0.5f, 2f);
        Assert.Equal(
            "\nNuma Version 1\nNumber of numbers = 1\n  [0] = 5.000000\n\nstartx = 0.500000, delx = 2.000000\n",
            Text(na.WriteMem()));

        na.SetParameters(0f, 1f);
        Assert.DoesNotContain("startx", Text(na.WriteMem()));
        na.SetParameters(0f, 1.5f);
        Assert.Contains("startx = 0.000000, delx = 1.500000\n", Text(na.WriteMem()));
    }

    [Fact]
    public void WriteMem_Empty()
    {
        Assert.Equal("\nNuma Version 1\nNumber of numbers = 0\n\n", Text(Numa.Create(0).WriteMem()));
    }

    [Fact]
    public void RoundTrip_Mem()
    {
        Numa na = MakeNuma(1f, -2.5f, 0.1f, 123456.789f, 1e-7f);
        na.SetParameters(-1.5f, 0.25f);
        Numa nb = Numa.ReadMem(na.WriteMem());
        Assert.Equal(5, nb.Count);
        Assert.Equal(na.WriteMem(), nb.WriteMem());
        nb.GetParameters(out float startx, out float delx);
        Assert.Equal((-1.5f, 0.25f), (startx, delx));
        /* values are written with %f (6 decimals) */
        Assert.Equal(1f, nb.GetFValue(0));
        Assert.Equal(-2.5f, nb.GetFValue(1));
        Assert.Equal(0.1f, nb.GetFValue(2));
        Assert.Equal(0f, nb.GetFValue(4));
    }

    [Fact]
    public void RoundTrip_Empty()
    {
        Numa nb = Numa.ReadMem(Numa.Create(0).WriteMem());
        Assert.Equal(0, nb.Count);
    }

    [Fact]
    public void RoundTrip_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"numa_{Guid.NewGuid():N}.na");
        try {
            Numa na = MakeNuma(4f, 5.5f);
            na.Write(path);
            Assert.Equal(Text(na.WriteMem()), File.ReadAllText(path));
            Numa nb = Numa.Read(path);
            Assert.Equal(new[] { 4f, 5.5f }, Values(nb));
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => Numa.Read(path));
    }

    [Fact]
    public void ReadStream_LeavesStreamAfterData()
    {
        byte[] data = Encoding.ASCII.GetBytes(Text(MakeNuma(1f, 2f).WriteMem()) + "tail");
        using MemoryStream ms = new MemoryStream(data);
        Numa na = Numa.ReadStream(ms);
        Assert.Equal(2, na.Count);
        Assert.Equal("tail", Encoding.ASCII.GetString(data, (int)ms.Position, data.Length - (int)ms.Position));
    }

    [Fact]
    public void WriteStream_ToStream()
    {
        using MemoryStream ms = new MemoryStream();
        MakeNuma(2f).WriteStream(ms);
        Assert.Equal("\nNuma Version 1\nNumber of numbers = 1\n  [0] = 2.000000\n\n", Text(ms.ToArray()));
    }

    [Fact]
    public void Read_Errors()
    {
        Assert.Throws<ArgumentException>(() => Numa.ReadMem(Encoding.ASCII.GetBytes("junk")));
        Assert.Throws<ArgumentException>(() => Numa.ReadMem(Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() =>
            Numa.ReadMem(Encoding.ASCII.GetBytes("\nNuma Version 2\nNumber of numbers = 0\n\n")));
        Assert.Throws<ArgumentException>(() =>
            Numa.ReadMem(Encoding.ASCII.GetBytes("\nNuma Version 1\nNumber of numbers = -1\n\n")));
        Assert.Throws<ArgumentException>(() =>
            Numa.ReadMem(Encoding.ASCII.GetBytes("\nNuma Version 1\nNumber of numbers = 200000000\n\n")));
        Assert.Throws<ArgumentException>(() =>
            Numa.ReadMem(Encoding.ASCII.GetBytes("\nNuma Version 1\nNumber of numbers = 2\n  [0] = 1.0\n\n")));
    }

    /*--------------------------------- Numaa --------------------------------*/

    private static Numaa MakeNumaa()
    {
        Numaa naa = Numaa.Create(0);
        naa.AddNuma(MakeNuma(1f, 2f), L.L_INSERT);
        Numa second = MakeNuma(3.5f);
        second.SetParameters(1f, 0.5f);
        naa.AddNuma(second, L.L_INSERT);
        naa.AddNuma(Numa.Create(0), L.L_INSERT);
        return naa;
    }

    [Fact]
    public void Numaa_CreateAndAdd()
    {
        Assert.Equal(50, Numaa.Create(0).Nalloc);
        Assert.Equal(50, Numaa.Create(1000001).Nalloc);

        Numaa naa = Numaa.Create(1);
        Numa na = MakeNuma(1f);
        naa.AddNuma(na, L.L_INSERT);
        naa.AddNuma(na, L.L_CLONE);
        naa.AddNuma(na, L.L_COPY);
        Assert.Equal(3, naa.Count);
        Assert.Equal(4, naa.Nalloc);
        Assert.Same(na, naa.GetNuma(0, L.L_CLONE));
        Assert.Same(na, naa.GetNuma(1, L.L_CLONE));
        Assert.NotSame(na, naa.GetNuma(2, L.L_CLONE));
        Assert.NotSame(na, naa.GetNuma(0, L.L_COPY));
        Assert.Throws<ArgumentException>(() => naa.AddNuma(na, 7));
        Assert.Throws<ArgumentException>(() => naa.GetNuma(3, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => naa.GetNuma(0, 7));
    }

    [Fact]
    public void Numaa_CreateFull_AddNumber_Counts()
    {
        Numaa naa = Numaa.CreateFull(3, 5);
        Assert.Equal(3, naa.Count);
        naa.AddNumber(0, 1f);
        naa.AddNumber(0, 2f);
        naa.AddNumber(2, 9f);
        Assert.Equal(2, naa.GetNumaCount(0));
        Assert.Equal(0, naa.GetNumaCount(1));
        Assert.Equal(1, naa.GetNumaCount(2));
        Assert.Equal(3, naa.GetNumberCount());
        Assert.Throws<ArgumentException>(() => naa.AddNumber(3, 1f));
        Assert.Throws<ArgumentException>(() => naa.GetNumaCount(-1));
    }

    [Fact]
    public void Numaa_GetValue()
    {
        Numaa naa = MakeNumaa();
        naa.GetValue(1, 0, out float fval, out int ival);
        Assert.Equal((3.5f, 3), (fval, ival));
        naa.AddNumber(1, -2.75f);
        naa.GetValue(1, 1, out fval, out ival);
        Assert.Equal((-2.75f, -2), (fval, ival));   /* truncated, not rounded */
        Assert.Throws<ArgumentException>(() => naa.GetValue(3, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => naa.GetValue(0, 2, out _, out _));
    }

    [Fact]
    public void Numaa_ReplaceNuma()
    {
        Numaa naa = MakeNumaa();
        Numa na = MakeNuma(8f);
        naa.ReplaceNuma(0, na);
        Assert.Same(na, naa.GetNuma(0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => naa.ReplaceNuma(3, na));
        Assert.Throws<ArgumentException>(() => naa.ReplaceNuma(-1, na));
    }

    [Fact]
    public void Numaa_Truncate()
    {
        Numaa naa = Numaa.CreateFull(5, 0);
        naa.AddNumber(1, 1f);
        naa.Truncate();
        Assert.Equal(2, naa.Count);
        Assert.Equal(5, naa.Nalloc);

        Numaa empty = Numaa.CreateFull(3, 0);
        empty.Truncate();
        Assert.Equal(0, empty.Count);
    }

    [Fact]
    public void Numaa_GetPtrArray_SetsCountToAllocation()
    {
        Numaa naa = Numaa.Create(4);
        Numa?[] array = naa.GetPtrArray();
        Assert.Equal(4, array.Length);
        Assert.Equal(4, naa.Count);
        array[0] = MakeNuma(1f);
        Assert.Equal(1, naa.GetNumaCount(0));
    }

    [Fact]
    public void Numaa_WriteMem_ExactTextFormat()
    {
        Assert.Equal(
            "\nNumaa Version 1\n" +
            "Number of numa = 3\n" +
            "\n" +
            "Numa[0]:\nNuma Version 1\nNumber of numbers = 2\n  [0] = 1.000000\n  [1] = 2.000000\n\n" +
            "Numa[1]:\nNuma Version 1\nNumber of numbers = 1\n  [0] = 3.500000\n\nstartx = 1.000000, delx = 0.500000\n" +
            "Numa[2]:\nNuma Version 1\nNumber of numbers = 0\n\n",
            Text(MakeNumaa().WriteMem()));
    }

    [Fact]
    public void Numaa_RoundTrip_Mem()
    {
        Numaa naa = MakeNumaa();
        Numaa nbb = Numaa.ReadMem(naa.WriteMem());
        Assert.Equal(3, nbb.Count);
        Assert.Equal(naa.WriteMem(), nbb.WriteMem());
        nbb.GetNuma(1, L.L_CLONE).GetParameters(out float startx, out float delx);
        Assert.Equal((1f, 0.5f), (startx, delx));
        Assert.Equal(0, nbb.GetNumaCount(2));
    }

    [Fact]
    public void Numaa_RoundTrip_FileAndEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"numaa_{Guid.NewGuid():N}.naa");
        try {
            Numaa naa = MakeNumaa();
            naa.Write(path);
            Numaa nbb = Numaa.Read(path);
            Assert.Equal(naa.WriteMem(), nbb.WriteMem());
        } finally {
            File.Delete(path);
        }
        Assert.Equal(0, Numaa.ReadMem(Numaa.Create(0).WriteMem()).Count);
    }

    [Fact]
    public void Numaa_Read_Errors()
    {
        Assert.Throws<ArgumentException>(() => Numaa.ReadMem(Encoding.ASCII.GetBytes("junk")));
        Assert.Throws<ArgumentException>(() =>
            Numaa.ReadMem(Encoding.ASCII.GetBytes("\nNumaa Version 2\nNumber of numa = 0\n\n")));
        Assert.Throws<ArgumentException>(() =>
            Numaa.ReadMem(Encoding.ASCII.GetBytes("\nNumaa Version 1\nNumber of numa = -2\n\n")));
        Assert.Throws<ArgumentException>(() =>
            Numaa.ReadMem(Encoding.ASCII.GetBytes("\nNumaa Version 1\nNumber of numa = 1\n\nbad")));
        Assert.Throws<ArgumentException>(() =>
            Numaa.ReadMem(Encoding.ASCII.GetBytes("\nNumaa Version 1\nNumber of numa = 1\n\nNuma[0]:junk")));
    }
}
