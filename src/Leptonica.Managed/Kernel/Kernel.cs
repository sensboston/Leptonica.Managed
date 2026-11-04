using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  kernel.c: Basic operations on kernels for image convolution.
 *
 *  Differences from C:
 *    - No kernelDestroy(): the GC owns the objects (not ported).
 *    - FILE* becomes Stream.  The serialization is written byte for byte as
 *      the C fprintf calls write it, and read with the fscanf formats of C.
 *    - Functions that return an error code or NULL in C throw instead.
 *    - kernelGetElement(), kernelGetSum() return the value; kernelGetMinMax()
 *      has both outputs as out parameters (the "neither" error of C cannot occur).
 *    - Float arithmetic is done in float, exactly as in C; expf() is MathF.Exp().
 */

/// <summary>Constants from morph.h used by the serialization of kernels.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized kernel format.</summary>
    public const int KERNEL_VERSION_NUMBER = 2;
}

/// <summary>
/// Kernel for convolution (struct L_Kernel).
/// </summary>
public sealed class L_Kernel
{
    /* Array size must be > 0 and not larger than this */
    private const uint MaxArraySize = 100000;

    /// <summary>Kernel height (kel->sy).</summary>
    public int Sy { get; private set; }
    /// <summary>Kernel width (kel->sx).</summary>
    public int Sx { get; private set; }
    /// <summary>Y value of the origin (kel->cy).</summary>
    public int Cy { get; private set; }
    /// <summary>X value of the origin (kel->cx).</summary>
    public int Cx { get; private set; }
    /// <summary>Kernel values, indexed [row][col] (kel->data).</summary>
    public float[][] Data { get; private set; } = Array.Empty<float[]>();

    private L_Kernel() { }

    /*------------------------------------------------------------------------*
     *                           Create / Destroy                             *
     *------------------------------------------------------------------------*/
    // leptonica: kernelCreate (kernel.c)
    /// <summary>Creates a kernel with all values 0.</summary>
    /// <param name="height">kernel height</param>
    /// <param name="width">kernel width</param>
    /// <remarks>
    /// (1) kernelCreate() initializes all values to 0.
    /// (2) After this call, (cy,cx) and nonzero data values must be
    ///     assigned.
    /// (2) The number of kernel elements must be less than 2^29.
    /// </remarks>
    public static L_Kernel Create(int height, int width)
    {
        const string func = "kernelCreate";
        if (width <= 0)
            throw Lept.Arg(func, "width must be > 0");
        if (height <= 0)
            throw Lept.Arg(func, "height must be > 0");

            /* Avoid overflow in malloc arg */
        ulong size64 = (ulong)width * (ulong)height;
        if (size64 >= (1UL << 29)) {
            Log.Error(func, $"requested width = {width}, height = {height}");
            throw Lept.Arg(func, "size >= 2^29");
        }

        L_Kernel kel = new L_Kernel();
        kel.Sy = height;
        kel.Sx = width;
        try {
            kel.Data = Create2dFloatArray(height, width);
        } catch (ArgumentException) {
            throw Lept.Arg(func, "data not allocated");
        }
        return kel;
    }

