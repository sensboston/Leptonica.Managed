using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>heap_reg.c: tests the heap utility.</summary>
public class HeapRegTests
{
    private const int NELEM = 50;

    private sealed class HeapElement : IHeapItem
    {
        public float distance;
        public float HeapKey => distance;
    }

    [RegFact]
    public void Heap()
    {
        byte[] data;
        int i;
        float frand, fval;
        HeapElement item;
        Numa na1, na2, na3, na4, na5;
        L_Heap lh;
        var rp = new RegTest("heap");

        Utils.LeptMkdir("lept/heap");

            /* A fresh C process starts with srand(1) */
        CRand.Seed(1);

            /* Make a numa of random numbers */
        na1 = Numa.Create(5);
        for (i = 0; i < NELEM; i++) {
            frand = (float)CRand.Next() / (float)CRand.RandMax;
            na1.AddNumber(frand);
        }
        data = na1.WriteMem();
        rp.WriteDataAndCheck(data, "na");  /* 0 */

            /* Make an array of HEAPELs with the same numbers */
        lh = L_Heap.Create(5, L.L_SORT_INCREASING);
        for (i = 0; i < NELEM; i++) {
            fval = na1.GetFValue(i);
            item = new HeapElement();
            item.distance = fval;
            lh.Add(item);
        }

            /* Re-sort for strict order */
        lh.SortStrictOrder();
        na2 = ExtractNumaFromHeap(lh);
        data = na2.WriteMem();
        rp.WriteDataAndCheck(data, "na");  /* 1 */

            /* Switch the direction and re-sort strict order */
        lh.Direction = L.L_SORT_DECREASING;
        lh.SortStrictOrder();
        na3 = ExtractNumaFromHeap(lh);
        data = na3.WriteMem();
        rp.WriteDataAndCheck(data, "na");  /* 2 */

            /* Switch direction again and re-sort strict sort */
        lh.Direction = L.L_SORT_INCREASING;
        lh.SortStrictOrder();
        na4 = ExtractNumaFromHeap(lh);
        data = na4.WriteMem();
        rp.WriteDataAndCheck(data, "na");  /* 3 */

            /* Switch direction again and re-sort strict sort */
        lh.Direction = L.L_SORT_DECREASING;
        lh.SortStrictOrder();
        na5 = ExtractNumaFromHeap(lh);
        data = na5.WriteMem();
        rp.WriteDataAndCheck(data, "na");  /* 4 */

        rp.CompareFiles(1, 3);  /* 5 */
        rp.CompareFiles(2, 4);  /* 6 */

            /* Remove the elements, one at a time */
        for (i = 0; lh.Count > 0; i++)
            lh.Remove();

        Assert.Equal(6, rp.Index);
        rp.Cleanup();
    }

        /* This just uses the heap array.  It will only be
           ordered if the heap is in strict ordering.  */
    private static Numa ExtractNumaFromHeap(L_Heap lh)
    {
        int i, n;
        HeapElement item;
        Numa na;

        n = lh.Count;
        na = Numa.Create(0);
        for (i = 0; i < n; i++) {
            item = (HeapElement)lh.GetElement(i);
            na.AddNumber(item.distance);
        }
        return na;
    }
}
