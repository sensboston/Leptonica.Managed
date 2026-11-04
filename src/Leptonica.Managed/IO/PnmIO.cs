using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  pnmio.c: Read and write PNM (P1..P6) and PAM (P7) files and memory.
 *
 *      Stream interface
 *          Pix.ReadStreamPnm          (pixReadStreamPnm)
 *          Pix.ReadHeaderPnm          (readHeaderPnm)
 *          Pix.FreadHeaderPnm         (freadHeaderPnm)
 *          Pix.WriteStreamPnm         (pixWriteStreamPnm)
 *          Pix.WriteStreamAsciiPnm    (pixWriteStreamAsciiPnm)
 *          Pix.WriteStreamPam         (pixWriteStreamPam)
 *
 *      Read/write to memory
 *          Pix.ReadMemPnm             (pixReadMemPnm)
 *          Pix.ReadHeaderMemPnm       (readHeaderMemPnm)
 *          Pix.WriteMemPnm            (pixWriteMemPnm)
 *          Pix.WriteMemPam            (pixWriteMemPam)
 *
 *      Local helpers
 *          static PnmReadNextAsciiValue  (pnmReadNextAsciiValue)
 *          static PnmReadNextNumber      (pnmReadNextNumber)
 *          static PnmReadNextString      (pnmReadNextString)
 *          static PnmSkipCommentLines    (pnmSkipCommentLines)
 */

public sealed partial class Pix
{
    /* a sanity check on the size read from file */
    private const int MAX_PNM_WIDTH = 100000;
    private const int MAX_PNM_HEIGHT = 100000;

    /// <summary>
    /// Byte reader with single-byte pushback so fscanf-style lookahead works
    /// identically on seekable and non-seekable streams.
    /// </summary>
    private sealed class PnmByteReader
    {
        public const int Eof = -1;
        private const int None = -2;

        private readonly Stream stream;
        private int pushed = None;

        public PnmByteReader(Stream stream)
        {
            this.stream = stream;
        }

        public static bool IsSpace(int c) => c == ' ' || (c >= '\t' && c <= '\r');

        public int ReadByte()
        {
            if (pushed != None) {
                int c = pushed;
                pushed = None;
                return c;
            }
            return stream.ReadByte();
        }

        public void Unget(int c)
        {
            if (c != Eof)
                pushed = c;
        }

        public void Release()
        {
            if (pushed != None && stream.CanSeek)
                stream.Seek(-1, SeekOrigin.Current);
            pushed = None;
        }

        public bool TryReadUInt8(out byte val)
        {
            int b = ReadByte();
            if (b == Eof) {
                val = 0;
                return false;
            }
            val = (byte)b;
            return true;
        }

        public bool TryReadUInt16LE(out ushort val)
        {
            int b0 = ReadByte();
            int b1 = b0 == Eof ? Eof : ReadByte();
            if (b1 == Eof) {
                val = 0;
                return false;
            }
            val = (ushort)(b0 | (b1 << 8));
            return true;
        }

        public bool ScanInt(out int result)
        {
            result = 0;
            int c;
            do {
                c = ReadByte();
            } while (IsSpace(c));
            if (c == Eof)
                return false;
            bool neg = false;
            if (c == '+' || c == '-') {
                neg = c == '-';
                c = ReadByte();
            }
            if (c < '0' || c > '9') {
                Unget(c);
                return false;
            }
            long val = 0;
            while (c >= '0' && c <= '9') {
                val = val * 10 + (c - '0');
                c = ReadByte();
            }
            Unget(c);
            result = (int)(neg ? -val : val);
            return true;
        }

        public void SkipSpaces()
        {
            int c;
            do {
                c = ReadByte();
            } while (IsSpace(c));
            Unget(c);
        }
    }

