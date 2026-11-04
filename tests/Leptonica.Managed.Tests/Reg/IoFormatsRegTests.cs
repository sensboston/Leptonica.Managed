using System;
using System.IO;
using System.Runtime.InteropServices;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// ioformats_reg.c: primary regression test for lossless &amp; lossy read/write I/O
/// across all standard image formats, depths, colormaps, alpha, and headers.
/// </summary>
public class IoFormatsRegTests
{
    private const string BMP_FILE = "test1.bmp";
    private const string FILE_1BPP = "feyn.tif";
    private const string FILE_2BPP = "speckle2.png";
    private const string FILE_2BPP_C = "weasel2.4g.png";
    private const string FILE_4BPP = "speckle4.png";
    private const string FILE_4BPP_C = "weasel4.16c.png";
    private const string FILE_8BPP_1 = "dreyfus8.png";
    private const string FILE_8BPP_2 = "weasel8.240c.png";
    private const string FILE_8BPP_3 = "test8.jpg";
    private const string FILE_16BPP = "test16.tif";
    private const string FILE_32BPP = "marge.jpg";
    private const string FILE_32BPP_ALPHA = "test32-alpha.png";
    private const string FILE_1BIT_ALPHA = "test-1bit-alpha.png";
    private const string FILE_CMAP_ALPHA = "test-cmap-alpha.png";
    private const string FILE_TRANS_ALPHA = "test-fulltrans-alpha.png";
    private const string FILE_GRAY_ALPHA = "test-gray-alpha.png";
    private const string FILE_GRAY_ALPHA_TIF = "gray-alpha.tif";
    private const string FILE_RGB16_TIF = "rgb16.tif";

    private static string ProgFile(string name) => TestPaths.ProgFile(name);

    [RegFact]
    public void IoFormats()
    {
        var rp = new RegTest("ioformats");
        bool failure = false;

            /* --------- Part 1: Test all formats for r/w to file --------- */
        using (new LeptDebugScope(true)) {
            foreach (string fn in new[] {
                BMP_FILE, FILE_1BPP, FILE_2BPP, FILE_2BPP_C, FILE_4BPP, FILE_4BPP_C,
                FILE_8BPP_1, FILE_8BPP_2, FILE_8BPP_3, FILE_16BPP, FILE_32BPP,
                FILE_32BPP_ALPHA, FILE_1BIT_ALPHA, FILE_CMAP_ALPHA, FILE_TRANS_ALPHA, FILE_GRAY_ALPHA
            }) {
                if (Pix.IoFormatTest(ProgFile(fn)))
                    failure = true;
            }
        }

            /* ------------------ Part 2: Test tiff r/w to file ------------------- */
        Pixa pixa = Pixa.Create(6);
        Pix pix1 = Pix.Read(ProgFile(BMP_FILE));
        Pix pix2 = Pix.Convert1To2(null, pix1, 3, 0);
        Pix pix4 = Pix.Convert1To4(null, pix1, 15, 0);
        Pix pix16 = Pix.Read(ProgFile(FILE_16BPP));
        Pix pix8 = pix16.Convert16To8(1);
        Pix pix32 = Pix.Read(ProgFile(FILE_32BPP));
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);
        pixa.AddPix(pix4, L.L_INSERT);
        pixa.AddPix(pix8, L.L_INSERT);
        pixa.AddPix(pix16, L.L_INSERT);
        pixa.AddPix(pix32, L.L_INSERT);

