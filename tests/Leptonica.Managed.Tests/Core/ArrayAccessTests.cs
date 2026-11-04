using System;
using Leptonica;
using static Leptonica.ArrayAccess;

namespace Leptonica.Managed.Tests.Core;

public class ArrayAccessTests
{
    [Fact]
    public void BitsAreMsbFirst()
    {
        uint[] line = new uint[2];
        SetDataBit(line, 0);
        SetDataBit(line, 33);
        Assert.Equal(0x80000000u, line[0]);
        Assert.Equal(0x40000000u, line[1]);
        Assert.Equal(1, GetDataBit(line, 33));
        ClearDataBit(line, 0);
        Assert.Equal(0u, line[0]);
        SetDataBitVal(line, 31, 1);
        Assert.Equal(1u, line[0]);
    }

    [Fact]
    public void SubByteAccess()
    {
        uint[] line = new uint[1];
        SetDataDibit(line, 1, 3);
        Assert.Equal(0x30000000u, line[0]);
        SetDataQbit(line, 7, 0xa);
        Assert.Equal(0x3000000au, line[0]);
        Assert.Equal(3, GetDataDibit(line, 1));
        Assert.Equal(0xa, GetDataQbit(line, 7));
    }

    [Fact]
    public void ByteAndTwoByteOrderMatchLittleEndianMacros()
    {
        /* On little-endian machines, GET_DATA_BYTE(line, n) reads byte n ^ 3 of
         * the memory, which is byte n of the word in big-endian order. */
        uint[] line = { 0x11223344, 0x55667788 };
        Assert.Equal(0x11, GetDataByte(line, 0));
        Assert.Equal(0x44, GetDataByte(line, 3));
        Assert.Equal(0x55, GetDataByte(line, 4));
        Assert.Equal(0x1122, GetDataTwoBytes(line, 0));
        Assert.Equal(0x7788, GetDataTwoBytes(line, 3));

        SetDataByte(line, 5, 0xab);
        Assert.Equal(0x55ab7788u, line[1]);
        SetDataTwoBytes(line, 0, 0xbeef);
        Assert.Equal(0xbeef3344u, line[0]);

        uint word = 0;
        SetDataByte(ref word, L.COLOR_RED, 0x12);
        SetDataByte(ref word, L.L_ALPHA_CHANNEL, 0xff);
        Assert.Equal(0x120000ffu, word);
        Assert.Equal(0x12, GetDataByte(word, L.COLOR_RED));
    }
}
