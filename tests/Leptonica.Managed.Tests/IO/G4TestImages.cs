using System;
using Leptonica;

namespace Leptonica.Managed.Tests.IO;

/// <summary>Deterministic 1 bpp test images with a mix of long runs, noise and similar consecutive rows.</summary>
public static class G4TestImages
{
    public static readonly int[] Widths = [1, 2, 7, 31, 32, 33, 63, 64, 65, 100, 257, 1000, 2528];

    public static Pix Make(int w, int h, int seed)
    {
        var rng = new Random(seed);
        Pix pix = Pix.Create(w, h, 1);
        int wpl = pix.Wpl;
        uint[] data = pix.Data;
        int mode = seed % 4;
        for (int y = 0; y < h; y++)
        {
            if (mode == 0)
            {
                // all white (0) or all black rows
                if (y % 5 == 4)
                    for (int x = 0; x < w; x++) Set(data, y * wpl, x);
                continue;
            }
            if (y > 0 && rng.Next(3) > 0)
            {
                // similar to the previous row: copy it and flip a few pixels
                Array.Copy(data, (y - 1) * wpl, data, y * wpl, wpl);
                int flips = rng.Next(4);
                for (int k = 0; k < flips; k++) Flip(data, y * wpl, rng.Next(w));
            }
            else
            {
                int x = 0;
                bool black = rng.Next(2) == 0;
                while (x < w)
                {
                    int maxRun = mode == 1 ? 6 : mode == 2 ? 80 : 3000;
                    int run = 1 + rng.Next(maxRun);
                    for (int i = 0; i < run && x < w; i++, x++)
                        if (black) Set(data, y * wpl, x);
                    black = !black;
                }
            }
        }
        return pix;
    }

    private static void Set(uint[] data, int row, int x) => data[row + (x >> 5)] |= 0x80000000u >> (x & 31);

    private static void Flip(uint[] data, int row, int x) => data[row + (x >> 5)] ^= 0x80000000u >> (x & 31);
}
