using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  bytearray.c
 *
 *   Functions for handling byte arrays, in analogy with C++ 'strings'
 *
 *      Creation, copy, clone
 *           L_Bytea.Create (l_byteaCreate)
 *           L_Bytea.InitFromMem (l_byteaInitFromMem)
 *           L_Bytea.InitFromFile (l_byteaInitFromFile)
 *           L_Bytea.InitFromStream (l_byteaInitFromStream)
 *           L_Bytea.Copy (l_byteaCopy)
 *
 *      Accessors
 *           L_Bytea.Size (l_byteaGetSize)
 *           L_Bytea.GetData (l_byteaGetData)
 *           L_Bytea.CopyData (l_byteaCopyData)
 *
 *      Appending
 *           L_Bytea.AppendData (l_byteaAppendData)
 *           L_Bytea.AppendString (l_byteaAppendString)
 *           private L_Bytea.ExtendArrayToSize (l_byteaExtendArrayToSize)
 *
 *      Join/Split
 *           L_Bytea.Join (l_byteaJoin)
 *           L_Bytea.Split (l_byteaSplit)
 *
 *      Search
 *           L_Bytea.FindEachSequence (l_byteaFindEachSequence)
 *
 *      Output to file
 *           L_Bytea.Write (l_byteaWrite)
 *           L_Bytea.WriteStream (l_byteaWriteStream)
 *
 *   The internal data array is always null-terminated, for ease of use
 *   in the event that it is an ascii string without null bytes.
 *
 *  Differences from C:
 *    - l_byteaDestroy is not ported (the GC owns the object); there is no
 *      reference count, so L_CLONE returns the same object.
 *    - l_byteaJoin leaves ba2 untouched (there is no destroy).
 *    - Sizes are int (the maximum array size is 10^9 bytes).
 */

/// <summary>Array of bytes with a size and a null terminator (struct L_Bytea).</summary>
public sealed class L_Bytea
{
        /* Bounds on array size */
    private const int MaxArraySize = 1000000000;   /* 10^9 bytes */
    private const int InitialArraySize = 200;      /* n'importe quoi */

    private byte[] data;      /* data array (length is the allocated size)  */
    private int size;         /* size of data in the array                  */

    private L_Bytea(int nbytes)
    {
        data = new byte[nbytes + 1];
    }

    /// <summary>Number of allocated bytes, including the null terminator.</summary>
    public int Nalloc => data.Length;

    /*---------------------------------------------------------------------*
     *                  Creation, copy, clone, destruction                 *
     *---------------------------------------------------------------------*/
    // leptonica: l_byteaCreate (bytearray.c)
    /// <summary>Creates a byte array.</summary>
    /// <param name="nbytes">determines initial size of data array</param>
    /// <remarks>
    /// (1) The allocated array is n + 1 bytes.  This allows room
    ///     for null termination.
    /// </remarks>
    public static L_Bytea Create(int nbytes)
    {
        if (nbytes <= 0 || nbytes > MaxArraySize)
            nbytes = InitialArraySize;
        return new L_Bytea(nbytes);
    }

    // leptonica: l_byteaInitFromMem (bytearray.c)
    /// <summary>Creates a byte array initialized with the given data.</summary>
    /// <param name="data">to be copied to the array</param>
    public static L_Bytea InitFromMem(ReadOnlySpan<byte> data)
    {
        const string func = "l_byteaInitFromMem";
        int size = data.Length;
        if (size <= 0)
            throw Lept.Arg(func, "no bytes to initialize");
        if (size > MaxArraySize)
            throw Lept.Arg(func, "size is too big");

        L_Bytea ba = Create(size);
        data.CopyTo(ba.data);
        ba.size = size;
        return ba;
    }

    // leptonica: l_byteaInitFromFile (bytearray.c)
    /// <summary>Creates a byte array initialized with the contents of a file.</summary>
    public static L_Bytea InitFromFile(string fname)
    {
        using FileStream fp = Utils.FopenReadStream(fname);
        return InitFromStream(fp);
    }

