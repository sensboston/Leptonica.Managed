using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.BarcodeTests;

public class BarDecodeTests
{
    private static string Reverse(string s)
    {
        char[] a = s.ToCharArray();
        Array.Reverse(a);
        return new string(a);
    }

    [Fact]
    public void DispatchDecoderDecodesForwardAndReversedFormats()
    {
        Assert.True(Barcode.FormatIsSupported(L.L_BF_CODE39));
        Assert.True(Barcode.FormatIsSupported(L.L_BF_UPCA));
        Assert.False(Barcode.FormatIsSupported(L.L_BF_CODE128));

            /* Code 39: *123* */
        string c39 = "121121211" + "1" + "211211112" + "1" + "112211112" + "1" + "212211111" + "1" + "121121211";
        Assert.Equal("123", Barcode.DispatchDecoder(c39, L.L_BF_ANY, 0));
        Assert.Equal("123", Barcode.DispatchDecoder(Reverse(c39), L.L_BF_ANY, 0));

            /* Code 2 of 5: Start + "42" + Stop */
        string c25 = "21211" + "1" + "111121112" + "1" + "112111112" + "1" + "21112";
        Assert.Equal("42", Barcode.DispatchDecoder(c25, L.L_BF_ANY, 0));
        Assert.Equal("42", Barcode.DispatchDecoder(Reverse(c25), L.L_BF_ANY, 0));

            /* Codabar: A + "015" + B */
        string codabar = "1122121" + "1" + "1111122" + "1" + "1111221" + "1" + "2111121" + "1" + "1212112";
        Assert.Equal("015", Barcode.DispatchDecoder(codabar, L.L_BF_ANY, 0));
        Assert.Equal("015", Barcode.DispatchDecoder(Reverse(codabar), L.L_BF_ANY, 0));

            /* UPC-A: 012345678905 (59 bars) */
        string upca = "111"
            + "3211" + "2221" + "2122" + "1411" + "1132" + "1231"
            + "11111"
            + "1114" + "1312" + "1213" + "3112" + "3211" + "1231"
            + "111";
        Assert.Equal("012345678905", Barcode.DispatchDecoder(upca, L.L_BF_UPCA, 0));
        Assert.Equal("012345678905", Barcode.DispatchDecoder(Reverse(upca), L.L_BF_UPCA, 0));
    }
}
