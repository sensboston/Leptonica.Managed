using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

public class ReadFileTests
{
    [Fact]
    public void ReadWriteDispatchAndHeadersWorkForBmpPngPnmAndSpix()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lept_readfile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            Pix src = Pix.Create(20, 16, 8);
            src.SetPixel(4, 5, 180);

            foreach ((string ext, int fmt) in new[] {
                ("bmp", L.IFF_BMP),
                ("png", L.IFF_PNG),
                ("pnm", L.IFF_PNM),
                ("spix", L.IFF_SPIX)
            }) {
                string file = Path.Combine(dir, $"img.{ext}");
                Pix.Write(file, src, fmt);

                Pix.ReadHeader(file, out int hfmt, out int hw, out int hh, out int hbps, out int hspp, out _);
                Assert.Equal((fmt, 20, 16, 8, 1), (hfmt, hw, hh, hbps, hspp));

                byte[] mem = Pix.WriteMem(src, fmt);
                Pix.ReadHeaderMem(mem, out int mfmt, out int mw, out int mh, out int mbps, out int mspp, out _);
                Assert.Equal((fmt, 20, 16, 8, 1), (mfmt, mw, mh, mbps, mspp));

                Pix fromFile = Pix.ReadWithHint(file, 0);
                Pix fromMem = Pix.ReadMem(mem);
                if (fmt == L.IFF_BMP) {
                    fromFile = fromFile.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
                    fromMem = fromMem.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
                }
                Assert.True(Pix.Equal(src, fromFile));
                Assert.True(Pix.Equal(src, fromMem));
            }

                /* Pixa.ReadFiles and Pix.ReadIndexed */
            Pixa pixa = Pixa.ReadFiles(dir, "img.");
            Assert.Equal(4, pixa.Count);

            Sarray sa = Sarray.Create(3);
            sa.AddString(Path.Combine(dir, "img.png"), L.L_COPY);
            sa.AddString("", L.L_COPY);
            sa.AddString(Path.Combine(dir, "missing.png"), L.L_COPY);
            Assert.NotNull(Pix.ReadIndexed(sa, 0));
            Assert.Null(Pix.ReadIndexed(sa, 1));
            Assert.Null(Pix.ReadIndexed(sa, 2));

                /* WriteImageFileInfo and IoFormatTest */
            string pngPath = Path.Combine(dir, "img.png");
            using var infoMs = new MemoryStream();
            Pix.WriteImageFileInfo(pngPath, infoMs, false);
            string info = Encoding.UTF8.GetString(infoMs.ToArray());
            Assert.Contains("Reading the header:", info);
            Assert.Contains("Reading the full image:", info);

            using (var scope = new LeptDebugScope(true)) {
                Assert.False(Pix.IoFormatTest(pngPath));
            }
        } finally {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
