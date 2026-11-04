using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  colormorph.c
 *
 *      Top-level color morphological operations
 *
 *            Pix.ColorMorph (pixColorMorph)
 *
 *      Method: Algorithm by van Herk and Gil and Werman, 1992
 *              Apply grayscale morphological operations separately
 *              to each component.
 */
public sealed partial class Pix
{
    /*-----------------------------------------------------------------*
     *              Top-level color morphological operations           *
     *-----------------------------------------------------------------*/
    // leptonica: pixColorMorph (colormorph.c)
    /// <summary>Applies a gray morphological operation to each component of a 32 bpp rgb pix.</summary>
    /// <param name="type">L_MORPH_DILATE, L_MORPH_ERODE, L_MORPH_OPEN, or L_MORPH_CLOSE</param>
    /// <param name="hsize">width of Sel; must be odd; origin implicitly in center</param>
    /// <param name="vsize">ditto for height of Sel</param>
    /// <returns>pixd</returns>
    public Pix ColorMorph(int type, int hsize, int vsize)
    {
        const string func = "pixColorMorph";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (type != L.L_MORPH_DILATE && type != L.L_MORPH_ERODE &&
            type != L.L_MORPH_OPEN && type != L.L_MORPH_CLOSE)
            throw Lept.Arg(func, "invalid morph type");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize or vsize < 1");
        if ((hsize & 1) == 0) {
            Log.Warning(func, "horiz sel size must be odd; increasing by 1");
            hsize++;
        }
        if ((vsize & 1) == 0) {
            Log.Warning(func, "vert sel size must be odd; increasing by 1");
            vsize++;
        }

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        if (Colormap == null &&
            LeptGpu.TryGetActive((long)Width * Height, (hsize + vsize) * (type is L.L_MORPH_OPEN or L.L_MORPH_CLOSE ? 6 : 3), out IGpuAccelerator? gpu) &&
            gpu.TryColorMorph(this, type, hsize, vsize, out Pix? gpuPix)) {
            return gpuPix;
        }

        SplitRGBFast(out Pix pixr, out Pix pixg, out Pix pixb);
        Pix pixrm, pixgm, pixbm;
        if (type == L.L_MORPH_DILATE) {
            pixrm = pixr.DilateGray(hsize, vsize);
            pixgm = pixg.DilateGray(hsize, vsize);
            pixbm = pixb.DilateGray(hsize, vsize);
        } else if (type == L.L_MORPH_ERODE) {
            pixrm = pixr.ErodeGray(hsize, vsize);
            pixgm = pixg.ErodeGray(hsize, vsize);
            pixbm = pixb.ErodeGray(hsize, vsize);
        } else if (type == L.L_MORPH_OPEN) {
            pixrm = pixr.OpenGray(hsize, vsize);
            pixgm = pixg.OpenGray(hsize, vsize);
            pixbm = pixb.OpenGray(hsize, vsize);
        } else {  /* type == L.L_MORPH_CLOSE */
            pixrm = pixr.CloseGray(hsize, vsize);
            pixgm = pixg.CloseGray(hsize, vsize);
            pixbm = pixb.CloseGray(hsize, vsize);
        }
        return CombineRGBFast(pixrm, pixgm, pixbm);
    }

