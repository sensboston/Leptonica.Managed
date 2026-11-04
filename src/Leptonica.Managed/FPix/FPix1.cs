using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Leptonica;

/*
 *  fpix1.c: FPix, FPixa and DPix utilities:
 *         - creation and destruction
 *         - accessors
 *         - serialization and deserialization
 *
 *  Differences from C:
 *    - No reference counting and no fpixDestroy(), fpixaDestroy() and
 *      dpixDestroy(): the GC owns the objects (not ported).
 *      fpixClone(), dpixClone() and fpixaCopy(L_CLONE) return the same
 *      reference.
 *    - The data of FPix is a float[] and the data of DPix a double[], with
 *      wpl in elements, exactly as the C arrays.
 *    - FILE* becomes Stream.  Header text is written as ASCII with "\n" line
 *      ends, byte for byte as the C fprintf calls write it; the float data
 *      is written in little-endian order.
 *    - The *Mem variants take a ReadOnlySpan&lt;byte&gt; and return a byte[]
 *      (without the terminating NUL that C appends to its buffer).
 *    - Reading from a stream that cannot seek reads the whole stream, because
 *      the C scanning code pushes back one character (ungetc).
 *    - fopenReadStream() and friends are replaced by System.IO.
 *    - Functions that return an error code or NULL in C throw instead.
 *      fpixGetPixel() and the other pixel accessors return false (C: 2) for
 *      a point outside the image, without logging.
 *    - The byte swap functions assume a little-endian machine, as the C
 *      build does on x86/ARM.
 */

/// <summary>Constants from pix_internal.h used by the serialization of FPix and DPix.</summary>
public static partial class L
{
    /// <summary>Version number used in the serialized <see cref="FPix"/> format.</summary>
    public const int FPIX_VERSION_NUMBER = 2;

    /// <summary>Version number used in the serialized <see cref="DPix"/> format.</summary>
    public const int DPIX_VERSION_NUMBER = 2;
}

