using System;
using Leptonica;
using Xunit;
using static Leptonica.ArrayAccess;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// lowaccess_reg.c: test low-level pixel accessors for 1, 2, 4, 8, 16 and 32 bpp,
/// including masking of irrelevant high-order bits.
/// </summary>
public class LowAccessRegTests
{
    [RegFact]
    public void LowAccess()
    {
        var rp = new RegTest("lowaccess");

        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        pixs.GetDimensions(out int w, out int h, out _);
        uint[] data1 = pixs.Data;
        int wpl = pixs.Wpl;
        int[] lines1 = pixs.GetLinePtrs();

        Pix pix1 = Pix.CreateTemplate(pixs);
        uint[] datat1 = pix1.Data;
        int[] linet1 = pix1.GetLinePtrs();
        Pix pix2 = Pix.CreateTemplate(pixs);
        uint[] data2 = pix2.Data;

            /* Test different methods for 1 bpp */
        int count1 = 0;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                uint val1 = (uint)GetDataBit(data1.AsSpan(lines1[i], wpl), j);
                count1 += (int)val1;
                if (val1 != 0) SetDataBit(datat1.AsSpan(linet1[i], wpl), j);
            }
        }
        int count2 = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl, wpl);
            Span<uint> line2 = data2.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j++) {
                uint val2 = (uint)GetDataBit(line1, j);
                count2 += (int)val2;
                if (val2 != 0) SetDataBit(line2, j);
            }
        }
        CompareResults(pixs, pix1, pix2, count1, count2, rp);

            /* Test different methods for 2 bpp */
        count1 = 0;
        int w2 = w / 2;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w2; j++) {
                uint val1 = (uint)GetDataDibit(data1.AsSpan(lines1[i], wpl), j);
                count1 += (int)val1;
                val1 += 0xbbbbbbbcu;
                SetDataDibit(datat1.AsSpan(linet1[i], wpl), j, (int)val1);
            }
        }
        count2 = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl, wpl);
            Span<uint> line2 = data2.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w2; j++) {
                uint val2 = (uint)GetDataDibit(line1, j);
                count2 += (int)val2;
                val2 += 0xbbbbbbbcu;
                SetDataDibit(line2, j, (int)val2);
            }
        }
        CompareResults(pixs, pix1, pix2, count1, count2, rp);

            /* Test different methods for 4 bpp */
        count1 = 0;
        int w4 = w / 4;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w4; j++) {
                uint val1 = (uint)GetDataQbit(data1.AsSpan(lines1[i], wpl), j);
                count1 += (int)val1;
                val1 += 0xbbbbbbb0u;
                SetDataQbit(datat1.AsSpan(linet1[i], wpl), j, (int)val1);
            }
        }
        count2 = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl, wpl);
            Span<uint> line2 = data2.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w4; j++) {
                uint val2 = (uint)GetDataQbit(line1, j);
                count2 += (int)val2;
                val2 += 0xbbbbbbb0u;
                SetDataQbit(line2, j, (int)val2);
            }
        }
        CompareResults(pixs, pix1, pix2, count1, count2, rp);

            /* Test different methods for 8 bpp */
        count1 = 0;
        int w8 = w / 8;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w8; j++) {
                uint val1 = (uint)GetDataByte(data1.AsSpan(lines1[i], wpl), j);
                count1 += (int)val1;
                val1 += 0xbbbbbb00u;
                SetDataByte(datat1.AsSpan(linet1[i], wpl), j, (int)val1);
            }
        }
        count2 = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl, wpl);
            Span<uint> line2 = data2.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w8; j++) {
                uint val2 = (uint)GetDataByte(line1, j);
                count2 += (int)val2;
                val2 += 0xbbbbbb00u;
                SetDataByte(line2, j, (int)val2);
            }
        }
        CompareResults(pixs, pix1, pix2, count1, count2, rp);

            /* Test different methods for 16 bpp */
        count1 = 0;
        int w16 = w / 16;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w16; j++) {
                uint val1 = (uint)GetDataTwoBytes(data1.AsSpan(lines1[i], wpl), j);
                count1 += (int)val1;
                val1 += 0xbbbb0000u;
                SetDataTwoBytes(datat1.AsSpan(linet1[i], wpl), j, (int)val1);
            }
        }
        count2 = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl, wpl);
            Span<uint> line2 = data2.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w16; j++) {
                uint val2 = (uint)GetDataTwoBytes(line1, j);
                count2 += (int)val2;
                val2 += 0xbbbb0000u;
                SetDataTwoBytes(line2, j, (int)val2);
            }
        }
        CompareResults(pixs, pix1, pix2, count1, count2, rp);

            /* Test different methods for 32 bpp */
        count1 = 0;
        int w32 = w / 32;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w32; j++) {
                uint val1 = GetDataFourBytes(data1.AsSpan(lines1[i], wpl), j);
                count1 += (int)(val1 & 0xfffu);
                SetDataFourBytes(datat1.AsSpan(linet1[i], wpl), j, val1);
            }
        }
        count2 = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl, wpl);
            Span<uint> line2 = data2.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w32; j++) {
                uint val2 = GetDataFourBytes(line1, j);
                count2 += (int)(val2 & 0xfffu);
                SetDataFourBytes(line2, j, val2);
            }
        }
        CompareResults(pixs, pix1, pix2, count1, count2, rp);

        Assert.Equal(17, rp.Index);
        rp.Cleanup();
    }

    private static void CompareResults(Pix pixs, Pix pix1, Pix pix2,
                                       int count1, int count2, RegTest rp)
    {
        rp.ComparePix(pixs, pix1);
        rp.ComparePix(pixs, pix2);
        rp.CompareValues(count1, count2, 1);
        pix1.ClearAll();
        pix2.ClearAll();
    }
}
