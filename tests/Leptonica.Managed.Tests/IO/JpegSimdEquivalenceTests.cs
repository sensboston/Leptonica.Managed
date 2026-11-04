using System;
using BitMiracle.LibJpeg.Classic;
using BitMiracle.LibJpeg.Classic.Internal;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

/// <summary>
/// The vectorized JPEG decoder routines must give exactly the output of the
/// plain scalar code they replace.
/// </summary>
public class JpegSimdEquivalenceTests
{
    // Scalar reference: the original libjpeg h2v2_fancy_upsample row loop.
    private static void RefH2v2FancyRow(byte[] inptr0, byte[] inptr1, byte[] outptr, int width)
    {
        int i = 0, o = 0;
        int thiscolsum = inptr0[i] * 3 + inptr1[i];
        i++;
        int nextcolsum = inptr0[i] * 3 + inptr1[i];
        i++;
        outptr[o++] = (byte)((thiscolsum * 4 + 8) >> 4);
        outptr[o++] = (byte)((thiscolsum * 3 + nextcolsum + 7) >> 4);
        int lastcolsum = thiscolsum;
        thiscolsum = nextcolsum;
        for (int colctr = width - 2; colctr > 0; colctr--) {
            nextcolsum = inptr0[i] * 3 + inptr1[i];
            i++;
            outptr[o++] = (byte)((thiscolsum * 3 + lastcolsum + 8) >> 4);
            outptr[o++] = (byte)((thiscolsum * 3 + nextcolsum + 7) >> 4);
            lastcolsum = thiscolsum;
            thiscolsum = nextcolsum;
        }
        outptr[o++] = (byte)((thiscolsum * 3 + lastcolsum + 8) >> 4);
        outptr[o] = (byte)((thiscolsum * 4 + 7) >> 4);
    }

    // Same table as jpeg_decomp_master.prepare_range_limit_table.
    private static byte[] MakeLimitTable(out int offset)
    {
        var table = new byte[5 * (JpegConstants.MAXJSAMPLE + 1)];
        offset = 2 * (JpegConstants.MAXJSAMPLE + 1);
        int i;
        for (i = 0; i <= JpegConstants.MAXJSAMPLE; i++)
            table[offset + i] = (byte)i;
        for (; i < 3 * (JpegConstants.MAXJSAMPLE + 1); i++)
            table[offset + i] = JpegConstants.MAXJSAMPLE;
        return table;
    }

    private static ComponentBuffer MakeOutput(int cols)
    {
        var rows = new byte[8][];
        for (int r = 0; r < 8; r++)
            rows[r] = new byte[cols];
        var cb = new ComponentBuffer();
        cb.SetBuffer(rows);
        return cb;
    }

    private static short RandomCoef(Random rnd, int kind)
    {
        switch (kind) {
            case 0: return (short)rnd.Next(-64, 65);                  // typical
            case 1: return (short)rnd.Next(short.MinValue, short.MaxValue + 1);   // extreme
            case 2: return (short)(rnd.Next(2) == 0 ? short.MaxValue : short.MinValue);
            default: return (short)(rnd.Next(4) == 0 ? rnd.Next(-2000, 2001) : 0); // sparse
        }
    }

    [Fact]
    public void IdctIslow_MatchesScalar()
    {
        var rnd = new Random(777);
        byte[] limit = MakeLimitTable(out int limitOffset);
        var workspace = new int[128];
        var coef = new short[64];
        var quant = new int[64];
        const int outCols = 24;
        int mismatches = 0;
        for (int iter = 0; iter < 60000 && mismatches == 0; iter++) {
            int kind = iter % 4;
            int quantKind = (iter / 4) % 3;
            for (int k = 0; k < 64; k++) {
                coef[k] = RandomCoef(rnd, kind);
                quant[k] = quantKind == 0 ? rnd.Next(1, 256) : quantKind == 1 ? rnd.Next(1, 65536) : rnd.Next(0, 3);
            }
            switch ((iter / 12) % 6) {
                case 0:   // DC only
                    for (int k = 1; k < 64; k++) coef[k] = 0;
                    break;
                case 1:   // whole columns / rows with zero AC
                    for (int c = 0; c < 8; c++)
                        if (rnd.Next(2) == 0)
                            for (int r = 1; r < 8; r++) coef[r * 8 + c] = 0;
                    break;
                case 2:   // everything zero but a single AC term
                    {
                        short v = coef[rnd.Next(1, 64)];
                        Array.Clear(coef);
                        coef[rnd.Next(1, 64)] = v;
                    }
                    break;
                case 3:   // first row only
                    for (int k = 8; k < 64; k++) coef[k] = 0;
                    break;
            }
            int col = rnd.Next(0, 2) * 16;
            ComponentBuffer exp = MakeOutput(outCols);
            ComponentBuffer act = MakeOutput(outCols);
            jpeg_inverse_dct.IdctIslowScalar(coef, quant, workspace, limit, limitOffset, exp, 0, col);
            jpeg_idct_islow_simd.Idct(coef, quant, limitOffset, act, 0, col);
            for (int r = 0; r < 8; r++)
                if (!exp[r].AsSpan().SequenceEqual(act[r]))
                    mismatches++;
            Assert.True(mismatches == 0, $"iter={iter} kind={kind} quantKind={quantKind}");
        }
    }

    [Fact]
    public void H2v2FancyRow_MatchesScalar()
    {
        var rnd = new Random(12345);
        // all widths around the vector step sizes, odd and even
        for (int width = 3; width <= 130; width++) {
            for (int mode = 0; mode < 4; mode++) {
                var a = new byte[width + 16];
                var b = new byte[width + 16];
                switch (mode) {
                    case 0: rnd.NextBytes(a); rnd.NextBytes(b); break;
                    case 1: Array.Fill(a, (byte)255); Array.Fill(b, (byte)255); break;
                    case 2: Array.Fill(a, (byte)0); Array.Fill(b, (byte)255); break;
                    default:
                        for (int k = 0; k < a.Length; k++) {
                            a[k] = (byte)((k & 1) == 0 ? 255 : 0);
                            b[k] = (byte)((k & 1) == 0 ? 0 : 255);
                        }
                        break;
                }
                var expect = new byte[2 * width + 32];
                var actual = new byte[2 * width + 32];
                RefH2v2FancyRow(a, b, expect, width);
                my_upsampler.H2v2FancyRow(a, b, actual, width);
                Assert.True(expect.AsSpan().SequenceEqual(actual), $"width={width} mode={mode}");
            }
        }
    }
}