/// <summary>Helpers for the stdio calls (fgets, fread, fgetc) of the serialization code.</summary>
internal static class FPixIo
{
    /// <summary>fgets(buf, 256, fp): reads up to 255 bytes, stopping after a newline; null at EOF.</summary>
    public static string? Fgets(Stream fp)
    {
        const int Max = 255;
        StringBuilder sb = new StringBuilder();
        while (sb.Length < Max) {
            int c = fp.ReadByte();
            if (c < 0)
                break;
            sb.Append((char)c);
            if (c == '\n')
                break;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>fread(buf, 1, buf.Length, fp): returns the number of bytes read.</summary>
    public static int Fread(Stream fp, Span<byte> buf)
    {
        int total = 0;
        while (total < buf.Length) {
            int got = fp.Read(buf.Slice(total));
            if (got <= 0)
                break;
            total += got;
        }
        return total;
    }

    /// <summary>The scanner of the C code pushes back one character; a stream that cannot seek is buffered.</summary>
    public static Stream Seekable(Stream fp)
    {
        if (fp.CanSeek)
            return fp;
        MemoryStream ms = new MemoryStream();
        fp.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
}

/// <summary>
/// Pix with a float array (struct FPix).
/// </summary>
public sealed partial class FPix
{
    private int w;      /* width in pixels                   */
    private int h;      /* height in pixels                  */
    private int xres;   /* image res (ppi) in x direction    */
    private int yres;   /* image res (ppi) in y direction    */

    private FPix()
    {
        Data = Array.Empty<float>();
    }

    /*--------------------------------------------------------------------*
     *                     FPix Create/copy/destroy                       *
     *--------------------------------------------------------------------*/
    // leptonica: fpixCreate (fpix1.c)
    /// <summary>Makes a FPix of specified size, with the data array allocated and initialized to 0.</summary>
    /// <remarks>
    /// (1) Makes a FPix of specified size, with the data array
    ///     allocated and initialized to 0.
    /// (2) The number of pixels must be less than 2^29.
    /// </remarks>
    public static FPix Create(int width, int height)
    {
        const string func = "fpixCreate";
        if (width <= 0)
            throw Lept.Arg(func, "width must be > 0");
        if (height <= 0)
            throw Lept.Arg(func, "height must be > 0");

            /* Avoid overflow in malloc arg, malicious or otherwise */
        ulong npix64 = (ulong)width * (ulong)height;   /* # of 4-byte pixels */
        if (npix64 >= (1UL << 29)) {
            Log.Error(func, $"requested w = {width}, h = {height}");
            throw Lept.Arg(func, "requested bytes >= 2^31");
        }

        FPix fpixd = new FPix();
        fpixd.SetDimensions(width, height);
        fpixd.Wpl = width;  /* 4-byte words */

        float[] data = new float[(int)((long)width * height)];
        fpixd.Data = data;
        return fpixd;
    }

    internal static FPix CreateNoInit(int width, int height)
    {
        const string func = "fpixCreate";
        if (width <= 0)
            throw Lept.Arg(func, "width must be > 0");
        if (height <= 0)
            throw Lept.Arg(func, "height must be > 0");

        ulong npix64 = (ulong)width * (ulong)height;
        if (npix64 >= (1UL << 29)) {
            Log.Error(func, $"requested w = {width}, h = {height}");
            throw Lept.Arg(func, "requested bytes >= 2^31");
        }

        FPix fpixd = new FPix();
        fpixd.SetDimensions(width, height);
        fpixd.Wpl = width;
        fpixd.Data = GC.AllocateUninitializedArray<float>((int)npix64);
        return fpixd;
    }

    // leptonica: fpixCreateTemplate (fpix1.c)
    /// <summary>Makes a FPix of the same size as this one, with the data array zeroed.</summary>
    /// <remarks>
    /// (1) Makes a FPix of the same size as the input FPix, with the
    ///     data array allocated and initialized to 0.
    /// (2) Copies the resolution.
    /// </remarks>
    public FPix CreateTemplate()
    {
        GetDimensions(out int w, out int h);
        FPix fpixd = Create(w, h);
        fpixd.CopyResolution(this);
        return fpixd;
    }

    internal FPix CreateTemplateNoInit()
    {
        GetDimensions(out int w, out int h);
        FPix fpixd = CreateNoInit(w, h);
        fpixd.CopyResolution(this);
        return fpixd;
    }

    // leptonica: fpixClone (fpix1.c)
    /// <summary>Returns the same FPix (a reference; changes are seen through both).</summary>
    /// <remarks>(1) See pixClone() for definition and usage.</remarks>
    public FPix Clone()
    {
        return this;
    }

    // leptonica: fpixCopy (fpix1.c)
    /// <summary>Returns a copy of the FPix.</summary>
    public FPix Copy()
    {
            /* Total bytes in image data */
        GetDimensions(out int w, out int h);

        FPix fpixd = CreateTemplateNoInit();
        Data.AsSpan(0, w * h).CopyTo(fpixd.Data);
        return fpixd;
    }

    /*--------------------------------------------------------------------*
     *                          FPix  Accessors                           *
     *--------------------------------------------------------------------*/
    /// <summary>Gets the width and height of the image.</summary>
    /// <param name="w">Receives the width in pixels.</param>
    /// <param name="h">Receives the height in pixels.</param>
    // leptonica: fpixGetDimensions (fpix1.c)
    public void GetDimensions(out int w, out int h)
    {
        w = this.w;
        h = this.h;
    }

    /// <summary>Sets the width and height recorded for the image, without reallocating the data.</summary>
    /// <param name="w">The width in pixels.</param>
    /// <param name="h">The height in pixels.</param>
    // leptonica: fpixSetDimensions (fpix1.c)
    public void SetDimensions(int w, int h)
    {
        this.w = w;
        this.h = h;
    }

    // leptonica: fpixGetWpl, fpixSetWpl (fpix1.c)
    /// <summary>32-bit words/line.</summary>
    public int Wpl { get; set; }

    // leptonica: fpixGetResolution (fpix1.c)
    /// <param name="xres">x resolution</param>
    /// <param name="yres">y resolution</param>
    public void GetResolution(out int xres, out int yres)
    {
        xres = this.xres;
        yres = this.yres;
    }

    // leptonica: fpixSetResolution (fpix1.c)
    /// <param name="xres">x resolution</param>
    /// <param name="yres">y resolution</param>
    public void SetResolution(int xres, int yres)
    {
        this.xres = xres;
        this.yres = yres;
    }

    // leptonica: fpixCopyResolution (fpix1.c)
    /// <summary>Copies the resolution of <paramref name="fpixs"/> to this FPix.</summary>
    public void CopyResolution(FPix fpixs)
    {
        fpixs.GetResolution(out int xres, out int yres);
        SetResolution(xres, yres);
    }

    // leptonica: fpixGetData, fpixSetData (fpix1.c)
    /// <summary>The float image data (not a copy).</summary>
    public float[] Data { get; set; }

    // leptonica: fpixGetPixel (fpix1.c)
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="val">pixel value; 0.0 if the point is outside the image</param>
    /// <returns>true if OK; false (C: 2) if the point is outside the image</returns>
    /// <remarks>
    /// (1) If the point is outside the image, this returns an error (2),
    ///     with 0.0 in %pval.  To avoid spamming output, it fails silently.
    /// </remarks>
    public bool GetPixel(int x, int y, out float val)
    {
        val = 0.0f;

        GetDimensions(out int w, out int h);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        val = Data[y * w + x];
        return true;
    }

    // leptonica: fpixSetPixel (fpix1.c)
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="val">pixel value</param>
    /// <returns>true if OK; false (C: 2) if the point is outside the image</returns>
    /// <remarks>
    /// (1) If the point is outside the image, this returns an error (2),
    ///     with 0.0 in %pval.  To avoid spamming output, it fails silently.
    /// </remarks>
    public bool SetPixel(int x, int y, float val)
    {
        GetDimensions(out int w, out int h);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        Data[y * w + x] = val;
        return true;
    }

    /*--------------------------------------------------------------------*
     *                       FPix serialized I/O                          *
     *--------------------------------------------------------------------*/
    /// <summary>Reads a serialized float image from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The image read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or its contents are not a valid serialized image.</exception>
    // leptonica: fpixRead (fpix1.c)
    public static FPix Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (LeptonicaException) {
            throw Lept.Arg("fpixRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in fpixRead: fpix not read: {filename}", e);
            }
        }
    }

    // leptonica: fpixReadStream (fpix1.c)
    /// <param name="fp">input stream</param>
    public static FPix ReadStream(Stream fp)
    {
        const string func = "fpixReadStream";
        Span<int> v = stackalloc int[3];

        fp = FPixIo.Seekable(fp);
        int version, w, h, nbytes;
        CScanner sc = new CScanner(fp);
        try {
            if (sc.Scan("\nFPix Version %d\n", v) != 1)
                throw Lept.Arg(func, "not a fpix file");
            version = v[0];
            if (version != L.FPIX_VERSION_NUMBER)
                throw Lept.Arg(func, "invalid fpix version");
            if (sc.Scan("w = %d, h = %d, nbytes = %d\n", v) != 3)
                throw Lept.Arg(func, "read fail for data size");
            w = v[0];
            h = v[1];
            nbytes = v[2];
        } finally {
            sc.Release();
        }

            /* Use fgets() and sscanf(); not fscanf(), for the last
             * bit of header data before the float data.  The reason is
             * that fscanf throws away white space, and if the float data
             * happens to begin with ascii character(s) that are white
             * space, it will swallow them and all will be lost!  */
        string? buf = FPixIo.Fgets(fp);
        if (buf == null)
            throw Lept.Arg(func, "fgets read fail");
        if (CFormat.Sscanf(buf, "xres = %d, yres = %d\n", out object?[] xy) != 2)
            throw Lept.Arg(func, "read fail for xres, yres");
        int xres = (int)xy[0]!;
        int yres = (int)xy[1]!;

        FPix fpix = Create(w, h);
        fpix.SetResolution(xres, yres);
        float[] data = fpix.Data;
        Span<byte> bytes = MemoryMarshal.AsBytes(data.AsSpan());
        // NOTE(upstream): nbytes is not checked against the size of the data in C, so a
        // corrupt header overflows the buffer; here such a header is a read error.
        if (nbytes < 0 || nbytes > bytes.Length || FPixIo.Fread(fp, bytes.Slice(0, nbytes)) != nbytes)
            throw Lept.Arg(func, "read error for nbytes");
        fp.ReadByte();  /* ending nl */

            /* Convert to little-endian if necessary */
        EndianByteSwap(fpix, fpix);
        return fpix;
    }

    // leptonica: fpixReadMem (fpix1.c)
    /// <param name="data">of serialized fpix</param>
    public static FPix ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("fpixReadMem", "fpix not read");
            throw;
        }
    }

    /// <summary>Writes the float image to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: fpixWrite (fpix1.c)
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (LeptonicaException) {
            throw Lept.Arg("fpixWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: fpixWriteStream (fpix1.c)
    /// <param name="fp">stream opened for writing</param>
    public void WriteStream(Stream fp)
    {
            /* Convert to little-endian if necessary */
        FPix fpixt = EndianByteSwap(null, this);

        fpixt.GetDimensions(out int w, out int h);
        float[] data = fpixt.Data;
        uint nbytes = (uint)(sizeof(float) * (long)w * h);
        fpixt.GetResolution(out int xres, out int yres);
        CStdio.Fprintf(fp, $"\nFPix Version {L.FPIX_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"w = {w}, h = {h}, nbytes = {nbytes}\n");
        CStdio.Fprintf(fp, $"xres = {xres}, yres = {yres}\n");
        fp.Write(MemoryMarshal.AsBytes(data.AsSpan()).Slice(0, (int)nbytes));
        CStdio.Fprintf(fp, $"\n");
    }

    // leptonica: fpixWriteMem (fpix1.c)
    /// <summary>Serializes a fpix in memory and returns the bytes.</summary>
    /// <remarks>(1) Serializes a fpix in memory and puts the result in a buffer.</remarks>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }

    // leptonica: fpixEndianByteSwap (fpix1.c)
    /// <param name="fpixd">[optional] can be either null, or equal to fpixs</param>
    /// <param name="fpixs">source</param>
    /// <returns>fpixd always</returns>
    /// <remarks>
    /// (1) On big-endian hardware, this does byte-swapping on each of
    ///     the 4-byte floats in the fpix data.  On little-endians,
    ///     the data is unchanged.  This is used for serialization
    ///     of fpix; the data is serialized in little-endian byte
    ///     order because most hardware is little-endian.
    /// (2) The operation can be either in-place or, if fpixd == null,
    ///     a new fpix is made.  If not in-place, caller must catch
    ///     the returned reference.
    /// </remarks>
    public static FPix EndianByteSwap(FPix? fpixd, FPix fpixs)
    {
        if (fpixd != null && !ReferenceEquals(fpixs, fpixd))
            throw Lept.Arg("fpixEndianByteSwap", "fpixd != fpixs");

        if (fpixd != null)
            return fpixd;  /* no-op */
        else
            return fpixs.Clone();
    }

    /*--------------------------------------------------------------------*
     *                 Print FPix (subsampled, for debugging)             *
     *--------------------------------------------------------------------*/
    // leptonica: fpixPrintStream (fpix1.c)
    /// <param name="fp">stream</param>
    /// <param name="factor">for subsampling</param>
    /// <remarks>(1) Subsampled printout of fpix for debugging.</remarks>
    public void PrintStream(Stream fp, int factor)
    {
        if (factor < 1)
            throw Lept.Arg("fpixPrintStream", "sampling factor < 1f");

        GetDimensions(out int w, out int h);
        CStdio.Fprintf(fp, $"\nFPix: w = {w}, h = {h}\n");
        for (int i = 0; i < h; i += factor) {
            int count, j;
            for (count = 0, j = 0; j < w; j += factor, count++) {
                GetPixel(j, i, out float val);
                CStdio.Fprintf(fp, $"{CFormat.Sprintf("val[%d, %d] = %f   ", i, j, val)}");
                if ((count + 1) % 3 == 0) CStdio.Fprintf(fp, $"\n");
            }
            if (count % 3 != 0) CStdio.Fprintf(fp, $"\n");
        }
        CStdio.Fprintf(fp, $"\n");
    }
}

/// <summary>
/// Array of FPix (struct FPixa).
/// </summary>
public sealed partial class FPixa
{
    /* Bounds on array sizes */
    private const int MaxPtrArraySize = 100000;
    private const int InitialPtrArraySize = 20;      /* n'importe quoi */

    private FPix?[] fpix;  /* the array of ptrs to fpix; its length is nalloc */
    private int n;         /* number of fpix in ptr array                     */

    private FPixa(int nalloc)
    {
        fpix = new FPix?[nalloc];
        n = 0;
    }

    /// <summary>Number of allocated fpix ptrs (fpixa->nalloc).</summary>
    public int Nalloc => fpix.Length;

    /*--------------------------------------------------------------------*
     *                     FPixa Create/copy/destroy                      *
     *--------------------------------------------------------------------*/
    // leptonica: fpixaCreate (fpix1.c)
    /// <param name="n">initial number of ptrs</param>
    public static FPixa Create(int n)
    {
        if (n <= 0 || n > MaxPtrArraySize)
            n = InitialPtrArraySize;

        return new FPixa(n);
    }

    // leptonica: fpixaCopy (fpix1.c)
    /// <summary>Copies or clones the fpixa.</summary>
    /// <param name="copyflag">L_COPY, L_CLONE or L_COPY_CLONE</param>
    /// <remarks>
    /// copyflag may be one of
    ///   ~ L_COPY makes a new fpixa and copies each fpix
    ///   ~ L_CLONE gives a new ref-counted handle to the input fpixa
    ///   ~ L_COPY_CLONE makes a new fpixa with clones of all fpix
    /// </remarks>
    public FPixa Copy(int copyflag)
    {
        if (copyflag == L.L_CLONE)
            return this;

        if (copyflag != L.L_COPY && copyflag != L.L_COPY_CLONE)
            throw Lept.Arg("fpixaCopy", "invalid copyflag");

        FPixa fpixac = Create(n);
        for (int i = 0; i < n; i++) {
            FPix fpixc;
            if (copyflag == L.L_COPY)
                fpixc = GetFPix(i, L.L_COPY);
            else  /* copy-clone */
                fpixc = GetFPix(i, L.L_CLONE);
            fpixac.AddFPix(fpixc, L.L_INSERT);
        }

        return fpixac;
    }

    /*--------------------------------------------------------------------*
     *                           FPixa addition                           *
     *--------------------------------------------------------------------*/
    // leptonica: fpixaAddFPix (fpix1.c)
    /// <param name="fpix">to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddFPix(FPix fpix, int copyflag)
    {
        FPix fpixc;
        if (copyflag == L.L_INSERT)
            fpixc = fpix;
        else if (copyflag == L.L_COPY)
            fpixc = fpix.Copy();
        else if (copyflag == L.L_CLONE)
            fpixc = fpix.Clone();
        else
            throw Lept.Arg("fpixaAddFPix", "invalid copyflag");

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        this.fpix[n] = fpixc;
        this.n++;
    }

    // leptonica: fpixaExtendArray (fpix1.c)
    /// <remarks>
    /// (1) Doubles the size of the fpixa ptr array.
    /// (2) The max number of fpix ptrs is 100000.
    /// </remarks>
    private void ExtendArray()
    {
        ExtendArrayToSize(2 * Nalloc);
    }

    // leptonica: fpixaExtendArrayToSize (fpix1.c)
    /// <param name="size">new ptr array size</param>
    /// <remarks>
    /// (1) If necessary, reallocs new fpix ptr array to %size.
    /// (2) The max number of fpix ptrs is 100K.
    /// </remarks>
    private void ExtendArrayToSize(int size)
    {
        const string func = "fpixaExtendArrayToSize";
        if (Nalloc > MaxPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail(func, "fpixa has too many ptrs");
        /* size is compared as a size_t in C, so a negative value is huge */
        if (size < 0 || size > MaxPtrArraySize)
            throw Lept.Arg(func, "size > 100K ptrs; too large");
        if (size <= Nalloc) {
            Log.Info(func, "size too small; no extension");
            return;
        }

        Array.Resize(ref fpix, size);
    }

    /*--------------------------------------------------------------------*
     *                          FPixa accessors                           *
     *--------------------------------------------------------------------*/
    // leptonica: fpixaGetCount (fpix1.c)
    /// <summary>Count of fpix; 0 if empty.</summary>
    public int Count => n;

    // leptonica: fpixaGetFPix (fpix1.c)
    /// <param name="index">to the index-th fpix</param>
    /// <param name="accesstype">L_COPY or L_CLONE</param>
    public FPix GetFPix(int index, int accesstype)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("fpixaGetFPix", "index not valid");

        if (accesstype == L.L_COPY)
            return fpix[index]!.Copy();
        else if (accesstype == L.L_CLONE)
            return fpix[index]!.Clone();
        else
            throw Lept.Arg("fpixaGetFPix", "invalid accesstype");
    }

    // leptonica: fpixaGetFPixDimensions (fpix1.c)
    /// <summary>Gets the width and height of one of the float images.</summary>
    /// <param name="index">The index of the image.</param>
    /// <param name="w">Receives the width in pixels.</param>
    /// <param name="h">Receives the height in pixels.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is not a valid image index.</exception>
    public void GetFPixDimensions(int index, out int w, out int h)
    {
        w = 0;
        h = 0;
        if (index < 0 || index >= n)
            throw Lept.Arg("fpixaGetFPixDimensions", "index not valid");

        FPix fpix = GetFPix(index, L.L_CLONE);
        fpix.GetDimensions(out w, out h);
    }

    // leptonica: fpixaGetData (fpix1.c)
    /// <param name="index">into fpixa array</param>
    /// <returns>data, not a copy</returns>
    public float[] GetData(int index)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("fpixaGetData", "invalid index");

        FPix fpix = GetFPix(index, L.L_CLONE);
        float[] data = fpix.Data;
        return data;
    }

    // leptonica: fpixaGetPixel (fpix1.c)
    /// <param name="index">into fpixa array</param>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="val">pixel value</param>
    /// <returns>true if OK; false (C: 2) if the point is outside the image</returns>
    public bool GetPixel(int index, int x, int y, out float val)
    {
        val = 0.0f;
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("fpixaGetPixel", "invalid index into fpixa");

        FPix fpix = GetFPix(index, L.L_CLONE);
        bool ret = fpix.GetPixel(x, y, out val);
        return ret;
    }

    // leptonica: fpixaSetPixel (fpix1.c)
    /// <param name="index">into fpixa array</param>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="val">pixel value</param>
    /// <returns>true if OK; false (C: 2) if the point is outside the image</returns>
    public bool SetPixel(int index, int x, int y, float val)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("fpixaSetPixel", "invalid index into fpixa");

        FPix fpix = GetFPix(index, L.L_CLONE);
        bool ret = fpix.SetPixel(x, y, val);
        return ret;
    }
}