    /*--------------------------------------------------------------------*
     *                          Stream interface                          *
     *--------------------------------------------------------------------*/
    // leptonica: pixReadStreamPnm (pnmio.c)
    /// <summary>Reads a PNM or PAM image from the stream.</summary>
    /// <param name="fp">file stream opened for read</param>
    /// <returns>pix</returns>
    public static Pix ReadStreamPnm(Stream fp)
    {
        const string func = "pixReadStreamPnm";
        byte val8, rval8, gval8, bval8, aval8, mask8;
        ushort val16, rval16, gval16, bval16, aval16;
        int w, h, d, bps, spp, bpl, wpl, i, j, type;
        int val, rval, gval, bval;
        uint rgbval;
        Pix pix;

        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        var r = new PnmByteReader(fp);
        if (!FreadHeaderPnmInternal(r, out w, out h, out d, out type, out bps, out spp)) {
            r.Release();
            throw Lept.Fail(func, "header read failed");
        }
        if (bps < 1 || bps > 16) {
            r.Release();
            throw Lept.Fail(func, "invalid bps");
        }
        if (spp < 1 || spp > 4) {
            r.Release();
            throw Lept.Fail(func, "invalid spp");
        }
        pix = Create(w, h, d);
        pix.InputFormat = L.IFF_PNM;
        uint[] data = pix.Data;
        wpl = pix.Wpl;

            /* If type == 6 and bps == 16, we use the code in type 7
             * to read 6 bytes/pixel from the input file. */
        if (type == 6 && bps == 16)
            type = 7;

        switch (type) {
        case 1:
        case 2:
            /* Old "ASCII" binary or gray format */
            for (i = 0; i < h; i++) {
                for (j = 0; j < w; j++) {
                    if (PnmReadNextAsciiValue(r, out val) != 0)
                        throw Lept.Fail(func, "read abend");
                    pix.SetPixel(j, i, (uint)val);
                }
            }
            break;

        case 3:
            /* Old "ASCII" rgb format */
            for (i = 0; i < h; i++) {
                for (j = 0; j < w; j++) {
                    if (PnmReadNextAsciiValue(r, out rval) != 0)
                        throw Lept.Fail(func, "read abend");
                    if (PnmReadNextAsciiValue(r, out gval) != 0)
                        throw Lept.Fail(func, "read abend");
                    if (PnmReadNextAsciiValue(r, out bval) != 0)
                        throw Lept.Fail(func, "read abend");
                    ComposeRGBPixel(rval, gval, bval, out rgbval);
                    pix.SetPixel(j, i, rgbval);
                }
            }
            break;

        case 4:
            /* "raw" format for 1 bpp */
            bpl = (d * w + 7) / 8;
            for (i = 0; i < h; i++) {
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                for (j = 0; j < bpl; j++) {
                    if (!r.TryReadUInt8(out val8))
                        throw Lept.Fail(func, "read error in 4");
                    SetDataByte(line, j, val8);
                }
            }
            break;

        case 5:
            /* "raw" format for grayscale */
            for (i = 0; i < h; i++) {
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                if (d != 16) {
                    for (j = 0; j < w; j++) {
                        if (!r.TryReadUInt8(out val8))
                            throw Lept.Fail(func, "error in 5");
                        if (d == 2)
                            SetDataDibit(line, j, val8);
                        else if (d == 4)
                            SetDataQbit(line, j, val8);
                        else  /* d == 8 */
                            SetDataByte(line, j, val8);
                    }
                } else {  /* d == 16 */
                    for (j = 0; j < w; j++) {
                        if (!r.TryReadUInt16LE(out val16))
                            throw Lept.Fail(func, "16 bpp error");
                        SetDataTwoBytes(line, j, val16);
                    }
                }
            }
            break;

        case 6:
            /* "raw" format, type == 6; 8 bps, rgb */
            for (i = 0; i < h; i++) {
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                for (j = 0; j < wpl; j++) {
                    if (!r.TryReadUInt8(out rval8))
                        throw Lept.Fail(func, "read error type 6");
                    if (!r.TryReadUInt8(out gval8))
                        throw Lept.Fail(func, "read error type 6");
                    if (!r.TryReadUInt8(out bval8))
                        throw Lept.Fail(func, "read error type 6");
                    ComposeRGBPixel(rval8, gval8, bval8, out rgbval);
                    line[j] = rgbval;
                }
            }
            break;

        case 7:
            /* "arbitrary" format; type == 7; */
            if (bps != 16) {
                mask8 = (byte)((1 << bps) - 1);
                switch (spp) {
                case 1: /* 1, 2, 4, 8 bpp grayscale */
                    for (i = 0; i < h; i++) {
                        for (j = 0; j < w; j++) {
                            if (!r.TryReadUInt8(out val8))
                                throw Lept.Fail(func, "read error type 7");
                            val8 = (byte)(val8 & mask8);
                            if (bps == 1) val8 ^= 1;  /* white-is-1 photometry */
                            pix.SetPixel(j, i, val8);
                        }
                    }
                    break;

                case 2: /* 1, 2, 4, 8 bpp grayscale + alpha */
                    for (i = 0; i < h; i++) {
                        for (j = 0; j < w; j++) {
                            if (!r.TryReadUInt8(out val8))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt8(out aval8))
                                throw Lept.Fail(func, "read error type 7");
                            val8 = (byte)(val8 & mask8);
                            aval8 = (byte)(aval8 & mask8);
                            ComposeRGBAPixel(val8, val8, val8, aval8, out rgbval);
                            pix.SetPixel(j, i, rgbval);
                        }
                    }
                    pix.Spp = 4;
                    break;

                case 3: /* rgb */
                    for (i = 0; i < h; i++) {
                        Span<uint> line = data.AsSpan(i * wpl, wpl);
                        for (j = 0; j < wpl; j++) {
                            if (!r.TryReadUInt8(out rval8))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt8(out gval8))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt8(out bval8))
                                throw Lept.Fail(func, "read error type 7");
                            rval8 = (byte)(rval8 & mask8);
                            gval8 = (byte)(gval8 & mask8);
                            bval8 = (byte)(bval8 & mask8);
                            ComposeRGBPixel(rval8, gval8, bval8, out rgbval);
                            line[j] = rgbval;
                        }
                    }
                    break;

                case 4: /* rgba */
                    for (i = 0; i < h; i++) {
                        Span<uint> line = data.AsSpan(i * wpl, wpl);
                        for (j = 0; j < wpl; j++) {
                            if (!r.TryReadUInt8(out rval8))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt8(out gval8))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt8(out bval8))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt8(out aval8))
                                throw Lept.Fail(func, "read error type 7");
                            rval8 = (byte)(rval8 & mask8);
                            gval8 = (byte)(gval8 & mask8);
                            bval8 = (byte)(bval8 & mask8);
                            aval8 = (byte)(aval8 & mask8);
                            ComposeRGBAPixel(rval8, gval8, bval8, aval8, out rgbval);
                            line[j] = rgbval;
                        }
                    }
                    pix.Spp = 4;
                    break;
                }
            } else {  /* bps == 16 */
                    /* I have only seen one example that is type 6, 16 bps.
                     * It was 3 spp (rgb), and the 8 bps of real data was stored
                     * in the second byte.  In the following, I make the wild
                     * assumption that for all 16 bpp pnm/pam files, we can
                     * take the second byte. */
                switch (spp) {
                case 1: /* 16 bps grayscale */
                    for (i = 0; i < h; i++) {
                        for (j = 0; j < w; j++) {
                            if (!r.TryReadUInt16LE(out val16))
                                throw Lept.Fail(func, "read error type 7");
                            val8 = (byte)(val16 & 0xff);
                            pix.SetPixel(j, i, val8);
                        }
                    }
                    break;

                case 2: /* 16 bps grayscale + alpha */
                    for (i = 0; i < h; i++) {
                        for (j = 0; j < w; j++) {
                            if (!r.TryReadUInt16LE(out val16))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt16LE(out aval16))
                                throw Lept.Fail(func, "read error type 7");
                            val8 = (byte)(val16 & 0xff);
                            aval8 = (byte)(aval16 & 0xff);
                            ComposeRGBAPixel(val8, val8, val8, aval8, out rgbval);
                            pix.SetPixel(j, i, rgbval);
                        }
                    }
                    pix.Spp = 4;
                    break;

                case 3: /* 16bps rgb */
                    for (i = 0; i < h; i++) {
                        Span<uint> line = data.AsSpan(i * wpl, wpl);
                        for (j = 0; j < wpl; j++) {
                            if (!r.TryReadUInt16LE(out rval16))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt16LE(out gval16))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt16LE(out bval16))
                                throw Lept.Fail(func, "read error type 7");
                            rval8 = (byte)(rval16 & 0xff);
                            gval8 = (byte)(gval16 & 0xff);
                            bval8 = (byte)(bval16 & 0xff);
                            ComposeRGBPixel(rval8, gval8, bval8, out rgbval);
                            line[j] = rgbval;
                        }
                    }
                    break;

                case 4: /* 16bps rgba */
                    for (i = 0; i < h; i++) {
                        Span<uint> line = data.AsSpan(i * wpl, wpl);
                        for (j = 0; j < wpl; j++) {
                            if (!r.TryReadUInt16LE(out rval16))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt16LE(out gval16))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt16LE(out bval16))
                                throw Lept.Fail(func, "read error type 7");
                            if (!r.TryReadUInt16LE(out aval16))
                                throw Lept.Fail(func, "read error type 7");
                            rval8 = (byte)(rval16 & 0xff);
                            gval8 = (byte)(gval16 & 0xff);
                            bval8 = (byte)(bval16 & 0xff);
                            aval8 = (byte)(aval16 & 0xff);
                            ComposeRGBAPixel(rval8, gval8, bval8, aval8, out rgbval);
                            line[j] = rgbval;
                        }
                    }
                    pix.Spp = 4;
                    break;
                }
            }
            break;
        }
        r.Release();
        return pix;
    }

    // leptonica: readHeaderPnm (pnmio.c)
    /// <summary>Reads PNM/PAM header metadata from a file.</summary>
    public static void ReadHeaderPnm(string filename, out int pw, out int ph, out int pd,
                                     out int ptype, out int pbps, out int pspp)
    {
        pw = ph = pd = ptype = pbps = pspp = 0;
        if (filename == null)
            throw Lept.Arg("readHeaderPnm", "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        FreadHeaderPnm(fp, out pw, out ph, out pd, out ptype, out pbps, out pspp);
    }

    // leptonica: freadHeaderPnm (pnmio.c)
    /// <summary>Reads PNM/PAM header metadata from a stream.</summary>
    public static void FreadHeaderPnm(Stream fp, out int pw, out int ph, out int pd,
                                      out int ptype, out int pbps, out int pspp)
    {
        pw = ph = pd = ptype = pbps = pspp = 0;
        if (fp == null)
            throw Lept.Arg("freadHeaderPnm", "fp not defined");

        var r = new PnmByteReader(fp);
        bool ok = FreadHeaderPnmInternal(r, out pw, out ph, out pd, out ptype, out pbps, out pspp);
        r.Release();
        if (!ok)
            throw Lept.Fail("freadHeaderPnm", "header read failed");
    }

    private static bool FreadHeaderPnmInternal(PnmByteReader r, out int pw, out int ph, out int pd,
                                               out int ptype, out int pbps, out int pspp)
    {
        const string func = "freadHeaderPnm";
        int i, w, h, d, bps, spp, type;
        int maxval;
        int ch;

        pw = ph = pd = ptype = pbps = pspp = 0;

            /* fscanf(fp, "P%d\n", &type) */
        int first = r.ReadByte();
        if (first != 'P' || !r.ScanInt(out type)) {
            Log.Error(func, "invalid read for type");
            return false;
        }
        r.SkipSpaces();
        if (type < 1 || type > 7) {
            Log.Error(func, "invalid pnm file");
            return false;
        }

        if (PnmSkipCommentLines(r) != 0) {
            Log.Error(func, "no data in file");
            return false;
        }

        if (type == 7) {
            w = h = d = bps = spp = maxval = 0;
            for (i = 0; i < 10; i++) {   /* limit to 10 lines of this header */
                if (PnmReadNextString(r, out string tag, 16) != 0) {
                    Log.Error(func, "found no next tag");
                    return false;
                }
                if (tag == "WIDTH") {
                    if (PnmReadNextNumber(r, out w) != 0) {
                        Log.Error(func, "failed reading width");
                        return false;
                    }
                    continue;
                }
                if (tag == "HEIGHT") {
                    if (PnmReadNextNumber(r, out h) != 0) {
                        Log.Error(func, "failed reading height");
                        return false;
                    }
                    continue;
                }
                if (tag == "DEPTH") {
                    if (PnmReadNextNumber(r, out spp) != 0) {
                        Log.Error(func, "failed reading depth");
                        return false;
                    }
                    continue;
                }
                if (tag == "MAXVAL") {
                    if (PnmReadNextNumber(r, out maxval) != 0) {
                        Log.Error(func, "failed reading maxval");
                        return false;
                    }
                    continue;
                }
                if (tag == "TUPLTYPE") {
                    if (PnmReadNextString(r, out _, 32) != 0) {
                        Log.Error(func, "failed reading tuple type");
                        return false;
                    }
                    continue;
                }
                if (tag == "ENDHDR") {
                    if ('\n' != (ch = r.ReadByte())) {
                        Log.Error(func, "missing LF after ENDHDR");
                        return false;
                    }
                    break;
                }
            }
            if (w <= 0 || h <= 0 || w > MAX_PNM_WIDTH || h > MAX_PNM_HEIGHT) {
                Log.Info(func, $"invalid size: w = {w}, h = {h}\n");
                return false;
            }
            if (maxval == 1) {
                d = bps = 1;
            } else if (maxval == 3) {
                d = bps = 2;
            } else if (maxval == 15) {
                d = bps = 4;
            } else if (maxval == 255) {
                d = bps = 8;
            } else if (maxval == 0xffff) {
                d = bps = 16;
            } else {
                Log.Info(func, $"invalid maxval = {maxval}\n");
                return false;
            }
            switch (spp) {
            case 1:
                /* d and bps are already set */
                break;
            case 2:
            case 3:
            case 4:
                /* create a 32 bpp Pix */
                d = 32;
                break;
            default:
                Log.Info(func, $"invalid depth = {spp}\n");
                return false;
            }
        } else {
                /* fscanf(fp, "%d %d\n", &w, &h) */
            if (!r.ScanInt(out w) || !r.ScanInt(out h)) {
                Log.Error(func, "invalid read for w,h");
                return false;
            }
            r.SkipSpaces();
            if (w <= 0 || h <= 0 || w > MAX_PNM_WIDTH || h > MAX_PNM_HEIGHT) {
                Log.Info(func, $"invalid size: w = {w}, h = {h}\n");
                return false;
            }

                /* Get depth of pix.  For types 2 and 5, we use the maxval. */
            if (type == 1 || type == 4) {
                d = 1;
                spp = 1;
                bps = 1;
            } else if (type == 2 || type == 5) {
                if (PnmReadNextNumber(r, out maxval) != 0) {
                    Log.Error(func, "invalid read for maxval (2,5)");
                    return false;
                }
                if (maxval == 3) {
                    d = 2;
                } else if (maxval == 15) {
                    d = 4;
                } else if (maxval == 255) {
                    d = 8;
                } else if (maxval == 0xffff) {
                    d = 16;
                } else {
                    Utils.LeptStderr("maxval = %d\n", maxval);
                    Log.Error(func, "invalid maxval");
                    return false;
                }
                bps = d;
                spp = 1;
            } else {  /* type == 3 || type == 6; this is rgb */
                if (PnmReadNextNumber(r, out maxval) != 0) {
                    Log.Error(func, "invalid read for maxval (3,6)");
                    return false;
                }
                if (maxval != 255 && maxval != 0xffff) {
                    Log.Error(func, $"unexpected maxval = {maxval}\n");
                    return false;
                }
                bps = (maxval == 255) ? 8 : 16;
                d = 32;
                spp = 3;
            }
        }
        pw = w;
        ph = h;
        pd = d;
        ptype = type;
        pbps = bps;
        pspp = spp;
        return true;
    }

    // leptonica: pixWriteStreamPnm (pnmio.c)
    /// <summary>Writes the pix in packed ("raw") PNM/PAM format to the stream.</summary>
    /// <remarks>
    /// (1) This writes "raw" packed format only:
    ///     1 bpp --&gt; pbm (P4)
    ///     2, 4, 8, 16 bpp, no colormap or grayscale colormap --&gt; pgm (P5)
    ///     2, 4, 8 bpp with color-valued colormap, or rgb --&gt; rgb ppm (P6)
    ///     32 bpp with spp == 4 --&gt; pam (P7)
    /// (2) 24 bpp rgb are not supported in leptonica, but this will
    ///     write them out as a packed array of bytes (3 to a pixel).
    /// </remarks>
    public static void WriteStreamPnm(Stream fp, Pix pix)
    {
        const string func = "pixWriteStreamPnm";
        byte val8;
        Span<byte> pel = stackalloc byte[4];
        ushort val16;
        int h, w, d, ds, i, j, wpls, bpl, filebpl, maxval;
        Pix pixs;

        if (fp == null)
            throw Lept.Arg(func, "fp not defined");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        pix.GetDimensions(out w, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 24 && d != 32)
            throw Lept.Arg(func, "d not in {1,2,4,8,16,24,32}");
        if (d == 32 && pix.Spp == 4) {
            WriteStreamPam(fp, pix);
            return;
        }

            /* If a colormap exists, remove and convert to grayscale or rgb */
        if (pix.Colormap != null)
            pixs = pix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        else
            pixs = Clone(pix);
        ds = pixs.Depth;
        uint[] datas = pixs.Data;
        wpls = pixs.Wpl;

        if (ds == 1) {  /* binary */
            CStdio.Fprintf(fp, $"P4\n# Raw PBM file written by leptonica (www.leptonica.com)\n{w} {h}\n");

            bpl = (w + 7) / 8;
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (j = 0; j < bpl; j++) {
                    val8 = (byte)GetDataByte(lines, j);
                    fp.WriteByte(val8);
                }
            }
        } else if (ds == 2 || ds == 4 || ds == 8 || ds == 16) {  /* grayscale */
            maxval = (1 << ds) - 1;
            CStdio.Fprintf(fp, $"P5\n# Raw PGM file written by leptonica (www.leptonica.com)\n{w} {h}\n{maxval}\n");

            if (ds != 16) {
                for (i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    for (j = 0; j < w; j++) {
                        if (ds == 2)
                            val8 = (byte)GetDataDibit(lines, j);
                        else if (ds == 4)
                            val8 = (byte)GetDataQbit(lines, j);
                        else  /* ds == 8 */
                            val8 = (byte)GetDataByte(lines, j);
                        fp.WriteByte(val8);
                    }
                }
            } else {  /* ds == 16 */
                Span<byte> buf2 = stackalloc byte[2];
                for (i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    for (j = 0; j < w; j++) {
                        val16 = (ushort)GetDataTwoBytes(lines, j);
                        BinaryPrimitives.WriteUInt16LittleEndian(buf2, val16);
                        fp.Write(buf2);
                    }
                }
            }
        } else {  /* rgb color */
            CStdio.Fprintf(fp, $"P6\n# Raw PPM file written by leptonica (www.leptonica.com)\n{w} {h}\n255\n");

            if (d == 24) {   /* packed, 3 bytes to a pixel */
                filebpl = 3 * w;
                for (i = 0; i < h; i++) {  /* write out each raster line */
                    ReadOnlySpan<byte> lineBytes = MemoryMarshal.AsBytes(datas.AsSpan(i * wpls, wpls));
                    fp.Write(lineBytes.Slice(0, filebpl));
                }
            } else {  /* 32 bpp rgb */
                for (i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    for (j = 0; j < wpls; j++) {
                        uint word = lines[j];
                        pel[0] = (byte)GetDataByte(word, L.COLOR_RED);
                        pel[1] = (byte)GetDataByte(word, L.COLOR_GREEN);
                        pel[2] = (byte)GetDataByte(word, L.COLOR_BLUE);
                        fp.Write(pel.Slice(0, 3));
                    }
                }
            }
        }
    }

    // leptonica: pixWriteStreamAsciiPnm (pnmio.c)
    /// <summary>Writes the pix in ASCII PNM (P1, P2, P3) format to the stream.</summary>
    public static void WriteStreamAsciiPnm(Stream fp, Pix pix)
    {
        const string func = "pixWriteStreamAsciiPnm";
        Span<byte> cval = stackalloc byte[3];
        int h, w, d, ds, i, j, k, maxval, count;
        uint val;
        Pix pixs;

        if (fp == null)
            throw Lept.Arg(func, "fp not defined");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        pix.GetDimensions(out w, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "d not in {1,2,4,8,16,32}");

            /* If a colormap exists, remove and convert to grayscale or rgb */
        if (pix.Colormap != null)
            pixs = pix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        else
            pixs = Clone(pix);
        ds = pixs.Depth;

        if (ds == 1) {  /* binary */
            CStdio.Fprintf(fp, $"P1\n# Ascii PBM file written by leptonica (www.leptonica.com)\n{w} {h}\n");

            count = 0;
            for (i = 0; i < h; i++) {
                for (j = 0; j < w; j++) {
                    pixs.GetPixel(j, i, out val);
                    if (val == 0)
                        fp.WriteByte((byte)'0');
                    else  /* val == 1 */
                        fp.WriteByte((byte)'1');
                    fp.WriteByte((byte)' ');
                    count += 2;
                    if (count >= 70) {
                        fp.WriteByte((byte)'\n');
                        count = 0;
                    }
                }
            }
        } else if (ds == 2 || ds == 4 || ds == 8 || ds == 16) {  /* grayscale */
            maxval = (1 << ds) - 1;
            CStdio.Fprintf(fp, $"P2\n# Ascii PGM file written by leptonica (www.leptonica.com)\n{w} {h}\n{maxval}\n");

            count = 0;
            for (i = 0; i < h; i++) {
                for (j = 0; j < w; j++) {
                    pixs.GetPixel(j, i, out val);
                    string s;
                    int len;
                    if (ds == 2) {
                        s = CFormat.Sprintf("%1d ", (int)val);
                        len = 2;
                    } else if (ds == 4) {
                        s = CFormat.Sprintf("%2d ", (int)val);
                        len = 3;
                    } else if (ds == 8) {
                        s = CFormat.Sprintf("%3d ", (int)val);
                        len = 4;
                    } else {  /* ds == 16 */
                        s = CFormat.Sprintf("%5d ", (int)val);
                        len = 6;
                    }
                    WriteAsciiPrefix(fp, s, len);
                    count += len;
                    if (count >= 60) {
                        fp.WriteByte((byte)'\n');
                        count = 0;
                    }
                }
            }
        } else {  /* rgb color */
            CStdio.Fprintf(fp, $"P3\n# Ascii PPM file written by leptonica (www.leptonica.com)\n{w} {h}\n255\n");
            count = 0;
            for (i = 0; i < h; i++) {
                for (j = 0; j < w; j++) {
                    pixs.GetPixel(j, i, out val);
                    cval[0] = (byte)GetDataByte(val, L.COLOR_RED);
                    cval[1] = (byte)GetDataByte(val, L.COLOR_GREEN);
                    cval[2] = (byte)GetDataByte(val, L.COLOR_BLUE);
                    for (k = 0; k < 3; k++) {
                        string s = CFormat.Sprintf("%3d ", (int)cval[k]);
                        WriteAsciiPrefix(fp, s, 4);
                        count += 4;
                        if (count >= 60) {
                            fp.WriteByte((byte)'\n');
                            count = 0;
                        }
                    }
                }
            }
        }
    }

    private static void WriteAsciiPrefix(Stream fp, string s, int len)
    {
        Span<byte> buf = stackalloc byte[8];
        int n = Math.Min(len, s.Length);
        for (int i = 0; i < n; i++)
            buf[i] = (byte)s[i];
        for (int i = n; i < len; i++)
            buf[i] = 0;
        fp.Write(buf.Slice(0, len));
    }

    // leptonica: pixWriteStreamPam (pnmio.c)
    /// <summary>Writes the pix in arbitrary PAM (P7) packed format to the stream.</summary>
    public static void WriteStreamPam(Stream fp, Pix pix)
    {
        const string func = "pixWriteStreamPam";
        byte val8;
        Span<byte> pel = stackalloc byte[8];
        ushort val16;
        int h, w, d, ds, i, j;
        int wpls, spps, filebpl, maxval;
        Pix pixs;

        if (fp == null)
            throw Lept.Arg(func, "fp not defined");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        pix.GetDimensions(out w, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 24 && d != 32)
            throw Lept.Arg(func, "d not in {1,2,4,8,16,24,32}");

            /* If a colormap exists, remove and convert to grayscale or rgb */
        if (pix.Colormap != null)
            pixs = pix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        else
            pixs = Clone(pix);
        ds = pixs.Depth;
        uint[] datas = pixs.Data;
        wpls = pixs.Wpl;
        spps = pixs.Spp;
        if (ds < 24)
            maxval = (1 << ds) - 1;
        else
            maxval = 255;

        CStdio.Fprintf(fp, $"P7\n# Arbitrary PAM file written by leptonica (www.leptonica.com)\n");
        CStdio.Fprintf(fp, $"WIDTH {w}\n");
        CStdio.Fprintf(fp, $"HEIGHT {h}\n");
        CStdio.Fprintf(fp, $"DEPTH {spps}\n");
        CStdio.Fprintf(fp, $"MAXVAL {maxval}\n");
        if (spps == 1 && ds == 1)
            CStdio.Fprintf(fp, $"TUPLTYPE BLACKANDWHITE\n");
        else if (spps == 1)
            CStdio.Fprintf(fp, $"TUPLTYPE GRAYSCALE\n");
        else if (spps == 3)
            CStdio.Fprintf(fp, $"TUPLTYPE RGB\n");
        else if (spps == 4)
            CStdio.Fprintf(fp, $"TUPLTYPE RGB_ALPHA\n");
        CStdio.Fprintf(fp, $"ENDHDR\n");

        // NOTE(upstream): switches on d (original depth) instead of ds (after colormap removal);
        // see docs/UPSTREAM_NOTES.md.
        switch (d) {
        case 1:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (j = 0; j < w; j++) {
                    val8 = (byte)GetDataBit(lines, j);
                    val8 ^= 1;  /* pam apparently uses white-is-1 photometry */
                    fp.WriteByte(val8);
                }
            }
            break;

        case 2:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (j = 0; j < w; j++) {
                    val8 = (byte)GetDataDibit(lines, j);
                    fp.WriteByte(val8);
                }
            }
            break;

        case 4:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (j = 0; j < w; j++) {
                    val8 = (byte)GetDataQbit(lines, j);
                    fp.WriteByte(val8);
                }
            }
            break;

        case 8:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (j = 0; j < w; j++) {
                    val8 = (byte)GetDataByte(lines, j);
                    fp.WriteByte(val8);
                }
            }
            break;

        case 16: {
            Span<byte> buf2 = stackalloc byte[2];
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (j = 0; j < w; j++) {
                    val16 = (ushort)GetDataTwoBytes(lines, j);
                    BinaryPrimitives.WriteUInt16LittleEndian(buf2, val16);
                    fp.Write(buf2);
                }
            }
            break;
        }

        case 24:
            filebpl = 3 * w;
            for (i = 0; i < h; i++) {
                ReadOnlySpan<byte> lineBytes = MemoryMarshal.AsBytes(datas.AsSpan(i * wpls, wpls));
                fp.Write(lineBytes.Slice(0, filebpl));
            }
            break;

        case 32:
            switch (spps) {
            case 3:
                for (i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    for (j = 0; j < wpls; j++) {
                        uint word = lines[j];
                        pel[0] = (byte)GetDataByte(word, L.COLOR_RED);
                        pel[1] = (byte)GetDataByte(word, L.COLOR_GREEN);
                        pel[2] = (byte)GetDataByte(word, L.COLOR_BLUE);
                        fp.Write(pel.Slice(0, 3));
                    }
                }
                break;
            case 4:
                for (i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    for (j = 0; j < wpls; j++) {
                        uint word = lines[j];
                        pel[0] = (byte)GetDataByte(word, L.COLOR_RED);
                        pel[1] = (byte)GetDataByte(word, L.COLOR_GREEN);
                        pel[2] = (byte)GetDataByte(word, L.COLOR_BLUE);
                        pel[3] = (byte)GetDataByte(word, L.L_ALPHA_CHANNEL);
                        fp.Write(pel.Slice(0, 4));
                    }
                }
                break;
            }
            break;
        }
    }

    /*---------------------------------------------------------------------*
     *                         Read/write to memory                        *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadMemPnm (pnmio.c)
    /// <summary>Reads a PNM/PAM image from memory.</summary>
    public static Pix ReadMemPnm(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStreamPnm(fp);
        } catch {
            Log.Error("pixReadMemPnm", "pix not read\n");
            throw;
        }
    }

    // leptonica: readHeaderMemPnm (pnmio.c)
    /// <summary>Reads PNM/PAM header metadata from memory.</summary>
    public static void ReadHeaderMemPnm(ReadOnlySpan<byte> data, out int pw, out int ph, out int pd,
                                        out int ptype, out int pbps, out int pspp)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            FreadHeaderPnm(fp, out pw, out ph, out pd, out ptype, out pbps, out pspp);
        } catch (Exception ex) when (ex is LeptonicaException or ArgumentException) {
            throw Lept.Fail("readHeaderMemPnm", "header data read failed");
        }
    }

    // leptonica: pixWriteMemPnm (pnmio.c)
    /// <summary>Writes the pix in packed PNM/PAM format to memory.</summary>
    public static byte[] WriteMemPnm(Pix pix)
    {
        if (pix == null)
            throw Lept.Arg("pixWriteMemPnm", "&pix not defined");

        using MemoryStream fp = new MemoryStream();
        WriteStreamPnm(fp, pix);
        return fp.ToArray();
    }

    // leptonica: pixWriteMemPam (pnmio.c)
    /// <summary>Writes the pix in PAM (P7) format to memory.</summary>
    public static byte[] WriteMemPam(Pix pix)
    {
        if (pix == null)
            throw Lept.Arg("pixWriteMemPam", "&pix not defined");

        using MemoryStream fp = new MemoryStream();
        WriteStreamPam(fp, pix);
        return fp.ToArray();
    }

    /*--------------------------------------------------------------------*
     *                          Static helpers                            *
     *--------------------------------------------------------------------*/
    // leptonica: pnmReadNextAsciiValue (pnmio.c)
    private static int PnmReadNextAsciiValue(PnmByteReader r, out int pval)
    {
        return r.ScanInt(out pval) ? 0 : 1;
    }

    // leptonica: pnmReadNextNumber (pnmio.c)
    private static int PnmReadNextNumber(PnmByteReader r, out int pval)
    {
        const string func = "pnmReadNextNumber";
        int i, c;
        bool foundws;

        pval = 0;

            /* Swallow whitespace */
        int ws;
        do {
            ws = r.ReadByte();
        } while (PnmByteReader.IsSpace(ws));
        if (ws == PnmByteReader.Eof) {
            Log.Error(func, "end of file reached");
            return 1;
        }
        r.Unget(ws);

            /* The ASCII characters for the number are followed by exactly
             * one whitespace character. */
        foundws = false;
        long acc = 0;
        int ndigits = 0;
        for (i = 0; i < 8; i++) {
            if ((c = r.ReadByte()) == PnmByteReader.Eof) {
                Log.Error(func, "end of file reached");
                return 1;
            }
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') {
                foundws = true;
                break;
            }
            if (c < '0' || c > '9') {
                Log.Error(func, "char read is not a digit");
                return 1;
            }
            acc = acc * 10 + (c - '0');
            ndigits++;
        }
        if (!foundws) {
            Log.Error(func, "no whitespace found");
            return 1;
        }
        if (ndigits == 0) {
            Log.Error(func, "invalid read");
            return 1;
        }
        pval = (int)acc;
        return 0;
    }

    // leptonica: pnmReadNextString (pnmio.c)
    private static int PnmReadNextString(PnmByteReader r, out string buff, int size)
    {
        const string func = "pnmReadNextString";
        buff = "";
        if (size > 10000) {
            Log.Error(func, "size is too big");
            return 1;
        }
        if (size <= 0) {
            Log.Error(func, "size is too small");
            return 1;
        }

            /* Skip whitespace */
        int c;
        do {
            c = r.ReadByte();
        } while (PnmByteReader.IsSpace(c));
        if (c == PnmByteReader.Eof)
            return 1;
        r.Unget(c);

            /* Comment lines are allowed to appear anywhere in the header lines */
        if (PnmSkipCommentLines(r) != 0) {
            Log.Error(func, "end of file reached");
            return 1;
        }

        do {
            c = r.ReadByte();
        } while (PnmByteReader.IsSpace(c));
        if (c == PnmByteReader.Eof)
            return 1;

        var sb = new StringBuilder();
        int maxChars = size - 1;
        while (c != PnmByteReader.Eof && !PnmByteReader.IsSpace(c) && sb.Length < maxChars) {
            sb.Append((char)c);
            if (sb.Length == maxChars)
                break;
            c = r.ReadByte();
            if (c == PnmByteReader.Eof || PnmByteReader.IsSpace(c)) {
                r.Unget(c);
                break;
            }
        }
        buff = sb.ToString();
        return 0;
    }

    // leptonica: pnmSkipCommentLines (pnmio.c)
    private static int PnmSkipCommentLines(PnmByteReader r)
    {
        while (true) {
            int c0 = r.ReadByte();
            if (c0 == PnmByteReader.Eof)
                return 1;
            if (c0 != '#') {
                r.Unget(c0);
                return 0;
            }
            int c = r.ReadByte();
            if (c == PnmByteReader.Eof)
                return 1;
            while (c != '\n') {
                c = r.ReadByte();
                if (c == PnmByteReader.Eof)
                    return 1;
            }
        }
    }
}
