using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// hash_reg.c: tests hashing functions for strings, points and double arrays, for both
/// ordered sets (underlying rbtree implementation for sorting) and hashing
/// (underlying hashmap implementation).
/// </summary>
public class HashRegTests
{
    private const int string_set = 10967;
    private const int string_union = 18278;
    private const int string_intersection = 3656;
    private const int pta_set = 150000;
    private const int pta_union = 250000;
    private const int pta_intersection = 50001;
    private const int da_set = 48000;
    private const int da_union = 80000;
    private const int da_intersection = 16001;

    [RegFact]
    public void Hash()
    {
        int i, n, c1, c3, c4, c5, s1;
        L_Aset set;
        L_Dna da0, da1, da2, da3, da4, da5;
        L_Hashmap hmap;
        Pta pta0, pta1, pta2, pta3;
        Sarray sa0, sa1, sa2, sa3;
        Pix pix1;
        var rp = new RegTest("hash");

        Utils.LeptMkdir("lept/hash");

            /* Test string hashing with aset */
        sa0 = BuildShortStrings(3);
        n = sa0.Count;
        sa1 = sa0.SelectRange(0, (int)(0.6 * n));
        sa1.AppendRange(sa0, (int)(0.1 * n), (int)(0.2 * n));  /* add dups */
        sa2 = sa0.SelectRange((int)(0.4 * n), -1);   /* overlaps sa1 */
        sa2.AppendRange(sa0, (int)(0.7 * n), (int)(0.8 * n));  /* add dups */
        set = sa2.CreateAset();
        s1 = set.Size;
        rp.CompareValues(string_set, s1, 0);  /* 0 */
        sa3 = sa2.RemoveDupsByAset();
        c1 = sa3.Count;
        rp.CompareValues(string_set, c1, 0);  /* 1 */
        sa3 = sa1.IntersectionByAset(sa2);
        c1 = sa3.Count;
        rp.CompareValues(string_intersection, c1, 1);  /* 2 */
        sa3 = sa1.UnionByAset(sa2);
        c1 = sa3.Count;
        rp.CompareValues(string_union, c1, 0);  /* 3 */

            /* Test string hashing with hashmap */
        hmap = sa1.CreateHashMap();
        c1 = hmap.NItems;
        rp.CompareValues(string_set, c1, 0);  /* 4 */

        sa3 = sa2.RemoveDupsByHmap(out _);
        c1 = sa3.Count;
        rp.CompareValues(string_set, c1, 0);  /* 5 */
        sa3 = sa1.IntersectionByHmap(sa2);
        c1 = sa3.Count;
        rp.CompareValues(string_intersection, c1, 1);  /* 6 */
        sa3 = sa1.UnionByHmap(sa2);
        c1 = sa3.Count;
        rp.CompareValues(string_union, c1, 0);  /* 7 */

            /* Test point hashing with aset. */
        pta0 = BuildPointSet(500, 500);
        n = pta0.Count;
        pta1 = pta0.SelectRange(0, (int)(0.6 * n));
        pta1.Join(pta0, (int)(0.1 * n), (int)(0.2 * n));  /* add dups */
        pta2 = pta0.SelectRange((int)(0.4 * n), -1);  /* overlap with pta1 */
        pta2.Join(pta0, (int)(0.7 * n), (int)(0.8 * n));  /* add dups */
        set = L_Aset.CreateFromPta(pta2);
        s1 = set.Size;
        rp.CompareValues(pta_set, s1, 0);  /* 8 */
        pta3 = pta2.RemoveDupsByAset();
        c1 = pta3.Count;
        rp.CompareValues(pta_set, c1, 0);  /* 9 */
        pta3 = pta1.IntersectionByAset(pta2);
        c1 = pta3.Count;
        rp.CompareValues(pta_intersection, c1, 1);  /* 10 */
        pta3 = pta1.UnionByAset(pta2);
        c1 = pta3.Count;
        rp.CompareValues(pta_union, c1, 0);  /* 11 */

            /* Test point hashing with hashmap */
        hmap = L_Hashmap.CreateFromPta(pta2);
        c1 = hmap.NItems;
        rp.CompareValues(pta_set, c1, 0);  /* 12 */
        pta2.RemoveDupsByHmap(out pta3, out _);
        c1 = pta3.Count;
        rp.CompareValues(pta_set, c1, 0);  /* 13 */
        pta3 = pta1.IntersectionByHmap(pta2);
        c1 = pta3.Count;
        rp.CompareValues(pta_intersection, c1, 1);  /* 14 */
        pta3 = pta1.UnionByHmap(pta2);
        c1 = pta3.Count;
        rp.CompareValues(pta_union, c1, 0);  /* 15 */

            /* Test dna hashing with aset */
        da0 = L_Dna.MakeSequence(0, 1, 80000);
        n = da0.Count;
        for (i = 0; i < n; i++)
            da0.SetValue(i, -7.4 * da0.GetDValue(i));
        da1 = da0.SelectRange(0, (int)(0.6 * n));
        da1.Join(da0, (int)(0.1 * n), (int)(0.2 * n));  /* add dups */
        da2 = da0.SelectRange((int)(0.4 * n), -1);  /* overlap with da1 */
        da2.Join(da0, (int)(0.7 * n), (int)(0.8 * n));  /* add dups */
        set = L_Aset.CreateFromDna(da2);
        s1 = set.Size;
        rp.CompareValues(da_set, s1, 0);  /* 16 */
        da3 = da2.RemoveDupsByAset();
        c1 = da3.Count;
        rp.CompareValues(da_set, c1, 0);  /* 17 */
        da3 = da1.IntersectionByAset(da2);
        c1 = da3.Count;
        rp.CompareValues(da_intersection, c1, 1);  /* 18 */
        da3 = da1.UnionByAset(da2);
        c1 = da3.Count;
        rp.CompareValues(da_union, c1, 0);  /* 19 */

            /* Test dna hashing with hashmap */
        hmap = L_Hashmap.CreateFromDna(da2);
        c1 = hmap.NItems;
        rp.CompareValues(da_set, c1, 0);  /* 20 */
        da3 = da2.RemoveDupsByHmap(out _);
        c1 = da3.Count;
        rp.CompareValues(da_set, c1, 0);  /* 21 */
        da3 = da1.IntersectionByHmap(da2);
        c1 = da3.Count;
        rp.CompareValues(da_intersection, c1, 1);  /* 22 */
        da3 = da1.UnionByHmap(da2);
        c1 = da3.Count;
        rp.CompareValues(da_union, c1, 0);  /* 23 */

            /* Another test of dna hashing, showing equivalence
             * of results between ordered and unordered sets. */
        da0 = L_Dna.MakeSequence(0, 1, 20);
        n = da0.Count;
        for (i = 0; i < n; i++)
            da0.SetValue(i, 3.4 * da0.GetDValue(i));
        da1 = da0.SelectRange(0, (int)(0.6 * n));
        da1.Join(da0, (int)(0.1 * n), (int)(0.2 * n));  /* add dups */
        da2 = da0.SelectRange((int)(0.4 * n), -1);  /* overlap with da1 */
        da2.Join(da0, (int)(0.7 * n), (int)(0.8 * n));  /* add dups */
        da3 = da2.RemoveDupsByAset();
        da4 = da2.RemoveDupsByHmap(out _);
            /* Show the two sets da3 and da4 are identical in content,
             * because they have the same size and their intersection also
             * has the same size. */
        c3 = da3.Count;
        c4 = da4.Count;
        rp.CompareValues(c3, c4, 0);  /* 24 */
        da5 = da3.IntersectionByHmap(da4);
        c5 = da5.Count;
        rp.CompareValues(c4, c5, 0);  /* 25 */

            /* Test pixel counting operations with hashmap and ordered map */
        pix1 = TestPaths.ReadInput("wet-day.jpg");
        c1 = pix1.CountRGBColorsByHash();
        int c2 = pix1.CountRGBColors(1);
        rp.CompareValues(42427, c1, 0);  /* 26 */
        rp.CompareValues(42427, c2, 0);  /* 27 */

        Assert.Equal(27, rp.Index);
        rp.Cleanup();
    }