/// <summary>
/// Pix with a double array (struct DPix).
/// </summary>
public sealed partial class DPix
{
    private int w;      /* width in pixels                   */
    private int h;      /* height in pixels                  */
    private int xres;   /* image res (ppi) in x direction    */
    private int yres;   /* image res (ppi) in y direction    */

    private DPix()
    {
        Data = Array.Empty<double>();
    }

    /*--------------------------------------------------------------------*
     *                     DPix Create/copy/destroy                       *
     *--------------------------------------------------------------------*/
    // leptonica: dpixCreate (fpix1.c)
    /// <summary>Makes a DPix of specified size, with the data array allocated and initialized to 0.</summary>
    /// <remarks>
    /// (1) Makes a DPix of specified size, with the data array
    ///     allocated and initialized to 0.
    /// (2) The number of pixels must be less than 2^28.
    /// </remarks>
    public static DPix Create(int width, int height)
    {
        const string func = "dpixCreate";
        if (width <= 0)
            throw Lept.Arg(func, "width must be > 0");
        if (height <= 0)
            throw Lept.Arg(func, "height must be > 0");

            /* Avoid overflow in malloc arg, malicious or otherwise */
        ulong npix64 = (ulong)width * (ulong)height;   /* # of 8 byte pixels */
        if (npix64 >= (1UL << 28)) {
            Log.Error(func, $"requested w = {width}, h = {height}");
            throw Lept.Arg(func, "requested bytes >= 2^31");
        }

        DPix dpix = new DPix();
        dpix.SetDimensions(width, height);
        dpix.Wpl = width;  /* 8 byte words */

        double[] data = new double[(int)((long)width * height)];
        dpix.Data = data;
        return dpix;
    }