    // leptonica: kernelCopy (kernel.c)
    /// <summary>Returns a copy of the kernel.</summary>
    public L_Kernel Copy()
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);
        L_Kernel keld = Create(sy, sx);
        keld.Cy = cy;
        keld.Cx = cx;
        for (int i = 0; i < sy; i++)
            for (int j = 0; j < sx; j++)
                keld.Data[i][j] = Data[i][j];

        return keld;
    }

    /// <summary>Returns a contiguous row-major copy of the kernel weights for SIMD/cache-friendly convolution.</summary>
    internal float[] GetFlatData()
    {
        int sy = Sy, sx = Sx;
        float[] flat = new float[sy * sx];
        for (int i = 0; i < sy; i++)
            Data[i].AsSpan(0, sx).CopyTo(flat.AsSpan(i * sx, sx));
        return flat;
    }

    /*----------------------------------------------------------------------*
     *                               Accessors                              *
     *----------------------------------------------------------------------*/
    // leptonica: kernelGetElement (kernel.c)
    /// <summary>Gets the value of the element at (row, col).</summary>
    public float GetElement(int row, int col)
    {
        const string func = "kernelGetElement";
        if (row < 0 || row >= Sy)
            throw Lept.Arg(func, "kernel row out of bounds");
        if (col < 0 || col >= Sx)
            throw Lept.Arg(func, "kernel col out of bounds");

        return Data[row][col];
    }

    // leptonica: kernelSetElement (kernel.c)
    /// <summary>Sets the value of the element at (row, col).</summary>
    public void SetElement(int row, int col, float val)
    {
        const string func = "kernelSetElement";
        if (row < 0 || row >= Sy)
            throw Lept.Arg(func, "kernel row out of bounds");
        if (col < 0 || col >= Sx)
            throw Lept.Arg(func, "kernel col out of bounds");

        Data[row][col] = val;
    }

    // leptonica: kernelGetParameters (kernel.c)
    /// <summary>Gets the height, width and origin of the kernel.</summary>
    public void GetParameters(out int psy, out int psx, out int pcy, out int pcx)
    {
        psy = Sy;
        psx = Sx;
        pcy = Cy;
        pcx = Cx;
    }

    // leptonica: kernelSetOrigin (kernel.c)
    /// <summary>Sets the origin of the kernel.</summary>
    public void SetOrigin(int cy, int cx)
    {
        Cy = cy;
        Cx = cx;
    }

    // leptonica: kernelGetSum (kernel.c)
    /// <returns>sum of all kernel values</returns>
    public float GetSum()
    {
        float sum = 0.0f;
        GetParameters(out int sy, out int sx, out _, out _);
        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                sum += Data[i][j];
            }
        }
        return sum;
    }

    // leptonica: kernelGetMinMax (kernel.c)
    /// <param name="pmin">minimum value</param>
    /// <param name="pmax">maximum value</param>
    public void GetMinMax(out float pmin, out float pmax)
    {
        GetParameters(out int sy, out int sx, out _, out _);
        float minval = 10000000.0f;
        float maxval = -10000000.0f;
        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                float val = Data[i][j];
                if (val < minval)
                    minval = val;
                if (val > maxval)
                    maxval = val;
            }
        }
        pmin = minval;
        pmax = maxval;
    }

    /*----------------------------------------------------------------------*
     *                          Normalize/Invert                            *
     *----------------------------------------------------------------------*/
    // leptonica: kernelNormalize (kernel.c)
    /// <summary>Returns a normalized version of the kernel.</summary>
    /// <param name="normsum">desired sum of elements in the result</param>
    /// <returns>normalized version of this kernel, or a copy of it if the sum of
    /// elements is very close to 0</returns>
    /// <remarks>
    /// (1) If the sum of kernel elements is close to 0, do not
    ///     try to calculate the normalized kernel.  Instead,
    ///     return a copy of the input kernel, with a warning.
    /// </remarks>
    public L_Kernel Normalize(float normsum)
    {
        float sum = GetSum();
        if ((sum < 0 ? -sum : sum) < 0.00001) {
            Log.Warning("kernelNormalize", "null sum; not normalizing; returning a copy");
            return Copy();
        }

        GetParameters(out int sy, out int sx, out int cy, out int cx);
        L_Kernel keld = Create(sy, sx);
        keld.Cy = cy;
        keld.Cx = cx;

        float factor = normsum / sum;
        for (int i = 0; i < sy; i++)
            for (int j = 0; j < sx; j++)
                keld.Data[i][j] = factor * Data[i][j];

        return keld;
    }

    // leptonica: kernelInvert (kernel.c)
    /// <returns>kernel spatially inverted, about the origin</returns>
    /// <remarks>
    /// (1) For convolution, the kernel is spatially inverted before
    ///     a "correlation" operation is done between the kernel and the image.
    /// </remarks>
    public L_Kernel Invert()
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);
        L_Kernel keld = Create(sy, sx);
        keld.Cy = sy - 1 - cy;
        keld.Cx = sx - 1 - cx;

        for (int i = 0; i < sy; i++)
            for (int j = 0; j < sx; j++)
                keld.Data[i][j] = Data[sy - 1 - i][sx - 1 - j];

        return keld;
    }

    /*----------------------------------------------------------------------*
     *                            Helper function                           *
     *----------------------------------------------------------------------*/
    // leptonica: create2dFloatArray (kernel.c)
    /// <param name="sy">rows == height</param>
    /// <param name="sx">columns == width</param>
    /// <returns>doubly indexed array i.e., an array of sy row arrays,
    /// each of which has sx floats</returns>
    /// <remarks>
    /// (1) The array[%sy][%sx] is indexed in standard "matrix notation",
    ///     with the row index first.
    /// (2) The caller kernelCreate() limits the size to &lt; 2^29 pixels.
    /// </remarks>
    public static float[][] Create2dFloatArray(int sy, int sx)
    {
        const string func = "create2dFloatArray";
        if (sx <= 0 || (uint)sx > MaxArraySize)
            throw Lept.Arg(func, "sx out of bounds");
        if (sy <= 0 || (uint)sy > MaxArraySize)
            throw Lept.Arg(func, "sy out of bounds");

        float[][] array = new float[sy][];
        for (int i = 0; i < sy; i++)
            array[i] = new float[sx];
        return array;
    }

    /*----------------------------------------------------------------------*
     *                            Kernel serialized I/O                     *
     *----------------------------------------------------------------------*/
    // leptonica: kernelRead (kernel.c)
    /// <summary>Reads a serialized kernel from a file.</summary>
    /// <param name="fname">Path of the file to read.</param>
    /// <returns>The kernel read from the file.</returns>
    /// <exception cref="System.ArgumentException">The file cannot be opened, or it does not contain a valid serialized kernel.</exception>
    public static L_Kernel Read(string fname)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(fname);
        } catch (LeptonicaException) {
            throw Lept.Arg("kernelRead", $"stream not opened: {fname}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in kernelRead: kel not returned: {fname}", e);
            }
        }
    }

    // leptonica: kernelReadStream (kernel.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// If the stream is seekable, it is left after the serialized data.
    /// </remarks>
    public static L_Kernel ReadStream(Stream fp)
    {
        CFloatScanner sc = new CFloatScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    private static L_Kernel ReadStream(CFloatScanner sc)
    {
        const string func = "kernelReadStream";

        int ret = sc.Scan("  Kernel Version %d\n", out object?[] v);
        if (ret != 1)
            throw Lept.Arg(func, "not a kernel file");
        int version = (int)v[0]!;
        if (version != L.KERNEL_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid kernel version");

        if (sc.Scan("  sy = %d, sx = %d, cy = %d, cx = %d\n", out v) != 4)
            throw Lept.Arg(func, "dimensions not read");
        int sy = (int)v[0]!, sx = (int)v[1]!, cy = (int)v[2]!, cx = (int)v[3]!;
            /* sx and sy are compared to an unsigned constant in C */
        if ((uint)sx > MaxArraySize || (uint)sy > MaxArraySize) {
            Log.Error(func, $"sx = {sx} or sy = {sy} > {MaxArraySize}");
            throw Lept.Arg(func, "kernel too large");  /* C returns NULL here, without ERROR_PTR */
        }
        L_Kernel kel;
        try {
            kel = Create(sy, sx);
        } catch (ArgumentException) {
            throw Lept.Arg(func, "kel not made");
        }
        kel.SetOrigin(cy, cx);

        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                    /* the value is left unchanged (0) if the scan fails */
                if (sc.Scan("%15f", out v) == 1)
                    kel.Data[i][j] = (float)v[0]!;
            }
            sc.Scan("\n", out _);
        }
        sc.Scan("\n", out _);

        return kel;
    }

    // leptonica: kernelWrite (kernel.c)
    /// <summary>Writes the kernel to a file in the serialized text format.</summary>
    /// <param name="fname">Path of the file to write.</param>
    /// <exception cref="System.ArgumentException">The file cannot be opened for writing.</exception>
    public void Write(string fname)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(fname, "wb");
        } catch (Exception e) when (e is LeptonicaException || e is ArgumentException) {
            throw Lept.Arg("kernelWrite", $"stream not opened: {fname}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: kernelWriteStream (kernel.c)
    /// <param name="fp">output stream</param>
    public void WriteStream(Stream fp)
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);

        CStreamText.Fprintf(fp, "  Kernel Version %d\n", L.KERNEL_VERSION_NUMBER);
        CStreamText.Fprintf(fp, "  sy = %d, sx = %d, cy = %d, cx = %d\n", sy, sx, cy, cx);
        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++)
                CStreamText.Fprintf(fp, "%15.4f", Data[i][j]);
            CStreamText.Fprintf(fp, "\n");
        }
        CStreamText.Fprintf(fp, "\n");
    }

    /*----------------------------------------------------------------------*
     *                 Making a kernel from a compiled string               *
     *----------------------------------------------------------------------*/
    // leptonica: kernelCreateFromString (kernel.c)
    /// <summary>Makes a kernel from a string of numbers.</summary>
    /// <param name="h">height</param>
    /// <param name="w">width</param>
    /// <param name="cy">origin</param>
    /// <param name="cx">origin</param>
    /// <param name="kdata">the numbers</param>
    /// <returns>kernel of the given size</returns>
    /// <remarks>
    /// (1) The data is an array of chars, in row-major order, giving
    ///     space separated integers in the range [-255 ... 255].
    /// (2) The only other formatting limitation is that you must
    ///     leave space between the last number in each row and
    ///     the double-quote.  If possible, it's also nice to have each
    ///     line in the string represent a line in the kernel; e.g.,
    ///         static const char *kdata =
    ///             " 20   50   20 "
    ///             " 70  140   70 "
    ///             " 20   50   20 ";
    /// </remarks>
    public static L_Kernel CreateFromString(int h, int w, int cy, int cx, string kdata)
    {
        const string func = "kernelCreateFromString";
        if (h < 1)
            throw Lept.Arg(func, "height must be > 0");
        if (w < 1)
            throw Lept.Arg(func, "width must be > 0");
        if (cy < 0 || cy >= h)
            throw Lept.Arg(func, "cy invalid");
        if (cx < 0 || cx >= w)
            throw Lept.Arg(func, "cx invalid");

        L_Kernel kel = Create(h, w);
        kel.SetOrigin(cy, cx);
        Numa na = ParseStringForNumbers(kdata, " \t\n");
        int n = na.Count;
        if (n != w * h) {
            CStreamText.Stderr("w = %d, h = %d, num ints = %d\n", w, h, n);
            throw Lept.Arg(func, "invalid integer data");
        }

        int index = 0;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                float val = na.GetFValue(index);
                kel.SetElement(i, j, val);
                index++;
            }
        }

        return kel;
    }

    /*----------------------------------------------------------------------*
     *                Making a kernel from a simple file format             *
     *----------------------------------------------------------------------*/
    // leptonica: kernelCreateFromFile (kernel.c)
    /// <summary>Makes a kernel from a text file.</summary>
    /// <remarks>
    /// (1) The file contains, in the following order:
    ///      ~ Any number of comment lines starting with '#' are ignored
    ///      ~ The height and width of the kernel
    ///      ~ The y and x values of the kernel origin
    ///      ~ The kernel data, formatted as lines of numbers (integers
    ///        or floats) for the kernel values in row-major order,
    ///        and with no other punctuation.
    ///        (Note: this differs from kernelCreateFromString(),
    ///        where each line must begin and end with a double-quote
    ///        to tell the compiler it's part of a string.)
    ///      ~ The kernel specification ends when a blank line,
    ///        a comment line, or the end of file is reached.
    /// (2) All lines must be left-justified.
    /// (3) See kernelCreateFromString() for a description of the string
    ///     format for the kernel data.  As an example, here are the lines
    ///     of a valid kernel description file  In the file, all lines
    ///     are left-justified:
    ///                # small 3x3 kernel
    ///                3 3
    ///                1 1
    ///                25.5   51    24.3
    ///                70.2  146.3  73.4
    ///                20     50.9  18.4
    /// </remarks>
    public static L_Kernel CreateFromFile(string filename)
    {
        const string func = "kernelCreateFromFile";
        byte[] filebytes;
        try {
            filebytes = Utils.BinaryRead(filename);
        } catch (LeptonicaException) {
            throw Lept.Arg(func, $"file not found: {filename}");
        }
        if (filebytes.Length == 0)
            throw Lept.Arg(func, $"file is empty: {filename}");

        string filestr = Encoding.Latin1.GetString(filebytes);
        Sarray sa = Sarray.CreateLinesFromString(filestr, 1);
        int nlines = sa.Count;

            /* Find the first data line. */
        int i, first;
        string line;
        for (i = 0, first = 0; i < nlines; i++) {
            line = sa.GetString(i, L.L_NOCOPY);
            if (FirstChar(line) != '#') {
                first = i;
                break;
            }
        }

            /* Find the kernel dimensions and origin location. */
        line = sa.GetString(first, L.L_NOCOPY);
        if (CFormat.Sscanf(line, "%d %d", out object?[] v) != 2)
            throw Lept.Arg(func, "error reading h,w");
        int h = (int)v[0]!, w = (int)v[1]!;
            /* h and w are compared to an unsigned constant in C */
        if ((uint)h > MaxArraySize || (uint)w > MaxArraySize) {
            Log.Error(func, $"h = {h} or w = {w} > {MaxArraySize}");
            throw Lept.Arg(func, "kernel too large");  /* C returns NULL here, without ERROR_PTR */
        }
        line = sa.GetString(first + 1, L.L_NOCOPY);
        if (CFormat.Sscanf(line, "%d %d", out v) != 2)
            throw Lept.Arg(func, "error reading cy,cx");
        int cy = (int)v[0]!, cx = (int)v[1]!;

            /* Extract the data.  This ends when we reach eof, or when we
             * encounter a line of data that is either a null string or
             * contains just a newline. */
        Numa na = Numa.Create(0);
        for (i = first + 2; i < nlines; i++) {
            line = sa.GetString(i, L.L_NOCOPY);
            char c0 = FirstChar(line);
            if (c0 == '\0' || c0 == '\n' || c0 == '#')
                break;
            Numa nat = ParseStringForNumbers(line, " \t\n");
            na.Join(nat, 0, -1);
        }

        int n = na.Count;
        if (n != w * h) {
            CStreamText.Stderr("w = %d, h = %d, num ints = %d\n", w, h, n);
            throw Lept.Arg(func, "invalid integer data");
        }

        L_Kernel kel = Create(h, w);
        kel.SetOrigin(cy, cx);
        int index = 0;
        for (i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                float val = na.GetFValue(index);
                kel.SetElement(i, j, val);
                index++;
            }
        }

        return kel;
    }

    /// <summary>line[0] of a C string: '\0' for the empty string.</summary>
    private static char FirstChar(string line) => line.Length > 0 ? line[0] : '\0';

    /*----------------------------------------------------------------------*
     *                       Making a kernel from a Pix                     *
     *----------------------------------------------------------------------*/
    // leptonica: kernelCreateFromPix (kernel.c)
    /// <summary>Makes a kernel from an 8 bpp pix.</summary>
    /// <param name="pix">8 bpp</param>
    /// <param name="cy">origin of kernel</param>
    /// <param name="cx">origin of kernel</param>
    /// <remarks>
    /// (1) The origin must be positive and within the dimensions of the pix.
    /// </remarks>
    public static L_Kernel CreateFromPix(Pix pix, int cy, int cx)
    {
        const string func = "kernelCreateFromPix";
        pix.GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pix not 8 bpp");
        if (cy < 0 || cx < 0 || cy >= h || cx >= w)
            throw Lept.Arg(func, "(cy, cx) invalid");

        L_Kernel kel = Create(h, w);
        kel.SetOrigin(cy, cx);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                pix.GetPixel(j, i, out uint val);
                kel.SetElement(i, j, (float)val);
            }
        }

        return kel;
    }

    /*----------------------------------------------------------------------*
     *                     Display a kernel in a pix                        *
     *----------------------------------------------------------------------*/
    // leptonica: kernelDisplayInPix (kernel.c)
    /// <summary>Displays the kernel in a pix.</summary>
    /// <param name="size">of grid interiors; odd; either 1 or a minimum size
    /// of 17 is enforced</param>
    /// <param name="gthick">grid thickness; either 0 or a minimum size of 2
    /// is enforced</param>
    /// <returns>display of kernel</returns>
    /// <remarks>
    /// (1) This gives a visual representation of a kernel.
    /// (2) There are two modes of display:
    ///     (a) Grid lines of minimum width 2, surrounding regions
    ///         representing kernel elements of minimum size 17,
    ///         with a "plus" mark at the kernel origin, or
    ///     (b) A pix without grid lines and using 1 pixel per kernel element.
    /// (3) For both cases, the kernel absolute value is displayed,
    ///     normalized such that the maximum absolute value is 255.
    /// (4) Large 2D separable kernels should be used for convolution
    ///     with two 1D kernels.  However, for the bilateral filter,
    ///     the computation time is independent of the size of the
    ///     2D content kernel.
    /// </remarks>
    public Pix DisplayInPix(int size, int gthick)
    {
        const string func = "kernelDisplayInPix";
        Pix pixd;
        float val;
        int normval;

            /* Normalize the max value to be 255 for display */
        GetParameters(out int sy, out int sx, out int cy, out int cx);
        GetMinMax(out float minval, out float maxval);
        float max = Math.Max(maxval, -minval);  /* L_MAX */
        if (max == 0.0)
            throw Lept.Arg(func, "kernel elements all 0.0");
        float norm = 255.0f / max;

            /* Handle the 1 element/pixel case; typically with large kernels */
        if (size == 1 && gthick == 0) {
            pixd = Pix.Create(sx, sy, 8);
            for (int i = 0; i < sy; i++) {
                for (int j = 0; j < sx; j++) {
                    val = GetElement(i, j);
                    normval = (int)(norm * (val < 0 ? -val : val));
                    pixd.SetPixel(j, i, (uint)normval);
                }
            }
            return pixd;
        }

            /* Enforce the constraints for the grid line version */
        if (size < 17) {
            Log.Warning(func, "size < 17; setting to 17");
            size = 17;
        }
        if (size % 2 == 0)
            size++;
        if (gthick < 2) {
            Log.Warning(func, "grid thickness < 2; setting to 2");
            gthick = 2;
        }

        int w = size * sx + gthick * (sx + 1);
        int h = size * sy + gthick * (sy + 1);
        pixd = Pix.Create(w, h, 8);

            /* Generate grid lines */
        for (int i = 0; i <= sy; i++)
            pixd.RenderLine(0, gthick / 2 + i * (size + gthick),
                            w - 1, gthick / 2 + i * (size + gthick),
                            gthick, L.L_SET_PIXELS);
        for (int j = 0; j <= sx; j++)
            pixd.RenderLine(gthick / 2 + j * (size + gthick), 0,
                            gthick / 2 + j * (size + gthick), h - 1,
                            gthick, L.L_SET_PIXELS);

            /* Generate mask for each element */
        Pix pixt0 = Pix.Create(size, size, 1);
        pixt0.SetAll();

            /* Generate crossed lines for origin pattern */
        Pix pixt1 = Pix.Create(size, size, 1);
        int width = size / 8;
        pixt1.RenderLine(size / 2, (int)(0.12 * size),
                         size / 2, (int)(0.88 * size),
                         width, L.L_SET_PIXELS);
        pixt1.RenderLine((int)(0.15 * size), size / 2,
                         (int)(0.85 * size), size / 2,
                         width, L.L_FLIP_PIXELS);
        pixt1.Rasterop(size / 2 - width, size / 2 - width,
                       2 * width, 2 * width, L.PIX_NOT(L.PIX_DST), null, 0, 0);

            /* Paste the patterns in */
        int y0 = gthick;
        for (int i = 0; i < sy; i++) {
            int x0 = gthick;
            for (int j = 0; j < sx; j++) {
                val = GetElement(i, j);
                normval = (int)(norm * (val < 0 ? -val : val));
                pixd.SetMaskedGeneral(pixt0, (uint)normval, x0, y0);
                if (i == cy && j == cx)
                    pixd.PaintThroughMask(pixt1, x0, y0, (uint)(255 - normval));
                x0 += size + gthick;
            }
            y0 += size + gthick;
        }

        return pixd;
    }

    /*------------------------------------------------------------------------*
     *                     Parse string to extract numbers                    *
     *------------------------------------------------------------------------*/
    // leptonica: parseStringForNumbers (kernel.c)
    /// <summary>Extracts the numbers in a string.</summary>
    /// <param name="str">string containing numbers; not changed</param>
    /// <param name="seps">string of characters that can be used between ints</param>
    /// <returns>numa of numbers found</returns>
    /// <remarks>
    /// (1) The numbers can be ints or floats.
    /// </remarks>
    public static Numa ParseStringForNumbers(string str, string seps)
    {
        Numa na = Numa.Create(0);
        string? tail = null;
        string? head = Utils.StrtokSafe(str, seps, ref tail);
        // NOTE(upstream): atof(NULL) crashes in C when str has no number (empty or only separators);
        // the port throws instead.
        if (head == null)
            throw Lept.Fail("parseStringForNumbers", "no numbers found");
        float val = (float)Atof(head);
        na.AddNumber(val);
        while ((head = Utils.StrtokSafe(null, seps, ref tail)) != null) {
            val = (float)Atof(head);
            na.AddNumber(val);
        }

        return na;
    }

    /// <summary>atof(): the leading number of the string, or 0.0 if there is none.</summary>
    private static double Atof(string s)
    {
        if (CFormat.Sscanf(s, "%lf", out object?[] v) == 1)
            return (double)v[0]!;
        return 0.0;
    }

    /*------------------------------------------------------------------------*
     *                        Simple parametric kernels                       *
     *------------------------------------------------------------------------*/
    // leptonica: makeFlatKernel (kernel.c)
    /// <param name="height">kernel height</param>
    /// <param name="width">kernel width</param>
    /// <param name="cy">origin of kernel</param>
    /// <param name="cx">origin of kernel</param>
    /// <remarks>
    /// (1) This is the same low-pass filtering kernel that is used
    ///     in the block convolution functions.
    /// (2) The kernel origin (%cy, %cx) is typically placed as near
    ///     the center of the kernel as possible.  If height and
    ///     width are odd, then using %cy = height / 2 and
    ///     %cx = width / 2 places the origin at the exact center.
    /// (3) This returns a normalized kernel.
    /// </remarks>
    public static L_Kernel MakeFlatKernel(int height, int width, int cy, int cx)
    {
        L_Kernel kel = Create(height, width);
        kel.SetOrigin(cy, cx);
        float normval = 1.0f / (float)(height * width);
        for (int i = 0; i < height; i++) {
            for (int j = 0; j < width; j++) {
                kel.SetElement(i, j, normval);
            }
        }

        return kel;
    }

    // leptonica: makeGaussianKernel (kernel.c)
    /// <param name="halfh">sy = 2 * halfh + 1</param>
    /// <param name="halfw">sx = 2 * halfw + 1</param>
    /// <param name="stdev">standard deviation</param>
    /// <param name="max">value at (cx,cy)</param>
    /// <remarks>
    /// (1) The kernel size (sx, sy) = (2 * %halfw + 1, 2 * %halfh + 1)
    /// (2) The kernel center (cx, cy) = (%halfw, %halfh).
    /// (3) %halfw and %halfh are typically equal, and
    ///     are typically several times larger than the standard deviation.
    /// (4) If pixConvolve() is invoked with normalization (the sum of
    ///     kernel elements = 1.0), use 1.0 for max (or any number that's
    ///     not too small or too large).
    /// </remarks>
    public static L_Kernel MakeGaussianKernel(int halfh, int halfw, float stdev, float max)
    {
        int sx = 2 * halfw + 1;
        int sy = 2 * halfh + 1;
        L_Kernel kel = Create(sy, sx);
        kel.SetOrigin(halfh, halfw);
        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                    /* the division is in double; expf() takes the float conversion of it */
                float val = MathF.Exp((float)(-(float)((i - halfh) * (i - halfh) +
                                                       (j - halfw) * (j - halfw)) /
                                              (2.0 * stdev * stdev)));
                kel.SetElement(i, j, max * val);
            }
        }

        return kel;
    }

    // leptonica: makeGaussianKernelSep (kernel.c)
    /// <param name="halfh">sy = 2 * halfh + 1</param>
    /// <param name="halfw">sx = 2 * halfw + 1</param>
    /// <param name="stdev">standard deviation</param>
    /// <param name="max">value at (cx,cy)</param>
    /// <param name="pkelx">x part of kernel</param>
    /// <param name="pkely">y part of kernel</param>
    /// <remarks>
    /// (1) See makeGaussianKernel() for description of input parameters.
    /// (2) These kernels are constructed so that the result of both
    ///     normalized and un-normalized convolution will be the same
    ///     as when convolving with pixConvolve() using the full kernel.
    /// (3) The trick for the un-normalized convolution is to have the
    ///     product of the two kernel elements at (cx,cy) be equal to %max,
    ///     not max**2.  That's why %max for kely is 1.0.  If instead
    ///     we use sqrt(%max) for both, the results are slightly less
    ///     accurate, when compared to using the full kernel in
    ///     makeGaussianKernel().
    /// </remarks>
    public static void MakeGaussianKernelSep(int halfh, int halfw, float stdev, float max,
                                             out L_Kernel pkelx, out L_Kernel pkely)
    {
        pkelx = MakeGaussianKernel(0, halfw, stdev, max);
        pkely = MakeGaussianKernel(halfh, 0, stdev, 1.0f);
    }

    // leptonica: makeDoGKernel (kernel.c)
    /// <param name="halfh">sy = 2 * halfh + 1</param>
    /// <param name="halfw">sx = 2 * halfw + 1</param>
    /// <param name="stdev">standard deviation of narrower gaussian</param>
    /// <param name="ratio">of stdev for wide filter to stdev for narrow one</param>
    /// <remarks>
    /// (1) The DoG (difference of gaussians) is a wavelet mother
    ///     function with null total sum.  By subtracting two blurred
    ///     versions of the image, it acts as a bandpass filter for
    ///     frequencies passed by the narrow gaussian but stopped
    ///     by the wide one.See:
    ///          http://en.wikipedia.org/wiki/Difference_of_Gaussians
    /// (2) The kernel size (sx, sy) = (2 * halfw + 1, 2 * halfh + 1).
    /// (3) The kernel center (cx, cy) = (halfw, halfh).
    /// (4) %halfw and %halfh are typically equal, and are typically
    ///     several times larger than the standard deviation.
    /// (5) %ratio is the ratio of standard deviations of the wide
    ///     to narrow gaussian.  It must be >= 1.0; 1.0 is a no-op.
    /// (6) Because the kernel is a null sum, it must be invoked without
    ///     normalization in pixConvolve().
    /// </remarks>
    public static L_Kernel MakeDoGKernel(int halfh, int halfw, float stdev, float ratio)
    {
        int sx = 2 * halfw + 1;
        int sy = 2 * halfh + 1;
        L_Kernel kel = Create(sy, sx);
        kel.SetOrigin(halfh, halfw);

        float pi = 3.1415926535f;
        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                float squaredist = (float)((i - halfh) * (i - halfh) +
                                           (j - halfw) * (j - halfw));
                float highnorm = 1.0f / (2 * stdev * stdev);
                float lownorm = highnorm / (ratio * ratio);
                float val = (highnorm / pi) * MathF.Exp(-(highnorm * squaredist))
                            - (lownorm / pi) * MathF.Exp(-(lownorm * squaredist));
                kel.SetElement(i, j, val);
            }
        }

        return kel;
    }
}
