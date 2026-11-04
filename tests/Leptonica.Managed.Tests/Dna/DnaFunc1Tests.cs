using System;
using System.Linq;
using Leptonica;

namespace Leptonica.Managed.Tests.DnaTests;

public class DnaFunc1Tests
{
    private static L_Dna Make(params double[] vals)
    {
        L_Dna da = L_Dna.Create(0);
        foreach (double v in vals)
            da.AddNumber(v);
        return da;
    }

    private static double[] Values(L_Dna da)
    {
        double[] v = new double[da.Count];
        for (int i = 0; i < v.Length; i++)
            v[i] = da.GetDValue(i);
        return v;
    }

    private static double[] Sorted(L_Dna da) => Values(da).OrderBy(x => x).ToArray();

    [Fact]
    public void Join_AppendsRanges()
    {
        L_Dna a = Make(1, 2);
        a.Join(Make(3, 4, 5, 6), 1, 2);
        Assert.Equal(new double[] { 1, 2, 4, 5 }, Values(a));
        a.Join(Make(9, 8), -3, -1);
        Assert.Equal(new double[] { 1, 2, 4, 5, 9, 8 }, Values(a));
        a.Join(null, 0, -1);
        Assert.Equal(6, a.Count);
        Assert.Throws<ArgumentException>(() => a.Join(Make(1, 2, 3), 2, 1));
    }

    [Fact]
    public void FlattenToDna_JoinsAll()
    {
        L_Dnaa daa = L_Dnaa.Create(0);
        daa.AddDna(Make(1, 2), L.L_COPY);
        daa.AddDna(Make(3), L.L_COPY);
        daa.AddDna(Make(4, 5, 6), L.L_COPY);
        Assert.Equal(new double[] { 1, 2, 3, 4, 5, 6 }, Values(daa.FlattenToDna()));
        Assert.Equal(3, daa.Count);
    }

    [Fact]
    public void SelectRange_ClipsAndValidates()
    {
        L_Dna a = Make(10, 20, 30, 40);
        Assert.Equal(new double[] { 20, 30 }, Values(a.SelectRange(1, 2)));
        Assert.Equal(new double[] { 10, 20, 30, 40 }, Values(a.SelectRange(-5, -1)));
        Assert.Equal(new double[] { 30, 40 }, Values(a.SelectRange(2, 99)));
        Assert.Throws<ArgumentException>(() => a.SelectRange(4, 5));
        Assert.Throws<ArgumentException>(() => a.SelectRange(3, 1));
        Assert.Empty(Values(L_Dna.Create(0).SelectRange(0, -1)));
    }

    [Fact]
    public void ConvertToNuma_And_Back()
    {
        L_Dna a = Make(1.5, -2, 3.25);
        Numa na = a.ConvertToNuma();
        Assert.Equal(3, na.Count);
        Assert.Equal(1.5f, na.GetFValue(0));
        Assert.Equal(3.25f, na.GetFValue(2));
        L_Dna b = na.ConvertToDna();
        Assert.Equal(new double[] { 1.5, -2, 3.25 }, Values(b));
    }

    [Fact]
    public void ConvertDataToDna_RowMajorPixelValues()
    {
        Pix pix = Pix.Create(2, 2, 32);
        pix.SetPixel(0, 0, 0xff000000u);
        pix.SetPixel(1, 0, 5);
        pix.SetPixel(0, 1, 6);
        pix.SetPixel(1, 1, 0xffffffffu);
        Assert.Equal(new double[] { 4278190080.0, 5, 6, 4294967295.0 }, Values(pix.ConvertDataToDna()));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).ConvertDataToDna());
    }

    [Fact]
    public void RemoveDupsByAset_KeepsFirstOccurrenceOrder()
    {
        L_Dna a = Make(3, 1, 3, 2, 1, 5.5, 5.5);
        Assert.Equal(new double[] { 3, 1, 2, 5.5 }, Values(a.RemoveDupsByAset()));
    }

    [Fact]
    public void UnionAndIntersectionByAset()
    {
        L_Dna a = Make(1, 2, 3, 3, 4);
        L_Dna b = Make(3, 4, 5, 6, 3);
        Assert.Equal(new double[] { 1, 2, 3, 4, 5, 6 }, Values(a.UnionByAset(b)));
        Assert.Equal(new double[] { 3, 4 }, Sorted(a.IntersectionByAset(b)));
        // order follows the smaller array
        L_Dna c = Make(4, 3);
        Assert.Equal(new double[] { 4, 3 }, Values(c.IntersectionByAset(a)));
        Assert.Empty(Values(Make(1).IntersectionByAset(Make(2))));
    }

    [Fact]
    public void AsetCreateFromDna_ContainsValues()
    {
        L_Aset set = L_Aset.CreateFromDna(Make(2.5, -1, 2.5));
        Assert.Equal(2, set.Size);
        RbType key = default;
        key.Ftype = -1;
        Assert.NotNull(set.Find(key));
        key.Ftype = 7;
        Assert.Null(set.Find(key));
    }

    [Fact]
    public void RemoveDupsByHmap_UniqueValues()
    {
        L_Dna a = Make(3, 1, 3, 2, 1, 5.5, 5.5, 0, -0.0);
        L_Dna d = a.RemoveDupsByHmap(out L_Hashmap hmap);
        // 0 and -0.0 hash to the same key
        Assert.Equal(new double[] { 0, 1, 2, 3, 5.5 }, Sorted(d));
        Assert.Equal(d.Count, hmap.NItems);
    }

    [Fact]
    public void UnionAndIntersectionByHmap()
    {
        L_Dna a = Make(1, 2, 3, 3, 4);
        L_Dna b = Make(3, 4, 5, 6, 3);
        Assert.Equal(new double[] { 1, 2, 3, 4, 5, 6 }, Sorted(a.UnionByHmap(b)));
        Assert.Equal(new double[] { 3, 4 }, Sorted(a.IntersectionByHmap(b)));
        Assert.Empty(Values(Make(1).IntersectionByHmap(Make(2))));
    }

    [Fact]
    public void MakeHistoByHmap_CountsValues()
    {
        L_Dna a = Make(7, 8, 7, 7, 9, 8);
        a.MakeHistoByHmap(out L_Dna dav, out L_Dna dac);
        Assert.Equal(3, dav.Count);
        for (int i = 0; i < dav.Count; i++) {
            double v = dav.GetDValue(i);
            double c = dac.GetDValue(i);
            Assert.Equal(v == 7 ? 3 : v == 8 ? 2 : 1, c);
        }
    }

    [Fact]
    public void HmapCreateFromDna_StoresIndices()
    {
        L_Dna a = Make(10, 20, 10);
        L_Hashmap hmap = L_Hashmap.CreateFromDna(a);
        Assert.Equal(2, hmap.NItems);
        L_Hashitem item = hmap.Lookup(Utils.HashFloat64ToUint64(10), 0, L.L_HMAP_CHECK)!;
        Assert.Equal(0UL, item.Val);   // first index seen
        Assert.Equal(2, item.Count);
    }

    [Fact]
    public void DiffAdjValues_TakesIntegerDifferences()
    {
        L_Dna a = Make(1, 4, 9, 16);
        // prev starts at 0 rather than at the first value, as in C
        Assert.Equal(new double[] { 4, 5, 7 }, Values(a.DiffAdjValues()));
    }
}
