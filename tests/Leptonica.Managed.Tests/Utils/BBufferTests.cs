using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.UtilsTests;

public class BBufferTests
{
    private static byte[] Seq(int start, int count)
    {
        byte[] b = new byte[count];
        for (int i = 0; i < count; i++)
            b[i] = (byte)(start + i);
        return b;
    }

    [Fact]
    public void Create_DefaultAndExplicitSize()
    {
        Assert.Equal(1024, L_BBuffer.Create(default, 0).NAlloc);
        Assert.Equal(1024, L_BBuffer.Create(default, -4).NAlloc);
        Assert.Equal(64, L_BBuffer.Create(default, 64).NAlloc);
        Assert.Equal(0, L_BBuffer.Create(default, 64).N);
    }

    [Fact]
    public void Create_WithData()
    {
        byte[] data = Seq(10, 20);
        var bb = L_BBuffer.Create(data, 20);
        Assert.Equal((20, 20, 0), (bb.NAlloc, bb.N, bb.NWritten));
        byte[] outp = new byte[20];
        Assert.Equal(20, bb.Write(outp, 100));
        Assert.Equal(data, outp);
        Assert.Throws<ArgumentException>(() => L_BBuffer.Create(Seq(0, 5), 20));
    }

    [Fact]
    public void ReadThenWrite_IsFifo()
    {
        var bb = L_BBuffer.Create(default, 16);
        bb.Read(Seq(0, 5), 5);
        bb.Read(Seq(5, 5), 5);
        Assert.Equal(10, bb.N);
        byte[] outp = new byte[4];
        Assert.Equal(4, bb.Write(outp, 4));
        Assert.Equal(Seq(0, 4), outp);
        Assert.Equal(4, bb.NWritten);
        outp = new byte[20];
        Assert.Equal(6, bb.Write(outp, 20));   /* only 6 are left */
        Assert.Equal(Seq(4, 6), outp[..6]);
        /* fully written: the buffer is emptied */
        Assert.Equal((0, 0), (bb.N, bb.NWritten));
    }

    [Fact]
    public void Read_AfterPartialWrite_ShiftsAndGrows()
    {
        var bb = L_BBuffer.Create(default, 20);
        bb.Read(Seq(0, 18), 18);
        byte[] outp = new byte[10];
        bb.Write(outp, 10);
        Assert.Equal(10, bb.NWritten);
        /* 8 unwritten bytes move to the front; 15 more fit into the 20 allocated?  8 + 15 = 23 > 20 */
        bb.Read(Seq(100, 15), 15);
        Assert.Equal(0, bb.NWritten);
        Assert.Equal(23, bb.N);
        Assert.Equal(40, bb.NAlloc);           /* grown by max(nalloc, nbytes) = 20 */
        byte[] all = new byte[23];
        Assert.Equal(23, bb.Write(all, 23));
        Assert.Equal(Seq(10, 8), all[..8]);
        Assert.Equal(Seq(100, 15), all[8..]);
    }

    [Fact]
    public void Read_GrowsByAtLeastTheRequest()
    {
        var bb = L_BBuffer.Create(default, 20);
        bb.Read(new byte[100], 100);
        Assert.Equal(120, bb.NAlloc);          /* 20 + max(20, 100) */
        Assert.Equal(100, bb.N);
    }

    [Fact]
    public void Read_InvalidArguments()
    {
        var bb = L_BBuffer.Create(default, 20);
        Assert.Throws<ArgumentException>(() => bb.Read(Seq(0, 5), 0));
        Assert.Throws<ArgumentException>(() => bb.Read(Seq(0, 5), 6));
        Assert.Throws<ArgumentException>(() => bb.Write(new byte[4], 0));
    }

    [Fact]
    public void Write_FromEmptyBuffer_ReturnsZero()
    {
        var bb = L_BBuffer.Create(default, 20);
        Assert.Equal(0, bb.Write(new byte[4], 4));
        Assert.Equal(0, bb.WriteStream(new MemoryStream(), 4));
    }

    [Fact]
    public void ReadStream_ReadsUpToTheEndOfTheStream()
    {
        var bb = L_BBuffer.Create(default, 20);
        using var ms = new MemoryStream(Seq(1, 30));
        bb.ReadStream(ms, 50);       /* only 30 are available */
        Assert.Equal(30, bb.N);
        Assert.True(bb.NAlloc >= 50);
        byte[] outp = new byte[30];
        bb.Write(outp, 30);
        Assert.Equal(Seq(1, 30), outp);
    }

    [Fact]
    public void WriteStream_WritesTheQueue()
    {
        var bb = L_BBuffer.Create(default, 20);
        bb.Read(Seq(0, 12), 12);
        using var ms = new MemoryStream();
        Assert.Equal(5, bb.WriteStream(ms, 5));
        Assert.Equal(7, bb.WriteStream(ms, 100));
        Assert.Equal(Seq(0, 12), ms.ToArray());
        Assert.Equal((0, 0), (bb.N, bb.NWritten));
    }

    [Fact]
    public void DestroyAndSaveData_ReturnsTheUnwrittenBytes()
    {
        var bb = L_BBuffer.Create(default, 20);
        bb.Read(Seq(0, 12), 12);
        bb.Write(new byte[5], 5);
        byte[] rest = bb.DestroyAndSaveData();
        Assert.Equal(Seq(5, 7), rest);
        Assert.Empty(bb.DestroyAndSaveData());
    }

    [Fact]
    public void ExtendArray_KeepsTheData()
    {
        var bb = L_BBuffer.Create(Seq(0, 20), 20);
        bb.ExtendArray(30);
        Assert.Equal(50, bb.NAlloc);
        byte[] outp = new byte[20];
        bb.Write(outp, 20);
        Assert.Equal(Seq(0, 20), outp);
    }
}