    internal static DPix CreateNoInit(int width, int height)
    {
        const string func = "dpixCreate";
        if (width <= 0)
            throw Lept.Arg(func, "width must be > 0");
        if (height <= 0)
            throw Lept.Arg(func, "height must be > 0");

        ulong npix64 = (ulong)width * (ulong)height;
        if (npix64 >= (1UL << 28)) {
            Log.Error(func, $"requested w = {width}, h = {height}");
            throw Lept.Arg(func, "requested bytes >= 2^31");
        }

        DPix dpix = new DPix();
        dpix.SetDimensions(width, height);
        dpix.Wpl = width;
        dpix.Data = GC.AllocateUninitializedArray<double>((int)npix64);
        return dpix;
    }

    // leptonica: dpixCreateTemplate (fpix1.c)
    /// <summary>Makes a DPix of the same size as this one, with the data array zeroed.</summary>
    /// <remarks>
    /// (1) Makes a DPix of the same size as the input DPix, with the
    ///     data array allocated and initialized to 0.
    /// (2) Copies the resolution.
    /// </remarks>
    public DPix CreateTemplate()
    {
        GetDimensions(out int w, out int h);
        DPix dpixd = Create(w, h);
        dpixd.CopyResolution(this);
        return dpixd;
    }

    internal DPix CreateTemplateNoInit()
    {
        GetDimensions(out int w, out int h);
        DPix dpixd = CreateNoInit(w, h);
        dpixd.CopyResolution(this);
        return dpixd;
    }