    private static Sarray BuildShortStrings(int nchars)  /* 3, 4 or 5 */
    {
        int i, j, k, l, m;
        Sarray sa;

        sa = Sarray.Create(1000);
        for (i = 0; i < 26; i++) {
            sa.AddString(Str(i), L.L_COPY);
            for (j = 0; j < 26; j++) {
                sa.AddString(Str(i, j), L.L_COPY);
                for (k = 0; k < 26; k++) {
                    sa.AddString(Str(i, j, k), L.L_COPY);
                    if (nchars > 3) {
                        for (l = 0; l < 26; l++) {
                            sa.AddString(Str(i, j, k, l), L.L_COPY);
                            if (nchars > 4) {
                                for (m = 0; m < 26; m++)
                                    sa.AddString(Str(i, j, k, l, m), L.L_COPY);
                            }
                        }
                    }
                }
            }
        }

        return sa;
    }

    private static string Str(params int[] letters)
    {
        var chars = new char[letters.Length];
        for (int i = 0; i < letters.Length; i++)
            chars[i] = (char)(letters[i] + 0x61);
        return new string(chars);
    }

    private static Pta BuildPointSet(int w, int h)
    {
        int i, j;
        Pta pta;

        pta = Pta.Create(w * h);
        for (i = 0; i < h; i++) {
            for (j = 0; j < w; j++)
                pta.AddPt((float)(316.27 * j), (float)(243.59 * i));
        }
        return pta;
    }
}
