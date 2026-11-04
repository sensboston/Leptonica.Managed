using System;
using System.IO;

namespace Leptonica;

/*
 *  bbuffer.c: Expandable byte buffer for reading data in and writing it out (fifo).
 *
 *      Create BBuffer
 *          L_BBuffer.Create (bbufferCreate)
 *          L_BBuffer.DestroyAndSaveData (bbufferDestroyAndSaveData)
 *
 *      Operations to read data TO a BBuffer
 *          Read (bbufferRead), ReadStream (bbufferReadStream), ExtendArray (bbufferExtendArray)
 *
 *      Operations to write data FROM a BBuffer
 *          Write (bbufferWrite), WriteStream (bbufferWriteStream)
 *
 *  Not ported: bbufferDestroy (memory management only).
 *
 *  The bbuffer is an implementation of a byte queue.
 *  The bbuffer holds a byte array from which bytes are
 *  processed in a first-in/first-out fashion.  As with
 *  any queue, bbuffer maintains two "pointers," one to the
 *  tail of the queue (where you read new bytes onto it)
 *  and one to the head of the queue (where you start from
 *  when writing bytes out of it.
 *
 *  The queue can be visualized:
 *
 *   byte 0                                           byte (nalloc - 1)
 *        |                                                |
 *        --------------------------------------------------
 *                  H                             T
 *        [   aw   ][  bytes currently on queue  ][  anr   ]
 *
 *        ---:  all allocated data in bbuffer
 *        H:    queue head (ptr to next byte to be written out)
 *        T:    queue tail (ptr to first byte to be written to)
 *        aw:   already written from queue
 *        anr:  allocated but not yet read to
 *
 *  The purpose of bbuffer is to allow you to safely read
 *  bytes in, and to sequentially write them out as well.
 *  In the process of writing bytes out, you don't actually
 *  remove the bytes in the array; you just move the pointer
 *  (nwritten) which points to the head of the queue.  In
 *  the process of reading bytes in, you sometimes need to
 *  expand the array size.  If a read is performed after a
 *  write, so that the head of the queue is not at the
 *  beginning of the array, the bytes already written are
 *  first removed by copying the others over them; then the
 *  new bytes are read onto the tail of the queue.
 *
 *  Note that the meaning of "read into" and "write from"
 *  the bbuffer is OPPOSITE to that for a stream, where
 *  you read "from" a stream and write "into" a stream.
 *  As a mnemonic for remembering the direction:
 *      ~ to read bytes from a stream into the bbuffer,
 *        you call fread on the stream
 *      ~ to write bytes from the bbuffer into a stream,
 *        you call fwrite on the stream
 *
 *  See zlibmem.c for an example use of bbuffer, where we
 *  compress and decompress an array of bytes in memory.
 *
 *  We can also use the bbuffer trivially to read from stdin
 *  into memory; e.g., to capture bytes piped from the stdout
 *  of another program.  This is equivalent to repeatedly
 *  calling bbufferReadStream() until the input queue is empty.
 *  This is implemented in l_binaryReadStream().
 */

/// <summary>Expandable byte buffer, used as a byte queue (struct L_BBuffer).</summary>
public sealed class L_BBuffer
{
    /* Bounds on array size */
    private const uint MaxArraySize = 1000000000;   /* 10^9 bytes */
    private const int InitialArraySize = 1024;      /* n'importe quoi */

    private byte[] array;    /* byte array; its length is nalloc                 */
    private int nalloc;      /* size of allocated byte array                     */
    private int n;           /* number of bytes read into the array              */
    private int nwritten;    /* number of bytes already written from the array   */

    private L_BBuffer(int nalloc)
    {
        array = new byte[nalloc];
        this.nalloc = nalloc;
        nwritten = 0;
        n = 0;
    }

    /// <summary>Size of the allocated byte array.</summary>
    public int NAlloc => nalloc;

    /// <summary>Number of bytes read into the array (including those already written out).</summary>
    public int N => n;