    // leptonica: dpixClone (fpix1.c)
    /// <summary>Returns the same DPix (a reference; changes are seen through both).</summary>
    /// <remarks>(1) See pixClone() for definition and usage.</remarks>
    public DPix Clone()
    {
        return this;
    }

    // leptonica: dpixCopy (fpix1.c)
    /// <summary>Returns a copy of the DPix.</summary>
    public DPix Copy()
    {
            /* Total bytes in image data */
        GetDimensions(out int w, out int h);

        DPix dpixd = CreateTemplateNoInit();
        Data.AsSpan(0, w * h).CopyTo(dpixd.Data);
        return dpixd;
    }

    /*--------------------------------------------------------------------*
     *                          DPix  Accessors                           *
     *--------------------------------------------------------------------*/
    /// <summary>Gets the width and height of the image.</summary>
    /// <param name="w">Receives the width in pixels.</param>
    /// <param name="h">Receives the height in pixels.</param>
    // leptonica: dpixGetDimensions (fpix1.c)
    public void GetDimensions(out int w, out int h)
    {
        w = this.w;
        h = this.h;
    }

    /// <summary>Sets the width and height recorded for the image, without reallocating the data.</summary>
    /// <param name="w">The width in pixels.</param>
    /// <param name="h">The height in pixels.</param>
    // leptonica: dpixSetDimensions (fpix1.c)
    public void SetDimensions(int w, int h)
    {
        this.w = w;
        this.h = h;
    }

