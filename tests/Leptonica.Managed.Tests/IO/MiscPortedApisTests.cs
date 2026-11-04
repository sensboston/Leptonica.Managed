using System;
using System.IO;
using System.Text;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

[Collection("LeptDebugOK")]
public class MiscPortedApisTests : IDisposable
{
    private readonly string dir;

    public MiscPortedApisTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_misc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public void GetImagelibVersions_ListsAllSevenManagedBackends()
    {
        string versions = Utils.GetImagelibVersions();
        Assert.Contains("libgif", versions);
        Assert.Contains("libjpeg", versions);
        Assert.Contains("libpng", versions);
        Assert.Contains("libtiff", versions);
        Assert.Contains("zlib", versions);
        Assert.Contains("libwebp", versions);
        Assert.Contains("libopenjp2", versions);
    }

    [Fact]
    public void ParseForProtos_ExtractsLeptonicaCFunctionDeclarations()
    {
        string cFile = Path.Combine(dir, "sample.i");
        File.WriteAllText(cFile, """
            # 1 "sample.c"
            static l_int32 helper_internal(l_int32 x)
            {
                return x + 1;
            }

            PIX *
            pixDoSomething(PIX *pixs,
                           l_int32 factor,
                           l_float32 *pscore)
            {
                return NULL;
            }

            l_ok
            pixCheckThing(const char *fname)
            {
                return 0;
            }
            """);

        string protos = Utils.ParseForProtos(cFile, "LEPT_DLL ");
        Assert.Contains("LEPT_DLL extern PIX * pixDoSomething ( PIX *pixs, l_int32 factor, l_float32 *pscore );", protos);
        Assert.Contains("LEPT_DLL extern l_ok pixCheckThing ( const char *fname );", protos);
        Assert.DoesNotContain("helper_internal", protos);
    }

    [Fact]
    public void QueuePrint_And_StackPrint_WriteFormattedDiagnostics()
    {
        var q = L_Queue<string>.Create(4);
        q.Add("alpha");
        q.Add("beta");
        using var qms = new MemoryStream();
        q.Print(qms);
        string qOut = Encoding.UTF8.GetString(qms.ToArray());
        Assert.Contains("L_Queue:", qOut);
        Assert.Contains("nelem = 2", qOut);

        var st = L_Stack.Create(4);
        st.Add("one");
        st.Add("two");
        using var sms = new MemoryStream();
        st.Print(sms);
        string sOut = Encoding.UTF8.GetString(sms.ToArray());
        Assert.Contains("Stack:", sOut);
        Assert.Contains("n = 2", sOut);
    }

    [Fact]
    public void AllFormatsReadWriteDispatch_And_UpstreamIoFormatTest()
    {
        Pix rgb = Pix.Create(48, 36, 32);
        for (int y = 0; y < 36; y++)
            for (int x = 0; x < 48; x++)
                rgb.SetRGBPixel(x, y, x * 5, y * 6, 100);

        foreach (int fmt in new[] {
            L.IFF_BMP, L.IFF_JFIF_JPEG, L.IFF_PNG, L.IFF_TIFF,
            L.IFF_TIFF_LZW, L.IFF_TIFF_ZIP, L.IFF_PNM, L.IFF_GIF,
            L.IFF_JP2, L.IFF_WEBP, L.IFF_SPIX
        }) {
            byte[] mem = Pix.WriteMem(rgb, fmt);
            Assert.NotEmpty(mem);
            Pix back = Pix.ReadMem(mem);
            Assert.Equal((48, 36), (back.Width, back.Height));
        }

        if (!Directory.Exists(TestPaths.ProgDir))
            return;

        using (new LeptDebugScope(true)) {
            foreach (string fn in new[] {
                "test1.bmp", "feyn.tif", "speckle2.png", "weasel2.4g.png",
                "speckle4.png", "weasel4.16c.png", "dreyfus8.png", "weasel8.240c.png",
                "test8.jpg", "test16.tif", "marge.jpg", "test32-alpha.png",
                "test-1bit-alpha.png", "test-cmap-alpha.png",
                "test-fulltrans-alpha.png", "test-gray-alpha.png"
            }) {
                Assert.False(Pix.IoFormatTest(TestPaths.ProgFile(fn)),
                             $"Pix.IoFormatTest reported failure on {fn}");
            }
        }
    }
}
