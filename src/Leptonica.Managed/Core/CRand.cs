using System;

namespace Leptonica;

/// <summary>
/// Replacement for the C runtime rand() / srand(). Reproduces the MSVC CRT generator,
/// so sequences match the goldens. One generator per thread, as in the CRT.
/// </summary>
public static class CRand
{
    /* MSVC: rand() without a prior srand() behaves as srand(1) */
    [ThreadStatic] private static uint state;
    [ThreadStatic] private static bool seeded;

    /// <summary>Largest value returned by <see cref="Next"/> (RAND_MAX).</summary>
    public const int RandMax = 0x7fff;

    /// <summary>srand().</summary>
    public static void Seed(uint seed)
    {
        state = seed;
        seeded = true;
    }

    /// <summary>rand(): a value in [0, 32767].</summary>
    public static int Next()
    {
        if (!seeded)
            Seed(1);
        state = state * 214013 + 2531011;
        return (int)((state >> 16) & 0x7fff);
    }
}