    // leptonica: dpixGetWpl, dpixSetWpl (fpix1.c)
    /// <summary>32-bit words/line.</summary>
    public int Wpl { get; set; }

    // leptonica: dpixGetResolution (fpix1.c)
    /// <param name="xres">x resolution</param>
    /// <param name="yres">y resolution</param>
    public void GetResolution(out int xres, out int yres)
    {
        xres = this.xres;
        yres = this.yres;
    }

    // leptonica: dpixSetResolution (fpix1.c)
    /// <param name="xres">x resolution</param>
    /// <param name="yres">y resolution</param>
    public void SetResolution(int xres, int yres)
    {
        this.xres = xres;
        this.yres = yres;
    }

    // leptonica: dpixCopyResolution (fpix1.c)
    /// <summary>Copies the resolution of <paramref name="dpixs"/> to this DPix.</summary>
    public void CopyResolution(DPix dpixs)
    {
        dpixs.GetResolution(out int xres, out int yres);
        SetResolution(xres, yres);
    }

    // leptonica: dpixGetData, dpixSetData (fpix1.c)
    /// <summary>The double image data (not a copy).</summary>
    public double[] Data { get; set; }

    // leptonica: dpixGetPixel (fpix1.c)
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="val">pixel value; 0.0 if the point is outside the image</param>
    /// <returns>true if OK; false (C: 2) if the point is outside the image</returns>
    /// <remarks>
    /// (1) If the point is outside the image, this returns an error (2),
    ///     with 0.0 in %pval.  To avoid spamming output, it fails silently.
    /// </remarks>
    public bool GetPixel(int x, int y, out double val)
    {
        val = 0.0;

        GetDimensions(out int w, out int h);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        val = Data[y * w + x];
        return true;
    }

