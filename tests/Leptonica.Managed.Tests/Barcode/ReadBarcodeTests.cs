using System.Collections.Generic;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.BarcodeTests;

public class ReadBarcodeTests
{
    private static List<string> NonEmpty(Sarray sa)
    {
        var list = new List<string>();
        for (int i = 0; i < sa.Count; i++) {
            string s = sa.GetString(i, L.L_NOCOPY);
            if (!string.IsNullOrEmpty(s))
                list.Add(s);
        }
        return list;
    }

    [RegFact]
    public void ProcessBarcodesDecodesRealBarcodeImagesForwardAndRotated180()
    {
        string[] supportedFiles = {
            "barcode-2of5-300.png",
            "barcode-39-300.png",
            "barcode-93-300.png",
            "barcode-codabar-300.png",
            "barcode-i2of5-300.png",
            "barcode-upc-300.png"
        };

        foreach (string fname in supportedFiles) {
            Pix pixs = TestPaths.ReadInput(fname);
            Sarray? sad1 = pixs.ProcessBarcodes(L.L_BF_ANY, L.L_USE_WIDTHS, out Sarray? saw1, 0);
            Assert.NotNull(sad1);
            Assert.NotNull(saw1);
            List<string> dec1 = NonEmpty(sad1!);
            Assert.NotEmpty(dec1);

                /* Also decode after 180 degree rotation (as in prog/barcodetest.c) */
            Pix pixRot = Pix.Rotate180(null, pixs);
            Sarray? sad2 = pixRot.ProcessBarcodes(L.L_BF_ANY, L.L_USE_WIDTHS, out _, 0);
            Assert.NotNull(sad2);
            List<string> dec2 = NonEmpty(sad2!);
            Assert.Contains(dec1[0], dec2);
        }

            /* Skewed Code128 page barcode (barcodetest1.jpg): locates, deskews, and extracts widths */
        Pix sheet = TestPaths.ReadInput("barcodetest1.jpg");
        Sarray? sheetSad = sheet.ProcessBarcodes(L.L_BF_ANY, L.L_USE_WIDTHS, out Sarray? sheetSaw, 0);
        Assert.NotNull(sheetSad);
        Assert.NotNull(sheetSaw);
        Assert.True(sheetSaw!.Count >= 1);
        Assert.True(sheetSaw.GetString(0, L.L_NOCOPY).Length >= 20);

            /* Also exercise L_USE_WINDOWS on barcode-39-300.png */
        Pix pix39 = TestPaths.ReadInput("barcode-39-300.png");
        Sarray? sadWin = pix39.ProcessBarcodes(L.L_BF_CODE39, L.L_USE_WINDOWS, out _, 0);
        Assert.NotNull(sadWin);
        Assert.NotEmpty(NonEmpty(sadWin!));
    }
}