    /// <summary>Number of bytes already written out of the array.</summary>
    public int NWritten => nwritten;

    /*--------------------------------------------------------------------------*
     *                         BBuffer create/destroy                           *
     *--------------------------------------------------------------------------*/
    // leptonica: bbufferCreate (bbuffer.c)
    /// <summary>Creates a bbuffer.</summary>
    /// <param name="indata">[optional] data to be read into the buffer; at least nalloc bytes</param>
    /// <param name="nalloc">size of byte array to be alloc'd; 0 for default</param>
    /// <remarks>
    /// (1) If a buffer address is given, you should read all the data in.
    /// (2) Allocates a bbuffer with associated byte array of
    ///     the given size.  If a buffer address is given,
    ///     it then reads the number of bytes into the byte array.
    /// </remarks>
    public static L_BBuffer Create(ReadOnlySpan<byte> indata, int nalloc)
    {
        if (nalloc <= 0 || nalloc > MaxArraySize)
            nalloc = InitialArraySize;

        L_BBuffer bb = new L_BBuffer(nalloc);
        if (!indata.IsEmpty) {
            if (indata.Length < nalloc)
                throw Lept.Arg("bbufferCreate", "indata is smaller than nalloc");
            indata.Slice(0, nalloc).CopyTo(bb.array);
            bb.n = nalloc;
        } else {
            bb.n = 0;
        }

        return bb;
    }

    // leptonica: bbufferDestroyAndSaveData (bbuffer.c)
    /// <summary>Copies the unwritten data to a new array and invalidates the buffer.</summary>
    /// <returns>newly allocated array of data</returns>
    /// <remarks>
    /// (1) Copies data to newly allocated array; then destroys the bbuffer.
    /// </remarks>
    public byte[] DestroyAndSaveData()
    {
            /* write all unwritten bytes out to a new array */
        int nbytes = n - nwritten;
        byte[] data = new byte[nbytes];
        Array.Copy(array, nwritten, data, 0, nbytes);

        array = Array.Empty<byte>();
        nalloc = 0;
        n = 0;
        nwritten = 0;
        return data;
    }

    /*--------------------------------------------------------------------------*
     *                   Operations to read data INTO a BBuffer                 *
     *--------------------------------------------------------------------------*/
    // leptonica: bbufferRead (bbuffer.c)
    /// <summary>Reads bytes from a memory buffer into the bbuffer.</summary>
    /// <param name="src">source memory buffer from which bytes are read</param>
    /// <param name="nbytes">bytes to be read</param>
    /// <remarks>
    /// (1) For a read after write, first remove the written
    ///     bytes by shifting the unwritten bytes in the array,
    ///     then check if there is enough room to add the new bytes.
    ///     If not, realloc with bbufferExpandArray(), resulting
    ///     in a second writing of the unwritten bytes.  While less
    ///     efficient, this is simpler than making a special case
    ///     of reallocNew().
    /// </remarks>
    public void Read(ReadOnlySpan<byte> src, int nbytes)
    {
        const string func = "bbufferRead";
        if (nbytes == 0)
            throw Lept.Arg(func, "no bytes to read");
        if (nbytes < 0 || nbytes > src.Length)
            throw Lept.Arg(func, "invalid number of bytes");

        int nw = nwritten;
        if (nw != 0) {  /* move the unwritten bytes over */
            Array.Copy(array, nw, array, 0, n - nw);  /* memmove */
            nwritten = 0;
            n -= nw;
        }

            /* If necessary, expand the allocated array.  Do so by
             * by at least a factor of two. */
        int navail = nalloc - n;
        if (nbytes > navail) {
            int nadd = Math.Max(nalloc, nbytes);
            ExtendArray(nadd);
        }

            /* Read in the new bytes */
        src.Slice(0, nbytes).CopyTo(array.AsSpan(n));
        n += nbytes;
    }

