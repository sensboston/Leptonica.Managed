using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Leptonica;

/*
 *  correlscore.c: correlation between pairs of 1 bpp images.
 *
 *     Optimized 2 pix correlators (for jbig2 clustering)
 *         Pix.CorrelationScore (pixCorrelationScore)
 *         Pix.CorrelationScoreThresholded (pixCorrelationScoreThresholded)
 *
 *     Simple 2 pix correlators
 *         Pix.CorrelationScoreSimple (pixCorrelationScoreSimple)
 *         Pix.CorrelationScoreShifted (pixCorrelationScoreShifted)
 *
 *     There are other, more application-oriented functions, that
 *     compute the correlation between two binary images, taking into
 *     account small translational shifts, between two binary images.
 *     These are:
 *         compare.c:     pixBestCorrelation()
 *                        Uses coarse-to-fine translations of full image
 *         recogident.c:  pixCorrelationBestShift()
 *                        Uses small shifts between c.c. centroids.
 *
 *  The row pointers of the C code (row1, row2) are indices into the data arrays.
 */
public sealed partial class Pix
{
    /// <summary>Number of ON pixels in the four bytes of a word, using hardware popcnt.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumTab4(int[]? tab, uint andw)
    {
        return BitOperations.PopCount(andw);
    }

    /* -------------------------------------------------------------------- *
     *           Optimized 2 pix correlators (for jbig2 clustering)         *
     * -------------------------------------------------------------------- */
    // leptonica: pixCorrelationScore (correlscore.c)
    /// <param name="pix2">exemplar pix, 1 bpp</param>
    /// <param name="area1">number of on pixels in pix1</param>
    /// <param name="area2">number of on pixels in pix2</param>
    /// <param name="delx">x comp of centroid difference</param>
    /// <param name="dely">y comp of centroid difference</param>
    /// <param name="maxdiffw">max width difference of pix1 and pix2</param>
    /// <param name="maxdiffh">max height difference of pix1 and pix2</param>
    /// <param name="tab">sum tab for byte</param>
    /// <returns>correlation score; this pix is the test pix, 1 bpp</returns>
    /// <remarks>
    /// We check first that the two pix are roughly the same size.
    /// For jbclass (jbig2) applications at roughly 300 ppi, maxdiffw and
    /// maxdiffh should be at least 2.
    ///
    /// Only if they meet that criterion do we compare the bitmaps.
    /// The centroid difference is used to align the two images to the
    /// nearest integer for the correlation.
    ///
    /// The correlation score is the ratio of the square of the number of
    /// pixels in the AND of the two bitmaps to the product of the number
    /// of ON pixels in each.  Denote the number of ON pixels in pix1
    /// by |1|, the number in pix2 by |2|, and the number in the AND
    /// of pix1 and pix2 by |1 &amp; 2|.  The correlation score is then
    /// (|1 &amp; 2|)**2 / (|1|*|2|).
    ///
    /// This score is compared with an input threshold, which can
    /// be modified depending on the weight of the template.
    /// The modified threshold is
    ///    thresh + (1.0 - thresh) * weight * R
    /// where
    ///    weight is a fixed input factor between 0.0 and 1.0
    ///    R = |2| / area(2)
    /// and area(2) is the total number of pixels in 2 (i.e., width x height).
    ///
    /// To understand why a weight factor is useful, consider what happens
    /// with thick, sans-serif characters that look similar and have a value
    /// of R near 1.  Different characters can have a high correlation value,
    /// and the classifier will make incorrect substitutions.  The weight
    /// factor raises the threshold for these characters.
    ///
    /// Yet another approach to reduce such substitutions is to run the classifier
    /// in a non-greedy way, matching to the template with the highest
    /// score, not the first template with a score satisfying the matching
    /// constraint.  However, this is not particularly effective.
    ///
    /// The implementation here gives the same result as in
    /// pixCorrelationScoreSimple(), where a temporary Pix is made to hold
    /// the AND and implementation uses rasterop:
    ///     pixt = pixCreateTemplate(pix1);
    ///     pixRasterop(pixt, idelx, idely, wt, ht, PIX_SRC, pix2, 0, 0);
    ///     pixRasterop(pixt, 0, 0, wi, hi, PIX_SRC &amp; PIX_DST, pix1, 0, 0);
    ///     pixCountPixels(pixt, &amp;count, tab);
    ///     pixDestroy(&amp;pixt);
    /// However, here it is done in a streaming fashion, counting as it goes,
    /// and touching memory exactly once, giving a 3-4x speedup over the
    /// simple implementation.  This very fast correlation matcher was
    /// contributed by William Rucklidge.
    /// </remarks>
    public float CorrelationScore(Pix pix2, int area1, int area2, float delx, float dely,
                                  int maxdiffw, int maxdiffh, int[] tab)
    {
        Pix pix1 = this;
        int idelx, idely, count;
        int pix1lskip, pix2lskip, rowwords1;
        uint word1, word2, andw;
        int x, y;

        if (pix1.Depth != 1)
            throw Lept.Arg("pixCorrelationScore", "pix1 undefined or not 1 bpp");
        if (pix2.Depth != 1)
            throw Lept.Arg("pixCorrelationScore", "pix2 undefined or not 1 bpp");
        if (area1 <= 0 || area2 <= 0)
            throw Lept.Arg("pixCorrelationScore", "areas must be > 0");

            /* Eliminate based on size difference */
        pix1.GetDimensions(out int wi, out int hi, out _);
        pix2.GetDimensions(out int wt, out int ht, out _);
        int delw = Math.Abs(wi - wt);
        if (delw > maxdiffw)
            return 0.0f;
        int delh = Math.Abs(hi - ht);
        if (delh > maxdiffh)
            return 0.0f;

            /* Round difference to nearest integer */
        if (delx >= 0)
            idelx = (int)(delx + 0.5);
        else
            idelx = (int)(delx - 0.5);
        if (dely >= 0)
            idely = (int)(dely + 0.5);
        else
            idely = (int)(dely - 0.5);

        count = 0;
        int wpl1 = pix1.Wpl;
        int wpl2 = pix2.Wpl;
        int rowwords2 = wpl2;
        uint[] data1 = pix1.Data;
        uint[] data2 = pix2.Data;

            /* What rows of pix1 need to be considered?  Only those underlying the
             * shifted pix2. */
        int lorow = Math.Max(idely, 0);
        int hirow = Math.Min(ht + idely, hi);

            /* Get the pointer to the first row of each image that will be
             * considered. */
        int row1 = wpl1 * lorow;
        int row2 = wpl2 * (lorow - idely);

            /* Similarly, figure out which columns of pix1 will be considered. */
        int locol = Math.Max(idelx, 0);
        int hicol = Math.Min(wt + idelx, wi);

        if (idelx >= 32) {
                /* pix2 is shifted far enough to the right that pix1's first
                 * word(s) won't contribute to the count.  Increment its
                 * pointer to point to the first word that will contribute,
                 * and adjust other values accordingly. */
            pix1lskip = idelx >> 5;  /* # of words to skip on left */
            row1 += pix1lskip;
            locol -= pix1lskip << 5;
            hicol -= pix1lskip << 5;
            idelx &= 31;
        } else if (idelx <= -32) {
                /* pix2 is shifted far enough to the left that its first word(s)
                 * won't contribute to the count.  Increment its pointer
                 * to point to the first word that will contribute,
                 * and adjust other values accordingly. */
            pix2lskip = -((idelx + 31) >> 5);  /* # of words to skip on left */
            row2 += pix2lskip;
            rowwords2 -= pix2lskip;
            idelx += pix2lskip << 5;
        }

        if ((locol >= hicol) || (lorow >= hirow)) {  /* there is no overlap */
            count = 0;
        } else {
                /* How many words of each row of pix1 need to be considered? */
            rowwords1 = (hicol + 31) >> 5;

            if (idelx == 0) {
                    /* There's no lateral offset; simple case. */
                for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    for (x = 0; x < rowwords1; x++) {
                        andw = data1[row1 + x] & data2[row2 + x];
                        count += SumTab4(tab, andw);
                    }
                }
            } else if (idelx > 0) {
                    /* pix2 is shifted to the right.  word 0 of pix1 is touched by
                     * word 0 of pix2; word 1 of pix1 is touched by word 0 and word
                     * 1 of pix2, and so on up to the last word of pix1 (word N),
                     * which is touched by words N-1 and N of pix1... if there is a
                     * word N.  Handle the two cases (pix2 has N-1 words and pix2
                     * has at least N words) separately.
                     *
                     * Note: we know that pix2 has at least N-1 words (i.e.,
                     * rowwords2 >= rowwords1 - 1) by the following logic.
                     * We can pretend that idelx <= 31 because the >= 32 logic
                     * above adjusted everything appropriately.  Then
                     * hicol <= wt + idelx <= wt + 31, so
                     * hicol + 31 <= wt + 62
                     * rowwords1 = (hicol + 31) >> 5 <= (wt + 62) >> 5
                     * rowwords2 == (wt + 31) >> 5, so
                     * rowwords1 <= rowwords2 + 1 */
                if (rowwords2 < rowwords1) {
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                            /* Do the first iteration so the loop can be
                             * branch-free. */
                        word1 = data1[row1];
                        word2 = data2[row2] >> idelx;
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);

                        for (x = 1; x < rowwords2; x++) {
                            word1 = data1[row1 + x];
                            word2 = (data2[row2 + x] >> idelx) |
                                (data2[row2 + x - 1] << (32 - idelx));
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }

                            /* Now the last iteration - we know that this is safe
                             * (i.e.  rowwords1 >= 2) because rowwords1 > rowwords2
                             * > 0 (if it was 0, we'd have taken the "count = 0"
                             * fast-path out of here). */
                        word1 = data1[row1 + x];
                        word2 = data2[row2 + x - 1] << (32 - idelx);
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);
                    }
                } else {
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                            /* Do the first iteration so the loop can be
                             * branch-free.  This section is the same as above
                             * except for the different limit on the loop, since
                             * the last iteration is the same as all the other
                             * iterations (beyond the first). */
                        word1 = data1[row1];
                        word2 = data2[row2] >> idelx;
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);

                        for (x = 1; x < rowwords1; x++) {
                            word1 = data1[row1 + x];
                            word2 = (data2[row2 + x] >> idelx) |
                                (data2[row2 + x - 1] << (32 - idelx));
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }
                    }
                }
            } else {
                    /* pix2 is shifted to the left.  word 0 of pix1 is touched by
                     * word 0 and word 1 of pix2, and so on up to the last word of
                     * pix1 (word N), which is touched by words N and N+1 of
                     * pix2... if there is a word N+1.  Handle the two cases (pix2
                     * has N words and pix2 has at least N+1 words) separately. */
                if (rowwords1 < rowwords2) {
                        /* pix2 has at least N+1 words, so every iteration through
                         * the loop can be the same. */
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                        for (x = 0; x < rowwords1; x++) {
                            word1 = data1[row1 + x];
                            word2 = data2[row2 + x] << -idelx;
                            word2 |= data2[row2 + x + 1] >> (32 + idelx);
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }
                    }
                } else {
                        /* pix2 has only N words, so the last iteration is broken
                         * out. */
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                        for (x = 0; x < rowwords1 - 1; x++) {
                            word1 = data1[row1 + x];
                            word2 = data2[row2 + x] << -idelx;
                            word2 |= data2[row2 + x + 1] >> (32 + idelx);
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }

                        word1 = data1[row1 + x];
                        word2 = data2[row2 + x] << -idelx;
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);
                    }
                }
            }
        }

        return (float)count * (float)count /
               ((float)area1 * (float)area2);
    }

    // leptonica: pixCorrelationScoreThresholded (correlscore.c)
    /// <param name="pix2">exemplar pix, 1 bpp</param>
    /// <param name="area1">number of on pixels in pix1</param>
    /// <param name="area2">number of on pixels in pix2</param>
    /// <param name="delx">x comp of centroid difference</param>
    /// <param name="dely">y comp of centroid difference</param>
    /// <param name="maxdiffw">max width difference of pix1 and pix2</param>
    /// <param name="maxdiffh">max height difference of pix1 and pix2</param>
    /// <param name="tab">sum tab for byte</param>
    /// <param name="downcount">count of 1 pixels below each row of pix1</param>
    /// <param name="score_threshold"></param>
    /// <returns>whether the correlation score is >= score_threshold; this pix is the test pix, 1 bpp</returns>
    /// <remarks>
    /// We check first that the two pix are roughly the same size.
    /// Only if they meet that criterion do we compare the bitmaps.
    /// The centroid difference is used to align the two images to the
    /// nearest integer for the correlation.
    ///
    /// The correlation score is the ratio of the square of the number of
    /// pixels in the AND of the two bitmaps to the product of the number
    /// of ON pixels in each.  Denote the number of ON pixels in pix1
    /// by |1|, the number in pix2 by |2|, and the number in the AND
    /// of pix1 and pix2 by |1 &amp; 2|.  The correlation score is then
    /// (|1 &amp; 2|)**2 / (|1|*|2|).
    ///
    /// This score is compared with an input threshold, which can
    /// be modified depending on the weight of the template.
    /// The modified threshold is
    ///    thresh + (1.0 - thresh) * weight * R
    /// where
    ///    weight is a fixed input factor between 0.0 and 1.0
    ///    R = |2| / area(2)
    /// and area(2) is the total number of pixels in 2 (i.e., width x height).
    ///
    /// To understand why a weight factor is useful, consider what happens
    /// with thick, sans-serif characters that look similar and have a value
    /// of R near 1.  Different characters can have a high correlation value,
    /// and the classifier will make incorrect substitutions.  The weight
    /// factor raises the threshold for these characters.
    ///
    /// Yet another approach to reduce such substitutions is to run the classifier
    /// in a non-greedy way, matching to the template with the highest
    /// score, not the first template with a score satisfying the matching
    /// constraint.  However, this is not particularly effective.
    ///
    /// This very fast correlation matcher was contributed by William Rucklidge.
    /// </remarks>
    public bool CorrelationScoreThresholded(Pix pix2, int area1, int area2, float delx, float dely,
                                            int maxdiffw, int maxdiffh, int[] tab, int[] downcount,
                                            float score_threshold)
    {
        Pix pix1 = this;
        int idelx, idely, count;
        int pix1lskip, pix2lskip, rowwords1;
        uint word1, word2, andw;
        int x, y;
        int untouchable = 0;

        if (pix1.Depth != 1)
            throw Lept.Arg("pixCorrelationScoreThresholded", "pix1 undefined or not 1 bpp");
        if (pix2.Depth != 1)
            throw Lept.Arg("pixCorrelationScoreThresholded", "pix2 undefined or not 1 bpp");
        if (area1 <= 0 || area2 <= 0)
            throw Lept.Arg("pixCorrelationScoreThresholded", "areas must be > 0");

            /* Eliminate based on size difference */
        pix1.GetDimensions(out int wi, out int hi, out _);
        pix2.GetDimensions(out int wt, out int ht, out _);
        int delw = Math.Abs(wi - wt);
        if (delw > maxdiffw)
            return false;
        int delh = Math.Abs(hi - ht);
        if (delh > maxdiffh)
            return false;

            /* Round difference to nearest integer */
        if (delx >= 0)
            idelx = (int)(delx + 0.5);
        else
            idelx = (int)(delx - 0.5);
        if (dely >= 0)
            idely = (int)(dely + 0.5);
        else
            idely = (int)(dely - 0.5);

            /* Compute the correlation count that is needed so that
             * count * count / (area1 * area2) >= score_threshold */
        int threshold = (int)Math.Ceiling(Math.Sqrt((double)score_threshold * area1 * area2));

        count = 0;
        int wpl1 = pix1.Wpl;
        int wpl2 = pix2.Wpl;
        int rowwords2 = wpl2;
        uint[] data1 = pix1.Data;
        uint[] data2 = pix2.Data;

            /* What rows of pix1 need to be considered?  Only those underlying the
             * shifted pix2. */
        int lorow = Math.Max(idely, 0);
        int hirow = Math.Min(ht + idely, hi);

            /* Get the pointer to the first row of each image that will be
             * considered. */
        int row1 = wpl1 * lorow;
        int row2 = wpl2 * (lorow - idely);
        if (hirow <= hi && hirow >= 1) {  /* C reads downcount[hirow - 1] also for hirow <= 0 */
                /* Some rows of pix1 will never contribute to count */
            untouchable = downcount[hirow - 1];
        }

            /* Similarly, figure out which columns of pix1 will be considered. */
        int locol = Math.Max(idelx, 0);
        int hicol = Math.Min(wt + idelx, wi);

        if (idelx >= 32) {
                /* pix2 is shifted far enough to the right that pix1's first
                 * word(s) won't contribute to the count.  Increment its
                 * pointer to point to the first word that will contribute,
                 * and adjust other values accordingly. */
            pix1lskip = idelx >> 5;  /* # of words to skip on left */
            row1 += pix1lskip;
            locol -= pix1lskip << 5;
            hicol -= pix1lskip << 5;
            idelx &= 31;
        } else if (idelx <= -32) {
                /* pix2 is shifted far enough to the left that its first word(s)
                 * won't contribute to the count.  Increment its pointer
                 * to point to the first word that will contribute,
                 * and adjust other values accordingly. */
            pix2lskip = -((idelx + 31) >> 5);  /* # of words to skip on left */
            row2 += pix2lskip;
            rowwords2 -= pix2lskip;
            idelx += pix2lskip << 5;
        }

        if ((locol >= hicol) || (lorow >= hirow)) {  /* there is no overlap */
            count = 0;
        } else {
                /* How many words of each row of pix1 need to be considered? */
            rowwords1 = (hicol + 31) >> 5;

            if (idelx == 0) {
                    /* There's no lateral offset; simple case. */
                for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    for (x = 0; x < rowwords1; x++) {
                        andw = data1[row1 + x] & data2[row2 + x];
                        count += SumTab4(tab, andw);
                    }

                        /* If the count is over the threshold, no need to
                         * calculate any further.  Likewise, return early if the
                         * count plus the maximum count attainable from further
                         * rows is below the threshold. */
                    if (count >= threshold) return true;
                    if (count + downcount[y] - untouchable < threshold) {
                        return false;
                    }
                }
            } else if (idelx > 0) {
                    /* pix2 is shifted to the right.  word 0 of pix1 is touched by
                     * word 0 of pix2; word 1 of pix1 is touched by word 0 and word
                     * 1 of pix2, and so on up to the last word of pix1 (word N),
                     * which is touched by words N-1 and N of pix1... if there is a
                     * word N.  Handle the two cases (pix2 has N-1 words and pix2
                     * has at least N words) separately.
                     *
                     * Note: we know that pix2 has at least N-1 words (i.e.,
                     * rowwords2 >= rowwords1 - 1) by the following logic.
                     * We can pretend that idelx <= 31 because the >= 32 logic
                     * above adjusted everything appropriately.  Then
                     * hicol <= wt + idelx <= wt + 31, so
                     * hicol + 31 <= wt + 62
                     * rowwords1 = (hicol + 31) >> 5 <= (wt + 62) >> 5
                     * rowwords2 == (wt + 31) >> 5, so
                     * rowwords1 <= rowwords2 + 1 */
                if (rowwords2 < rowwords1) {
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                            /* Do the first iteration so the loop can be
                             * branch-free. */
                        word1 = data1[row1];
                        word2 = data2[row2] >> idelx;
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);

                        for (x = 1; x < rowwords2; x++) {
                            word1 = data1[row1 + x];
                            word2 = (data2[row2 + x] >> idelx) |
                                (data2[row2 + x - 1] << (32 - idelx));
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }

                            /* Now the last iteration - we know that this is safe
                             * (i.e.  rowwords1 >= 2) because rowwords1 > rowwords2
                             * > 0 (if it was 0, we'd have taken the "count = 0"
                             * fast-path out of here). */
                        word1 = data1[row1 + x];
                        word2 = data2[row2 + x - 1] << (32 - idelx);
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);

                        if (count >= threshold) return true;
                        if (count + downcount[y] - untouchable < threshold) {
                            return false;
                        }
                    }
                } else {
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                            /* Do the first iteration so the loop can be
                             * branch-free.  This section is the same as above
                             * except for the different limit on the loop, since
                             * the last iteration is the same as all the other
                             * iterations (beyond the first). */
                        word1 = data1[row1];
                        word2 = data2[row2] >> idelx;
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);

                        for (x = 1; x < rowwords1; x++) {
                            word1 = data1[row1 + x];
                            word2 = (data2[row2 + x] >> idelx) |
                                (data2[row2 + x - 1] << (32 - idelx));
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }

                        if (count >= threshold) return true;
                        if (count + downcount[y] - untouchable < threshold) {
                            return false;
                        }
                    }
                }
            } else {
                    /* pix2 is shifted to the left.  word 0 of pix1 is touched by
                     * word 0 and word 1 of pix2, and so on up to the last word of
                     * pix1 (word N), which is touched by words N and N+1 of
                     * pix2... if there is a word N+1.  Handle the two cases (pix2
                     * has N words and pix2 has at least N+1 words) separately. */
                if (rowwords1 < rowwords2) {
                        /* pix2 has at least N+1 words, so every iteration through
                         * the loop can be the same. */
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                        for (x = 0; x < rowwords1; x++) {
                            word1 = data1[row1 + x];
                            word2 = data2[row2 + x] << -idelx;
                            word2 |= data2[row2 + x + 1] >> (32 + idelx);
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }

                        if (count >= threshold) return true;
                        if (count + downcount[y] - untouchable < threshold) {
                            return false;
                        }
                    }
                } else {
                        /* pix2 has only N words, so the last iteration is broken
                         * out. */
                    for (y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                        for (x = 0; x < rowwords1 - 1; x++) {
                            word1 = data1[row1 + x];
                            word2 = data2[row2 + x] << -idelx;
                            word2 |= data2[row2 + x + 1] >> (32 + idelx);
                            andw = word1 & word2;
                            count += SumTab4(tab, andw);
                        }

                        word1 = data1[row1 + x];
                        word2 = data2[row2 + x] << -idelx;
                        andw = word1 & word2;
                        count += SumTab4(tab, andw);

                        if (count >= threshold) return true;
                        if (count + downcount[y] - untouchable < threshold) {
                            return false;
                        }
                    }
                }
            }
        }

        float score = (float)count * (float)count /
                      ((float)area1 * (float)area2);
        if (score >= score_threshold) {
            Log.Info("pixCorrelationScoreThresholded",
                     $"count {count} < threshold {threshold} but score {score} >= score_threshold {score_threshold}");
        }
        return false;
    }

    /* -------------------------------------------------------------------- *
     *             Simple 2 pix correlators (for jbig2 clustering)          *
     * -------------------------------------------------------------------- */
    // leptonica: pixCorrelationScoreSimple (correlscore.c)
    /// <param name="pix2">exemplar pix, 1 bpp</param>
    /// <param name="area1">number of on pixels in pix1</param>
    /// <param name="area2">number of on pixels in pix2</param>
    /// <param name="delx">x comp of centroid difference</param>
    /// <param name="dely">y comp of centroid difference</param>
    /// <param name="maxdiffw">max width difference of pix1 and pix2</param>
    /// <param name="maxdiffh">max height difference of pix1 and pix2</param>
    /// <param name="tab">sum tab for byte</param>
    /// <returns>correlation score, in range [0.0 ... 1.0]; this pix is the test pix, 1 bpp</returns>
    /// <remarks>
    /// (1) This calculates exactly the same value as pixCorrelationScore().
    ///     It is 2-3x slower, but much simpler to understand.
    /// (2) The returned correlation score is 0.0 if the width or height
    ///     exceed %maxdiffw or %maxdiffh.
    /// </remarks>
    public float CorrelationScoreSimple(Pix pix2, int area1, int area2, float delx, float dely,
                                        int maxdiffw, int maxdiffh, int[] tab)
    {
        Pix pix1 = this;
        int idelx, idely;

        if (pix1.Depth != 1)
            throw Lept.Arg("pixCorrelationScoreSimple", "pix1 undefined or not 1 bpp");
        if (pix2.Depth != 1)
            throw Lept.Arg("pixCorrelationScoreSimple", "pix2 undefined or not 1 bpp");
        if (area1 == 0 || area2 == 0)
            throw Lept.Arg("pixCorrelationScoreSimple", "areas must be > 0");

            /* Eliminate based on size difference */
        pix1.GetDimensions(out int wi, out int hi, out _);
        pix2.GetDimensions(out int wt, out int ht, out _);
        int delw = Math.Abs(wi - wt);
        if (delw > maxdiffw)
            return 0.0f;
        int delh = Math.Abs(hi - ht);
        if (delh > maxdiffh)
            return 0.0f;

            /* Round difference to nearest integer */
        if (delx >= 0)
            idelx = (int)(delx + 0.5);
        else
            idelx = (int)(delx - 0.5);
        if (dely >= 0)
            idely = (int)(dely + 0.5);
        else
            idely = (int)(dely - 0.5);

            /*  pixt = pixAnd(NULL, pix1, pix2), including shift.
             *  To insure that pixels are ON only within the
             *  intersection of pix1 and the shifted pix2:
             *  (1) Start with pixt cleared and equal in size to pix1.
             *  (2) Blit the shifted pix2 onto pixt.  Then all ON pixels
             *      are within the intersection of pix1 and the shifted pix2.
             *  (3) AND pix1 with pixt. */
        Pix pixt = CreateTemplate(pix1);
        pixt.Rasterop(idelx, idely, wt, ht, L.PIX_SRC, pix2, 0, 0);
        pixt.Rasterop(0, 0, wi, hi, L.PIX_SRC & L.PIX_DST, pix1, 0, 0);
        int count = pixt.CountPixels(tab);

        return (float)count * (float)count /
               ((float)area1 * (float)area2);
    }

    // leptonica: pixCorrelationScoreShifted (correlscore.c)
    /// <summary>Correlation score of this 1 bpp pix with pix2 shifted by (delx, dely).</summary>
    /// <param name="pix2">1 bpp</param>
    /// <param name="area1">number of on pixels in this pix</param>
    /// <param name="area2">number of on pixels in pix2</param>
    /// <param name="delx">x translation of pix2 relative to pix1</param>
    /// <param name="dely">y translation of pix2 relative to pix1</param>
    /// <param name="tab">sum tab for ON pixels in a byte</param>
    /// <returns>correlation score</returns>
    /// <remarks>
    /// (1) This finds the correlation between two 1 bpp images,
    ///     when pix2 is shifted by (delx, dely) with respect
    ///     to each other.
    /// (2) This is implemented by starting with a copy of pix1 and
    ///     ANDing its pixels with those of a shifted pix2.
    /// (3) Get the pixel counts for area1 and area2 using piCountPixels().
    /// (4) A good estimate for a shift that would maximize the correlation
    ///     is to align the centroids (cx1, cy1; cx2, cy2), giving the
    ///     relative translations etransx and etransy:
    ///        etransx = cx1 - cx2
    ///        etransy = cy1 - cy2
    ///     Typically delx is chosen to be near etransx; ditto for dely.
    ///     This function is used in pixBestCorrelation(), where the
    ///     translations delx and dely are varied to find the best alignment.
    /// (5) We do not check the sizes of pix1 and pix2, because they should
    ///     be comparable.
    /// </remarks>
    public float CorrelationScoreShifted(Pix pix2, int area1, int area2, int delx, int dely,
                                         int[]? tab)
    {
        Pix pix1 = this;

        if (pix1.Depth != 1)
            throw Lept.Arg("pixCorrelationScoreShifted", "pix1 undefined or not 1 bpp");
        if (pix2.Depth != 1)
            throw Lept.Arg("pixCorrelationScoreShifted", "pix2 undefined or not 1 bpp");
        if (area1 == 0 || area2 == 0)
            throw Lept.Arg("pixCorrelationScoreShifted", "areas must be > 0");

        int count = CountShiftedOverlap(pix1, pix2, delx, dely);

        return (float)count * (float)count /
               ((float)area1 * (float)area2);
    }

    internal static int CountShiftedOverlap(Pix pix1, Pix pix2, int idelx, int idely)
    {
        pix1.GetDimensions(out int wi, out int hi, out _);
        pix2.GetDimensions(out int wt, out int ht, out _);

        int lorow = Math.Max(idely, 0);
        int hirow = Math.Min(ht + idely, hi);
        if (lorow >= hirow)
            return 0;

        int wpl1 = pix1.Wpl;
        int wpl2 = pix2.Wpl;
        int rowwords2 = wpl2;
        uint[] data1 = pix1.Data;
        uint[] data2 = pix2.Data;

        int row1 = wpl1 * lorow;
        int row2 = wpl2 * (lorow - idely);
        int locol = Math.Max(idelx, 0);
        int hicol = Math.Min(wt + idelx, wi);

        if (idelx >= 32) {
            int pix1lskip = idelx >> 5;
            row1 += pix1lskip;
            locol -= pix1lskip << 5;
            hicol -= pix1lskip << 5;
            idelx &= 31;
        } else if (idelx <= -32) {
            int pix2lskip = -((idelx + 31) >> 5);
            row2 += pix2lskip;
            rowwords2 -= pix2lskip;
            idelx += pix2lskip << 5;
        }

        if (locol >= hicol)
            return 0;

        int rowwords1 = (hicol + 31) >> 5;
        int endbits = hicol & 31;
        uint lastMask = endbits == 0 ? 0xffffffffu : (0xffffffffu << (32 - endbits));
        int count = 0;

        if (idelx == 0) {
            int full = rowwords1 - 1;
            for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                for (int x = 0; x < full; x++)
                    count += BitOperations.PopCount(data1[row1 + x] & data2[row2 + x]);
                count += BitOperations.PopCount(data1[row1 + full] & data2[row2 + full] & lastMask);
            }
        } else if (idelx > 0) {
            int rshift = 32 - idelx;
            if (rowwords2 < rowwords1) {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    count += BitOperations.PopCount(data1[row1] & (data2[row2] >> idelx));
                    int x;
                    for (x = 1; x < rowwords2; x++) {
                        uint w2 = (data2[row2 + x] >> idelx) | (data2[row2 + x - 1] << rshift);
                        count += BitOperations.PopCount(data1[row1 + x] & w2);
                    }
                    uint wLast = data2[row2 + x - 1] << rshift;
                    count += BitOperations.PopCount(data1[row1 + x] & wLast & lastMask);
                }
            } else {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    if (rowwords1 == 1) {
                        count += BitOperations.PopCount(data1[row1] & (data2[row2] >> idelx) & lastMask);
                    } else {
                        count += BitOperations.PopCount(data1[row1] & (data2[row2] >> idelx));
                        int last = rowwords1 - 1;
                        for (int x = 1; x < last; x++) {
                            uint w2 = (data2[row2 + x] >> idelx) | (data2[row2 + x - 1] << rshift);
                            count += BitOperations.PopCount(data1[row1 + x] & w2);
                        }
                        uint wLast = (data2[row2 + last] >> idelx) | (data2[row2 + last - 1] << rshift);
                        count += BitOperations.PopCount(data1[row1 + last] & wLast & lastMask);
                    }
                }
            }
        } else {
            int lshift = -idelx;
            int rshift = 32 + idelx;
            int last = rowwords1 - 1;
            if (rowwords1 < rowwords2) {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    for (int x = 0; x < last; x++) {
                        uint w2 = (data2[row2 + x] << lshift) | (data2[row2 + x + 1] >> rshift);
                        count += BitOperations.PopCount(data1[row1 + x] & w2);
                    }
                    uint wLast = (data2[row2 + last] << lshift) | (data2[row2 + last + 1] >> rshift);
                    count += BitOperations.PopCount(data1[row1 + last] & wLast & lastMask);
                }
            } else {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    for (int x = 0; x < last; x++) {
                        uint w2 = (data2[row2 + x] << lshift) | (data2[row2 + x + 1] >> rshift);
                        count += BitOperations.PopCount(data1[row1 + x] & w2);
                    }
                    uint wLast = data2[row2 + last] << lshift;
                    count += BitOperations.PopCount(data1[row1 + last] & wLast & lastMask);
                }
            }
        }

        return count;
    }
}