    /// <summary>Single-pass 32-bpp RGB split into three 8-bpp channel Pix (bit-identical to three GetRGBComponent calls).</summary>
    internal void SplitRGBFast(out Pix pixr, out Pix pixg, out Pix pixb)
    {
        if (Colormap != null) {
            pixr = GetRGBComponent(L.COLOR_RED);
            pixg = GetRGBComponent(L.COLOR_GREEN);
            pixb = GetRGBComponent(L.COLOR_BLUE);
            return;
        }

        int w = Width, h = Height;
        pixr = CreateNoInit(w, h, 8);
        pixg = CreateNoInit(w, h, 8);
        pixb = CreateNoInit(w, h, 8);
        pixr.CopyResolution(this);
        pixg.CopyResolution(this);
        pixb.CopyResolution(this);

        uint[] datas = Data;
        uint[] datar = pixr.Data, datag = pixg.Data, datab = pixb.Data;
        int wpls = Wpl, wpl8 = pixr.Wpl;
        int fullWords = w >> 2;
        bool hasTail = (w & 3) != 0;

        ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
        ref uint baseR = ref MemoryMarshal.GetArrayDataReference(datar);
        ref uint baseG = ref MemoryMarshal.GetArrayDataReference(datag);
        ref uint baseB = ref MemoryMarshal.GetArrayDataReference(datab);

        for (int i = 0; i < h; i++) {
            ref uint rs = ref Unsafe.Add(ref baseS, (nuint)(i * wpls));
            ref uint rr = ref Unsafe.Add(ref baseR, (nuint)(i * wpl8));
            ref uint rg = ref Unsafe.Add(ref baseG, (nuint)(i * wpl8));
            ref uint rb = ref Unsafe.Add(ref baseB, (nuint)(i * wpl8));

            for (int jw = 0; jw < fullWords; jw++) {
                int j4 = jw << 2;
                uint p0 = Unsafe.Add(ref rs, (nuint)j4);
                uint p1 = Unsafe.Add(ref rs, (nuint)(j4 + 1));
                uint p2 = Unsafe.Add(ref rs, (nuint)(j4 + 2));
                uint p3 = Unsafe.Add(ref rs, (nuint)(j4 + 3));
                Unsafe.Add(ref rr, (nuint)jw) = (p0 & 0xff000000u) | ((p1 >> 8) & 0x00ff0000u) | ((p2 >> 16) & 0x0000ff00u) | (p3 >> 24);
                Unsafe.Add(ref rg, (nuint)jw) = ((p0 << 8) & 0xff000000u) | (p1 & 0x00ff0000u) | ((p2 >> 8) & 0x0000ff00u) | ((p3 >> 16) & 0xffu);
                Unsafe.Add(ref rb, (nuint)jw) = ((p0 << 16) & 0xff000000u) | ((p1 << 8) & 0x00ff0000u) | (p2 & 0x0000ff00u) | ((p3 >> 8) & 0xffu);
            }

            if (hasTail) {
                Unsafe.Add(ref rr, (nuint)fullWords) = 0;
                Unsafe.Add(ref rg, (nuint)fullWords) = 0;
                Unsafe.Add(ref rb, (nuint)fullWords) = 0;
                for (int j = fullWords << 2; j < w; j++) {
                    uint px = Unsafe.Add(ref rs, (nuint)j);
                    ArrayAccess.SetDataByteUnchecked(ref rr, j, (int)(px >> 24));
                    ArrayAccess.SetDataByteUnchecked(ref rg, j, (int)((px >> 16) & 0xff));
                    ArrayAccess.SetDataByteUnchecked(ref rb, j, (int)((px >> 8) & 0xff));
                }
            }
        }
    }

    /// <summary>Single-pass recombination of three 8-bpp channel Pix into a 32-bpp RGB Pix (bit-identical to CreateRGBImage).</summary>
    internal static Pix CombineRGBFast(Pix pixr, Pix pixg, Pix pixb)
    {
        int w = pixr.Width, h = pixr.Height;
        Pix pixd = CreateNoInit(w, h, 32);
        pixd.CopyResolution(pixr);

        uint[] datar = pixr.Data, datag = pixg.Data, datab = pixb.Data;
        uint[] datad = pixd.Data;
        int wpl8 = pixr.Wpl, wpld = pixd.Wpl;
        int fullWords = w >> 2;

        ref uint baseR = ref MemoryMarshal.GetArrayDataReference(datar);
        ref uint baseG = ref MemoryMarshal.GetArrayDataReference(datag);
        ref uint baseB = ref MemoryMarshal.GetArrayDataReference(datab);
        ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);

        for (int i = 0; i < h; i++) {
            ref uint rr = ref Unsafe.Add(ref baseR, (nuint)(i * wpl8));
            ref uint rg = ref Unsafe.Add(ref baseG, (nuint)(i * wpl8));
            ref uint rb = ref Unsafe.Add(ref baseB, (nuint)(i * wpl8));
            ref uint rd = ref Unsafe.Add(ref baseD, (nuint)(i * wpld));

            for (int jw = 0; jw < fullWords; jw++) {
                uint rw = Unsafe.Add(ref rr, (nuint)jw);
                uint gw = Unsafe.Add(ref rg, (nuint)jw);
                uint bw = Unsafe.Add(ref rb, (nuint)jw);
                int j4 = jw << 2;
                Unsafe.Add(ref rd, (nuint)j4) = (rw & 0xff000000u) | ((gw >> 8) & 0x00ff0000u) | ((bw >> 16) & 0x0000ff00u);
                Unsafe.Add(ref rd, (nuint)(j4 + 1)) = ((rw << 8) & 0xff000000u) | (gw & 0x00ff0000u) | ((bw >> 8) & 0x0000ff00u);
                Unsafe.Add(ref rd, (nuint)(j4 + 2)) = ((rw << 16) & 0xff000000u) | ((gw << 8) & 0x00ff0000u) | (bw & 0x0000ff00u);
                Unsafe.Add(ref rd, (nuint)(j4 + 3)) = (rw << 24) | ((gw << 16) & 0x00ff0000u) | ((bw << 8) & 0x0000ff00u);
            }

            for (int j = fullWords << 2; j < w; j++) {
                uint rv = (uint)ArrayAccess.GetDataByteUnchecked(ref rr, j);
                uint gv = (uint)ArrayAccess.GetDataByteUnchecked(ref rg, j);
                uint bv = (uint)ArrayAccess.GetDataByteUnchecked(ref rb, j);
                Unsafe.Add(ref rd, (nuint)j) = (rv << 24) | (gv << 16) | (bv << 8);
            }
        }

        return pixd;
    }
}