    // leptonica: bbufferReadStream (bbuffer.c)
    /// <summary>Reads bytes from a stream into the bbuffer.</summary>
    /// <param name="fp">source stream from which bytes are read</param>
    /// <param name="nbytes">bytes to be read</param>
    public void ReadStream(Stream fp, int nbytes)
    {
        const string func = "bbufferReadStream";
        if (nbytes == 0)
            throw Lept.Arg(func, "no bytes to read");
        if (nbytes < 0)
            throw Lept.Arg(func, "invalid number of bytes");

        int nw = nwritten;
        if (nw != 0) {  /* move any unwritten bytes over */
            Array.Copy(array, nw, array, 0, n - nw);  /* memmove */
            nwritten = 0;
            n -= nw;
        }

            /* If necessary, expand the allocated array.  Do so by
             * by at least a factor of two. */
        int navail = nalloc - n;
        if (nbytes > navail) {
            int nadd = Math.Max(nalloc, nbytes);
            ExtendArray(nadd);
        }

            /* Read in the new bytes (fread: up to nbytes, fewer at the end of the stream) */
        int nread = 0;
        while (nread < nbytes) {
            int got = fp.Read(array, n + nread, nbytes - nread);
            if (got <= 0)
                break;
            nread += got;
        }
        n += nread;
    }

    // leptonica: bbufferExtendArray (bbuffer.c)
    /// <summary>Extends the byte array.</summary>
    /// <param name="nbytes">number of bytes to extend array size</param>
    /// <remarks>
    /// (1) reallocNew() copies all bb->nalloc bytes, even though
    ///     only bb->n are data.
    /// </remarks>
    public void ExtendArray(int nbytes)
    {
        Array.Resize(ref array, nalloc + nbytes);
        nalloc += nbytes;
    }

    /*--------------------------------------------------------------------------*
     *                  Operations to write data FROM a BBuffer                 *
     *--------------------------------------------------------------------------*/
    // leptonica: bbufferWrite (bbuffer.c)
    /// <summary>Writes bytes from the bbuffer to a memory buffer.</summary>
    /// <param name="dest">dest memory buffer to which bytes are written</param>
    /// <param name="nbytes">bytes requested to be written</param>
    /// <returns>bytes actually written</returns>
    public int Write(Span<byte> dest, int nbytes)
    {
        const string func = "bbufferWrite";
        if (nbytes <= 0)
            throw Lept.Arg(func, "no bytes requested to write");

        int nleft = n - nwritten;
        int nout = Math.Min(nleft, nbytes);
        if (nout > dest.Length)
            throw Lept.Arg(func, "dest too small");

        if (nleft == 0) {   /* nothing to write; reinitialize the buffer */
            n = 0;
            nwritten = 0;
            return nout;
        }

            /* nout > 0; transfer the data out */
        array.AsSpan(nwritten, nout).CopyTo(dest);
        nwritten += nout;

            /* If all written; "empty" the buffer */
        if (nout == nleft) {
            n = 0;
            nwritten = 0;
        }

        return nout;
    }

    // leptonica: bbufferWriteStream (bbuffer.c)
    /// <summary>Writes bytes from the bbuffer to a stream.</summary>
    /// <param name="fp">dest stream to which bytes are written</param>
    /// <param name="nbytes">bytes requested to be written</param>
    /// <returns>bytes actually written</returns>
    public int WriteStream(Stream fp, int nbytes)
    {
        const string func = "bbufferWriteStream";
        if (nbytes <= 0)
            throw Lept.Arg(func, "no bytes requested to write");

        int nleft = n - nwritten;
        int nout = Math.Min(nleft, nbytes);

        if (nleft == 0) {   /* nothing to write; reinitialize the buffer */
            n = 0;
            nwritten = 0;
            return nout;
        }

            /* nout > 0; transfer the data out */
        fp.Write(array, nwritten, nout);
        nwritten += nout;

            /* If all written; "empty" the buffer */
        if (nout == nleft) {
            n = 0;
            nwritten = 0;
        }

        return nout;
    }
}