    // leptonica: l_byteaInitFromStream (bytearray.c)
    /// <summary>Creates a byte array initialized with the rest of a stream.</summary>
    /// <param name="fp">file stream</param>
    public static L_Bytea InitFromStream(Stream fp)
    {
        byte[] bytes = Utils.BinaryReadStream(fp);
        L_Bytea ba = Create(bytes.Length);
        bytes.CopyTo(ba.data, 0);
        ba.size = bytes.Length;
        return ba;
    }

    // leptonica: l_byteaCopy (bytearray.c)
    /// <summary>Makes a copy or a clone of the byte array.</summary>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    /// <returns>clone or copy of this array</returns>
    /// <remarks>
    /// (1) If cloning, return this array.
    /// </remarks>
    public L_Bytea Copy(int copyflag)
    {
        if (copyflag == L.L_CLONE)
            return this;

        return InitFromMem(data.AsSpan(0, size));
    }

    /*---------------------------------------------------------------------*
     *                               Accessors                             *
     *---------------------------------------------------------------------*/
    // leptonica: l_byteaGetSize (bytearray.c)
    /// <summary>Size of the stored byte array.</summary>
    public int Size => size;

    // leptonica: l_byteaGetData (bytearray.c)
    /// <summary>Gets the internal data array and the size of the data in it.</summary>
    /// <param name="psize">size of data in lba</param>
    /// <returns>the existing data array; its length can be larger than the size</returns>
    /// <remarks>
    /// (1) The returned array is owned by this object.
    /// </remarks>
    public byte[] GetData(out int psize)
    {
        psize = size;
        return data;
    }

    // leptonica: l_byteaCopyData (bytearray.c)
    /// <summary>Gets a copy of the data in use in the data array.</summary>
    /// <param name="psize">size of data in lba</param>
    /// <returns>copy of data in use in the data array</returns>
    /// <remarks>
    /// (1) The returned data is owned by the caller.  This object
    ///     still owns the original data array.
    /// </remarks>
    public byte[] CopyData(out int psize)
    {
        byte[] bytes = GetData(out psize);
        return Utils.BinaryCopy(bytes.AsSpan(0, psize));
    }

    /*---------------------------------------------------------------------*
     *                               Appending                             *
     *---------------------------------------------------------------------*/
    // leptonica: l_byteaAppendData (bytearray.c)
    /// <summary>Appends bytes to the array.</summary>
    /// <param name="newdata">byte array to be appended</param>
    public void AppendData(ReadOnlySpan<byte> newdata)
    {
        int newbytes = newdata.Length;
        long reqsize = (long)size + newbytes + 1;
        int nalloc = data.Length;
        if (nalloc < reqsize) {
            if (!ExtendArrayToSize(2 * reqsize))
                throw Lept.Fail("l_byteaAppendData", "extension failed");
        }

        newdata.CopyTo(data.AsSpan(size));
        size += newbytes;
    }

    // leptonica: l_byteaAppendString (bytearray.c)
    /// <summary>Appends a string to the array.</summary>
    /// <param name="str">string to be appended (its UTF-8 bytes)</param>
    public void AppendString(string str)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(str);
        int len = bytes.Length;
        long reqsize = (long)size + len + 1;
        int nalloc = data.Length;
        if (nalloc < reqsize) {
            if (!ExtendArrayToSize(2 * reqsize))
                throw Lept.Fail("l_byteaAppendString", "extension failed");
        }