        int n = pixa.Count;
        bool success = n == 6;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            if (i == 0) {
                Pix.Write("/tmp/lept/regout/junkg3.tif", pix, L.IFF_TIFF_G3);
                Pix.Write("/tmp/lept/regout/junkg4.tif", pix, L.IFF_TIFF_G4);
                Pix.Write("/tmp/lept/regout/junkrle.tif", pix, L.IFF_TIFF_RLE);
                Pix.Write("/tmp/lept/regout/junkpb.tif", pix, L.IFF_TIFF_PACKBITS);
                if (TestComp("/tmp/lept/regout/junkg3.tif", pix, L.IFF_TIFF_G3)) success = false;
                if (TestComp("/tmp/lept/regout/junkg4.tif", pix, L.IFF_TIFF_G4)) success = false;
                if (TestComp("/tmp/lept/regout/junkrle.tif", pix, L.IFF_TIFF_RLE)) success = false;
                if (TestComp("/tmp/lept/regout/junkpb.tif", pix, L.IFF_TIFF_PACKBITS)) success = false;
            }
            Pix.Write("/tmp/lept/regout/junklzw.tif", pix, L.IFF_TIFF_LZW);
            Pix.Write("/tmp/lept/regout/junkzip.tif", pix, L.IFF_TIFF_ZIP);
            Pix.Write("/tmp/lept/regout/junknon.tif", pix, L.IFF_TIFF);
            if (TestComp("/tmp/lept/regout/junklzw.tif", pix, L.IFF_TIFF_LZW)) success = false;
            if (TestComp("/tmp/lept/regout/junkzip.tif", pix, L.IFF_TIFF_ZIP)) success = false;
            if (TestComp("/tmp/lept/regout/junknon.tif", pix, L.IFF_TIFF)) success = false;
        }

        pix1 = Pix.Read(ProgFile(FILE_8BPP_2));
        Pix.Write("/tmp/lept/regout/weas8.tif", pix1, L.IFF_TIFF);
        Pix.ReadHeaderTiff("/tmp/lept/regout/weas8.tif", 0, out int w, out int h,
                           out int bps, out int spp, out _, out bool iscmap, out _);
        if (w != 82 || h != 73 || bps != 8 || spp != 1 || !iscmap) success = false;
        pix2 = Pix.Read("/tmp/lept/regout/weas8.tif");
        Pix.Write("/tmp/lept/regout/weas8a.tif", pix2, L.IFF_TIFF);
        Pix pix3 = Pix.Read("/tmp/lept/regout/weas8a.tif");
        if (!Pix.Equal(pix1, pix3)) success = false;

        pix1 = Pix.Read(ProgFile("feyn-fract2.tif"));
        PixColormap cmap = PixColormap.Create(1);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 255, 255);
        pix1.SetColormap(cmap);
        Pix.Write("/tmp/lept/regout/fract1.tif", pix1, L.IFF_TIFF_ZIP);
        pix2 = Pix.Read("/tmp/lept/regout/fract1.tif");
        if (!Pix.Equal(pix1, pix2)) success = false;
        cmap = PixColormap.Create(1);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(100, 200, 50);
        pix1.SetColormap(cmap);
        Pix.Write("/tmp/lept/regout/fract2.tif", pix1, L.IFF_TIFF_ZIP);
        pix3 = Pix.Read("/tmp/lept/regout/fract2.tif");
        if (!Pix.Equal(pix1, pix3)) success = false;

        pix1 = Pix.Read(ProgFile(FILE_GRAY_ALPHA_TIF));
        Pix.Write("/tmp/lept/regout/graya.tif", pix1, L.IFF_TIFF);
        Pix.ReadHeaderTiff("/tmp/lept/regout/graya.tif", 0, out w, out h, out bps, out spp, out _, out iscmap, out _);
        if (w != 100 || h != 100 || bps != 8 || spp != 4 || iscmap) success = false;
        pix2 = Pix.Read("/tmp/lept/regout/graya.tif");
        if (!Pix.Equal(pix1, pix2)) success = false;

        pix1 = Pix.Read(ProgFile(FILE_GRAY_ALPHA));
        Pix.WriteTiff("/tmp/lept/regout/graya2.tif", pix1, L.IFF_TIFF_ZIP, "w");
        pix2 = Pix.Read("/tmp/lept/regout/graya2.tif");
        if (!Pix.Equal(pix1, pix2)) success = false;

        pix1 = Pix.Read(ProgFile(FILE_RGB16_TIF));
        Pix.Write("/tmp/lept/regout/rgb16.tif", pix1, L.IFF_TIFF_ZIP);
        Pix.ReadHeaderTiff("/tmp/lept/regout/rgb16.tif", 0, out w, out h, out bps, out spp, out _, out iscmap, out _);
        if (w != 129 || h != 90 || bps != 8 || spp != 3 || iscmap) success = false;
        pix2 = Pix.Read("/tmp/lept/regout/rgb16.tif");
        if (!Pix.Equal(pix1, pix2)) success = false;

        pix1 = Pix.Read(ProgFile("testbuffer.tif"));
        Pix.Write("/tmp/lept/regout/testbuffer.tif", pix1, L.IFF_TIFF_ZIP);
        Pix.ReadHeaderTiff("/tmp/lept/regout/testbuffer.tif", 0, out w, out h, out bps, out spp, out int res, out iscmap, out _);
        if (w != 659 || h != 799 || bps != 8 || spp != 3 || res != 96) success = false;
        pix2 = Pix.Read("/tmp/lept/regout/testbuffer.tif");
        if (!Pix.Equal(pix1, pix2)) success = false;

        if (!success) failure = true;

            /* ------------------ Part 3: Test tiff r/w to memory ----------------- */
        success = true;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            if (i == 0) {
                foreach (int fmt in new[] { L.IFF_TIFF_G3, L.IFF_TIFF_G4, L.IFF_TIFF_RLE, L.IFF_TIFF_PACKBITS }) {
                    byte[] m = Pix.WriteMemTiff(pix, fmt);
                    Pix? pt = Pix.ReadMemTiff(m, 0);
                    if (pt == null || !Pix.Equal(pix, pt)) success = false;
                }
            }
            foreach (int fmt in new[] { L.IFF_TIFF_LZW, L.IFF_TIFF_ZIP, L.IFF_TIFF }) {
                byte[] m = Pix.WriteMemTiff(pix, fmt);
                Pix? pt = Pix.ReadMemTiff(m, 0);
                if (pt == null || !Pix.Equal(pix, pt)) success = false;
            }
        }
        if (!success) failure = true;

            /* ---------------- Part 4: Test non-tiff r/w to memory ---------------- */
        success = true;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            int d = pix.Depth;
            string psname = $"/tmp/lept/regout/junkps.{d}";
            if (TestWriteMem(pix, L.IFF_PNM, null)) success = false;
            if (TestWriteMem(pix, L.IFF_PS, psname)) success = false;
            if (d == 16) continue;
            if (TestWriteMem(pix, L.IFF_PNG, null)) success = false;
            if (TestWriteMem(pix, L.IFF_BMP, null)) success = false;
            if (d != 32 && TestWriteMem(pix, L.IFF_GIF, null)) success = false;
            if (d == 8 || d == 32) {
                if (TestWriteMem(pix, L.IFF_JFIF_JPEG, null)) success = false;
                if (TestWriteMem(pix, L.IFF_JP2, null)) success = false;
                if (TestWriteMem(pix, L.IFF_WEBP, null)) success = false;
            }
        }
        if (!success) failure = true;

            /* ------------ Part 5: Test multipage tiff r/w to memory ------------ */
        Pix mpSrc = Pix.Read(ProgFile("feyn.tif"));
        Pixa mpSplit = Pixa.SplitPix(mpSrc, 3, 3, 0, 0);
        for (int i = 0; i < 9; i++) {
            Pix pt = mpSplit.GetPix(i, L.L_CLONE);
            Pix.WriteTiff("/tmp/lept/regout/junktiffmpage.tif", pt, L.IFF_TIFF_G4, i == 0 ? "w" : "a");
        }
        byte[] mpBytes = Utils.BinaryRead("/tmp/lept/regout/junktiffmpage.tif");
        Pixa mpRead = Pixa.Create(9);
        for (int i = 0; i < 9; i++)
            mpRead.AddPix(Pix.ReadMemTiff(mpBytes, i)!, L.L_INSERT);
        Pix mpUnsplit = mpRead.DisplayUnsplit(3, 3, 0, 0);
        mpSrc.ClipToForeground(out Pix? fg1, out _);
        mpUnsplit.ClipToForeground(out Pix? fg2, out _);
        if (fg1 == null || fg2 == null || !Pix.Equal(fg1, fg2)) failure = true;

            /* ------------ Part 6: Test 24 bpp writing ------------ */
        Pix marge = Pix.Read(ProgFile("marge.jpg"));
        Pix marge24 = Make24BppPix(marge);
        Pix.Write("/tmp/lept/regout/junk24.png", marge24, L.IFF_PNG);
        Pix.Write("/tmp/lept/regout/junk24.jpg", marge24, L.IFF_JFIF_JPEG);
        Pix.Write("/tmp/lept/regout/junk24.tif", marge24, L.IFF_TIFF);
        if (!Pix.Equal(marge, Pix.Read("/tmp/lept/regout/junk24.png"))) failure = true;
        rp.CompareSimilarPix(marge, Pix.Read("/tmp/lept/regout/junk24.jpg"), 10, 0.0002f, 0);  /* 0 */
        if (!Pix.Equal(marge, Pix.Read("/tmp/lept/regout/junk24.tif"))) failure = true;

            /* ------------- Part 7: Miscellaneous additional tests ------------ */
        pix1 = Pix.Read(ProgFile("test32-alpha.png"));
        if (TestWriteMem(pix1, L.IFF_BMP, null)) failure = true;
        if (TestWriteMem(pix1, L.IFF_PNG, null)) failure = true;

        pix1 = Pix.Read(ProgFile("test-rgba.bmp"));
        Pix.Write("/tmp/lept/regout/alpha1.bmp", pix1, L.IFF_BMP);
        Pix.Write("/tmp/lept/regout/alpha1.png", pix1, L.IFF_PNG);
        pix2 = Pix.Read("/tmp/lept/regout/alpha1.bmp");
        pix3 = Pix.Read("/tmp/lept/regout/alpha1.png");
        rp.CompareValues(1.0f, Pix.Equal(pix2, pix1) ? 1.0f : 0.0f, 0.0f);  /* 1 */
        rp.CompareValues(1.0f, Pix.Equal(pix3, pix1) ? 1.0f : 0.0f, 0.0f);  /* 2 */
        Pix.Write("/tmp/lept/regout/alpha2.bmp", pix2, L.IFF_BMP);
        pix4 = Pix.Read("/tmp/lept/regout/alpha2.bmp");
        rp.CompareValues(1.0f, Pix.Equal(pix4, pix1) ? 1.0f : 0.0f, 0.0f);  /* 3 */

        pix1 = Pix.Read(ProgFile("test-rgba.bmp"));
        pix2 = pix1.Convert32To24();
        pix3 = pix2.Convert24To32();
        rp.CompareValues(1.0f, Pix.Equal(pix3, pix1) ? 1.0f : 0.0f, 0.0f);  /* 4 */
        pix4 = pix1.DisplayDiff(pix3, true, 1, 0xff000000);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 5 */

        pix1 = Pix.Read(ProgFile("test-rgba.bmp"));
        pix2 = pix1.Convert32To24();
        Pix.Write("/tmp/lept/regout/alpha3.bmp", pix2, L.IFF_BMP);
        Pix.Write("/tmp/lept/regout/alpha3.png", pix2, L.IFF_PNG);
        pix3 = Pix.Read("/tmp/lept/regout/alpha3.bmp");
        pix4 = Pix.Read("/tmp/lept/regout/alpha3.png");
        rp.CompareValues(1.0f, Pix.Equal(pix3, pix1) ? 1.0f : 0.0f, 0.0f);  /* 6 */
        Pix pix5 = pix1.DisplayDiff(pix3, true, 1, 0xff000000);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 7 */
        rp.CompareValues(1.0f, Pix.Equal(pix4, pix1) ? 1.0f : 0.0f, 0.0f);  /* 8 */
        Pix pix6 = pix1.DisplayDiff(pix4, true, 1, 0xff000000);
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 9 */

            /* -------------- Part 8: Read header information -------------- */
        foreach ((string f, int fmt) in new[] {
            (FILE_1BPP, L.IFF_TIFF_G4),
            (FILE_2BPP, L.IFF_PNG),
            (FILE_2BPP_C, L.IFF_PNG),
            (FILE_4BPP, L.IFF_PNG),
            (FILE_4BPP_C, L.IFF_PNG),
            (FILE_8BPP_1, L.IFF_PNG),
            (FILE_8BPP_2, L.IFF_PNG),
            (FILE_8BPP_3, L.IFF_JFIF_JPEG),
            (FILE_GRAY_ALPHA, L.IFF_PNG),
            (FILE_16BPP, L.IFF_TIFF_ZIP),
            (FILE_32BPP, L.IFF_JFIF_JPEG),
            (FILE_32BPP_ALPHA, L.IFF_PNG),
        }) {
            if (GetHeaderData(ProgFile(f), fmt)) failure = true;
        }

        string tempname = Utils.LMakeTempFilename();
        try {
            Pix hpix = Pix.Read(ProgFile(FILE_8BPP_1));
            Pix.Write(tempname, hpix, L.IFF_PNM);
            if (GetHeaderData(tempname, L.IFF_PNM)) failure = true;

            hpix = Pix.Read(ProgFile(FILE_1BPP));
            foreach (int tfmt in new[] {
                L.IFF_TIFF_G3, L.IFF_TIFF_G4, L.IFF_TIFF_PACKBITS,
                L.IFF_TIFF_RLE, L.IFF_TIFF_LZW, L.IFF_TIFF_ZIP, L.IFF_TIFF
            }) {
                Pix.Write(tempname, hpix, tfmt);
                if (GetHeaderData(tempname, tfmt)) failure = true;
            }
        } finally {
            Utils.LeptRmfile(tempname);
        }

        Assert.False(failure, "At least one section in ioformats_reg failed");
        rp.Cleanup();
    }

    private static bool TestComp(string filename, Pix pix, int comptype)
    {
        using FileStream fp = File.OpenRead(Utils.GenPathname(filename, null));
        Pix.FindFileFormatStream(fp, out int format);
        Pix pixt = Pix.Read(filename);
        return format != comptype || !Pix.Equal(pix, pixt);
    }

    private static bool TestWriteMem(Pix pixs, int format, string? psfile)
    {
        if (format == L.IFF_PS) {
            byte[] psData = Pix.WriteMemPS(pixs, null, 0, 1.0f);
            Utils.BinaryWrite(psfile!, "w", psData);
            return false;
        }
        byte[] data = Pix.WriteMem(pixs, format);
        Pix pixd = Pix.ReadMem(data);
        if (format == L.IFF_JFIF_JPEG || format == L.IFF_JP2 ||
            format == L.IFF_WEBP || format == L.IFF_TIFF_JPEG) {
            float diff;
            if (pixd.Depth == 8)
                pixs.CompareGray(pixd, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
            else if (pixs.Depth == 32 && pixd.Depth == 32)
                pixs.CompareRGB(pixd, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
            else
                return false;
            return diff > 8.0f;
        }
        return !Pix.Equal(pixs, pixd);
    }

    private static Pix Make24BppPix(Pix pixs)
    {
        int w = pixs.Width, h = pixs.Height;
        Pix pixd = Pix.Create(w, h, 24);
        int wpls = pixs.Wpl, wpld = pixd.Wpl;
        Span<byte> dstBytes = MemoryMarshal.AsBytes(pixd.Data.AsSpan());
        for (int i = 0; i < h; i++) {
            int rowBase = 4 * i * wpld;
            for (int j = 0; j < w; j++) {
                Pix.ExtractRGBValues(pixs.Data[i * wpls + j], out int r, out int g, out int b);
                dstBytes[rowBase + 3 * j] = (byte)r;
                dstBytes[rowBase + 3 * j + 1] = (byte)g;
                dstBytes[rowBase + 3 * j + 2] = (byte)b;
            }
        }
        return pixd;
    }

    private static bool GetHeaderData(string filename, int trueFormat)
    {
        Pix.ReadHeader(filename, out int format1, out int w1, out int h1, out int bps1, out int spp1, out bool iscmap1);
        byte[] data = Utils.BinaryRead(filename);
        Pix.ReadHeaderMem(data, out int format2, out int w2, out int h2, out int bps2, out int spp2, out bool iscmap2);
        return format1 != trueFormat || format1 != format2 ||
               w1 != w2 || h1 != h2 || bps1 != bps2 || spp1 != spp2 || iscmap1 != iscmap2;
    }
}