    // leptonica: dpixSetPixel (fpix1.c)
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="val">pixel value</param>
    /// <returns>true if OK; false (C: 2) if the point is outside the image</returns>
    /// <remarks>
    /// (1) If the point is outside the image, this returns an error (2),
    ///     with 0.0 in %pval.  To avoid spamming output, it fails silently.
    /// </remarks>
    public bool SetPixel(int x, int y, double val)
    {
        GetDimensions(out int w, out int h);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        Data[y * w + x] = val;
        return true;
    }

    /*--------------------------------------------------------------------*
     *                       DPix serialized I/O                          *
     *--------------------------------------------------------------------*/
    /// <summary>Reads a serialized double-precision image from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The image read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or its contents are not a valid serialized image.</exception>
    // leptonica: dpixRead (fpix1.c)
    public static DPix Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (LeptonicaException) {
            throw Lept.Arg("dpixRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in dpixRead: dpix not read: {filename}", e);
            }
        }
    }

    // leptonica: dpixReadStream (fpix1.c)
    /// <param name="fp">input stream</param>
    public static DPix ReadStream(Stream fp)
    {
        const string func = "dpixReadStream";
        Span<int> v = stackalloc int[3];

        fp = FPixIo.Seekable(fp);
        int version, w, h, nbytes;
        CScanner sc = new CScanner(fp);
        try {
            if (sc.Scan("\nDPix Version %d\n", v) != 1)
                throw Lept.Arg(func, "not a dpix file");
            version = v[0];
            if (version != L.DPIX_VERSION_NUMBER)
                throw Lept.Arg(func, "invalid dpix version");
            if (sc.Scan("w = %d, h = %d, nbytes = %d\n", v) != 3)
                throw Lept.Arg(func, "read fail for data size");
            w = v[0];
            h = v[1];
            nbytes = v[2];
        } finally {
            sc.Release();
        }

            /* Use fgets() and sscanf(); not fscanf(), for the last
             * bit of header data before the float data.  The reason is
             * that fscanf throws away white space, and if the float data
             * happens to begin with ascii character(s) that are white
             * space, it will swallow them and all will be lost!  */
        string? buf = FPixIo.Fgets(fp);
        if (buf == null)
            throw Lept.Arg(func, "fgets read fail");
        if (CFormat.Sscanf(buf, "xres = %d, yres = %d\n", out object?[] xy) != 2)
            throw Lept.Arg(func, "read fail for xres, yres");
        int xres = (int)xy[0]!;
        int yres = (int)xy[1]!;

        DPix dpix = Create(w, h);
        dpix.SetResolution(xres, yres);
        double[] data = dpix.Data;
        Span<byte> bytes = MemoryMarshal.AsBytes(data.AsSpan());
        // NOTE(upstream): nbytes is not checked against the size of the data in C, so a
        // corrupt header overflows the buffer; here such a header is a read error.
        if (nbytes < 0 || nbytes > bytes.Length || FPixIo.Fread(fp, bytes.Slice(0, nbytes)) != nbytes)
            throw Lept.Arg(func, "read error for nbytes");
        fp.ReadByte();  /* ending nl */

            /* Convert to little-endian if necessary */
        EndianByteSwap(dpix, dpix);
        return dpix;
    }

    // leptonica: dpixReadMem (fpix1.c)
    /// <param name="data">of serialized dpix</param>
    public static DPix ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("dpixReadMem", "dpix not read");
            throw;
        }
    }

    /// <summary>Writes the double-precision image to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: dpixWrite (fpix1.c)
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (LeptonicaException) {
            throw Lept.Arg("dpixWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: dpixWriteStream (fpix1.c)
    /// <param name="fp">stream opened for writing</param>
    public void WriteStream(Stream fp)
    {
            /* Convert to little-endian if necessary */
        DPix dpixt = EndianByteSwap(null, this);

        dpixt.GetDimensions(out int w, out int h);
        dpixt.GetResolution(out int xres, out int yres);
        double[] data = dpixt.Data;
        uint nbytes = (uint)(sizeof(double) * (long)w * h);
        CStdio.Fprintf(fp, $"\nDPix Version {L.DPIX_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"w = {w}, h = {h}, nbytes = {nbytes}\n");
        CStdio.Fprintf(fp, $"xres = {xres}, yres = {yres}\n");
        fp.Write(MemoryMarshal.AsBytes(data.AsSpan()).Slice(0, (int)nbytes));
        CStdio.Fprintf(fp, $"\n");
    }

    // leptonica: dpixWriteMem (fpix1.c)
    /// <summary>Serializes a dpix in memory and returns the bytes.</summary>
    /// <remarks>(1) Serializes a dpix in memory and puts the result in a buffer.</remarks>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }

    // leptonica: dpixEndianByteSwap (fpix1.c)
    /// <param name="dpixd">[optional] can be either null, or equal to dpixs</param>
    /// <param name="dpixs">source</param>
    /// <returns>dpixd always</returns>
    /// <remarks>
    /// (1) On big-endian hardware, this does byte-swapping on each of
    ///     the 4-byte words in the dpix data.  On little-endians,
    ///     the data is unchanged.  This is used for serialization
    ///     of dpix; the data is serialized in little-endian byte
    ///     order because most hardware is little-endian.
    /// (2) The operation can be either in-place or, if dpixd == null,
    ///     a new dpix is made.  If not in-place, caller must catch
    ///     the returned reference.
    /// </remarks>
    public static DPix EndianByteSwap(DPix? dpixd, DPix dpixs)
    {
        if (dpixd != null && !ReferenceEquals(dpixs, dpixd))
            throw Lept.Arg("dpixEndianByteSwap", "dpixd != dpixs");

        if (dpixd != null)
            return dpixd;  /* no-op */
        else
            return dpixs.Clone();
    }
}
