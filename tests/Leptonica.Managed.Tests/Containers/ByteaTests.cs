using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.ContainersTests;

public class ByteaTests
{
    private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

    private static string Text(L_Bytea ba)
    {
        byte[] data = ba.GetData(out int size);
        return Encoding.ASCII.GetString(data, 0, size);
    }

    [Fact]
    public void Create_AllocatesNullTerminatedArray()
    {
        L_Bytea ba = L_Bytea.Create(10);
        Assert.Equal(11, ba.Nalloc);
        Assert.Equal(0, ba.Size);
        Assert.Equal(201, L_Bytea.Create(0).Nalloc);
        Assert.Equal(201, L_Bytea.Create(-5).Nalloc);
        Assert.Equal(201, L_Bytea.Create(int.MaxValue).Nalloc);
    }

    [Fact]
    public void InitFromMem_CopiesData()
    {
        byte[] src = Bytes("hello");
        L_Bytea ba = L_Bytea.InitFromMem(src);
        src[0] = (byte)'j';
        Assert.Equal("hello", Text(ba));
        Assert.Equal(5, ba.Size);
        Assert.Throws<ArgumentException>(() => L_Bytea.InitFromMem(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Copy_CloneSharesCopyDoesNot()
    {
        L_Bytea ba = L_Bytea.InitFromMem(Bytes("abc"));
        Assert.Same(ba, ba.Copy(L.L_CLONE));
        L_Bytea cp = ba.Copy(L.L_COPY);
        Assert.NotSame(ba, cp);
        ba.AppendString("def");
        Assert.Equal("abc", Text(cp));
        Assert.Equal("abcdef", Text(ba));
    }

    [Fact]
    public void CopyData_ReturnsZeroPaddedCopy()
    {
        L_Bytea ba = L_Bytea.InitFromMem(Bytes("xyz"));
        byte[] copy = ba.CopyData(out int size);
        Assert.Equal(3, size);
        Assert.Equal(new byte[] { (byte)'x', (byte)'y', (byte)'z', 0, 0, 0, 0 }, copy);
    }

    [Fact]
    public void Append_GrowsAndKeepsNullTerminator()
    {
        L_Bytea ba = L_Bytea.Create(4);
        ba.AppendString("abcd");
        Assert.Equal(5, ba.Nalloc);   // exactly fits, no growth needed
        ba.AppendData(Bytes("efghij"));
        Assert.Equal("abcdefghij", Text(ba));
        Assert.True(ba.Nalloc >= 11);
        byte[] data = ba.GetData(out int size);
        Assert.Equal(0, data[size]);
        // growth is by twice the required size
        Assert.Equal(2 * 11, ba.Nalloc);
    }

    [Fact]
    public void Join_AppendsOtherArray()
    {
        L_Bytea a = L_Bytea.InitFromMem(Bytes("foo"));
        L_Bytea b = L_Bytea.InitFromMem(Bytes("bar"));
        a.Join(b);
        Assert.Equal("foobar", Text(a));
        a.Join(null);
        Assert.Equal("foobar", Text(a));
    }

    [Fact]
    public void Split_SeparatesAndNullsTail()
    {
        L_Bytea a = L_Bytea.InitFromMem(Bytes("foobar"));
        a.Split(3, out L_Bytea b);
        Assert.Equal("foo", Text(a));
        Assert.Equal("bar", Text(b));
        byte[] data = a.GetData(out _);
        Assert.Equal(0, data[3]);
        Assert.Equal(0, data[5]);
        Assert.Throws<ArgumentException>(() => a.Split(3, out _));
    }

    [Fact]
    public void WriteStream_HonorsStartAndCount()
    {
        L_Bytea a = L_Bytea.InitFromMem(Bytes("0123456789"));
        var ms = new MemoryStream();
        a.WriteStream(ms, 2, 3);
        Assert.Equal("234", Encoding.ASCII.GetString(ms.ToArray()));
        ms = new MemoryStream();
        a.WriteStream(ms, 7, 0);
        Assert.Equal("789", Encoding.ASCII.GetString(ms.ToArray()));
        ms = new MemoryStream();
        a.WriteStream(ms, 7, 100);
        Assert.Equal("789", Encoding.ASCII.GetString(ms.ToArray()));
        Assert.Throws<ArgumentException>(() => a.WriteStream(new MemoryStream(), 10, 0));
    }

    [Fact]
    public void WriteAndInitFromFile_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bytea_{Guid.NewGuid():N}.bin");
        try {
            L_Bytea a = L_Bytea.InitFromMem(new byte[] { 1, 0, 2, 0, 3 });
            a.Write(path, 0, 0);
            L_Bytea b = L_Bytea.InitFromFile(path);
            Assert.Equal(5, b.Size);
            Assert.Equal(new byte[] { 1, 0, 2, 0, 3 }, b.GetData(out _).AsSpan(0, 5).ToArray());
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void FindEachSequence_FindsNonOverlappingMatches()
    {
        L_Bytea a = L_Bytea.InitFromMem(Bytes("baaabbbaa"));
        L_Dna da = a.FindEachSequence(Bytes("aa"))!;
        Assert.Equal(2, da.Count);
        Assert.Equal(1.0, da.GetDValue(0));
        Assert.Equal(7.0, da.GetDValue(1));
    }
}