        bytes.CopyTo(data, size);
        size += len;
    }

    // leptonica: l_byteaExtendArrayToSize (bytearray.c)
    /// <summary>Reallocs the byte array to the given size, if necessary.</summary>
    /// <param name="newsize">new size of lba data array</param>
    /// <returns>true if OK; false if the size is too large</returns>
    /// <remarks>
    /// (1) If necessary, reallocs the byte array to size.
    /// (2) The max buffer size is 1 GB.
    /// </remarks>
    private bool ExtendArrayToSize(long newsize)
    {
        const string func = "l_byteaExtendArrayToSize";
        if (data.Length > MaxArraySize) {  /* belt & suspenders */
            Log.Error(func, "ba has too many ptrs");
            return false;
        }
        if (newsize > MaxArraySize) {
            Log.Error(func, "size > 1 GB; too large");
            return false;
        }
        if (newsize <= data.Length) {
            Log.Info(func, "size too small; no extension\n");
            return true;
        }

        Array.Resize(ref data, (int)newsize);
        return true;
    }

    /*---------------------------------------------------------------------*
     *                        String join/split                            *
     *---------------------------------------------------------------------*/
    // leptonica: l_byteaJoin (bytearray.c)
    /// <summary>Appends the data of another array to this one.</summary>
    /// <param name="ba2">data array is added to the one in this array</param>
    /// <remarks>
    /// (1) It is a no-op, not an error, for ba2 to be null.
    /// </remarks>
    public void Join(L_Bytea? ba2)
    {
        if (ba2 == null) return;

        byte[] data2 = ba2.GetData(out int nbytes2);
        AppendData(data2.AsSpan(0, nbytes2));
    }

    // leptonica: l_byteaSplit (bytearray.c)
    /// <summary>Splits the array at the given location.</summary>
    /// <param name="splitloc">location in ba1 to split; ba2 begins there</param>
    /// <param name="pba2">with data starting at splitloc</param>
    /// <remarks>
    /// The bytes of this array beyond the split location are nulled.
    /// </remarks>
    public void Split(int splitloc, out L_Bytea pba2)
    {
        byte[] data1 = GetData(out int nbytes1);
        if (splitloc < 0 || splitloc >= nbytes1)
            throw Lept.Arg("l_byteaSplit", "splitloc invalid");
        int nbytes2 = nbytes1 - splitloc;

            /* Make the new lba */
        pba2 = InitFromMem(data1.AsSpan(splitloc, nbytes2));

            /* Null the removed bytes in the input lba */
        Array.Clear(data1, splitloc, nbytes2);
        size = splitloc;
    }

    /*---------------------------------------------------------------------*
     *                                Search                               *
     *---------------------------------------------------------------------*/
    // leptonica: l_byteaFindEachSequence (bytearray.c)
    /// <summary>Finds each non-overlapping occurrence of a byte sequence.</summary>
    /// <param name="sequence">subarray of bytes to find in data</param>
    /// <returns>byte positions of each occurrence of the sequence; null if none are found</returns>
    public L_Dna? FindEachSequence(ReadOnlySpan<byte> sequence)
    {
        byte[] bytes = GetData(out int sz);
        return Utils.ArrayFindEachSequence(bytes.AsSpan(0, sz), sequence);
    }

    /*---------------------------------------------------------------------*
     *                              Output to file                         *
     *---------------------------------------------------------------------*/
    // leptonica: l_byteaWrite (bytearray.c)
    /// <summary>Writes the data to a file.</summary>
    /// <param name="fname">output file</param>
    /// <param name="startloc">first byte to output</param>
    /// <param name="nbytes">number of bytes to write; use 0 to write to
    ///                      the end of the data array</param>
    public void Write(string fname, int startloc, int nbytes)
    {
        using FileStream fp = Utils.FopenWriteStream(fname, "wb");
        WriteStream(fp, startloc, nbytes);
    }

    // leptonica: l_byteaWriteStream (bytearray.c)
    /// <summary>Writes the data to a stream.</summary>
    /// <param name="fp">file stream opened for binary write</param>
    /// <param name="startloc">first byte to output</param>
    /// <param name="nbytes">number of bytes to write; use 0 to write to
    ///                      the end of the data array</param>
    public void WriteStream(Stream fp, int startloc, int nbytes)
    {
        byte[] bytes = GetData(out int sz);
        if (startloc < 0 || startloc >= sz)
            throw Lept.Arg("l_byteaWriteStream", "invalid startloc");
        int maxbytes = sz - startloc;
        nbytes = (nbytes == 0) ? maxbytes : Math.Min(nbytes, maxbytes);

        fp.Write(bytes, startloc, nbytes);
    }
}
