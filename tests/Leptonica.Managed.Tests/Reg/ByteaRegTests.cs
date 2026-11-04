using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// bytea_reg.c: tests the L_Bytea byte array utility (init, copy, join, split,
/// string/binary append, sequence search, and stream write).
/// </summary>
public class ByteaRegTests
{
    [RegFact]
    public void Bytea()
    {
        var rp = new RegTest("bytea");
        string progDir = TestPaths.ProgDir;

        Utils.LeptMkdir("lept/bytea");

        L_Bytea lba1 = L_Bytea.InitFromFile(Path.Combine(progDir, "feyn.tif"));
        L_Bytea lba2 = L_Bytea.InitFromFile(Path.Combine(progDir, "test24.jpg"));
        L_Bytea lba3 = lba2.Copy(L.L_COPY);
        int size1 = lba1.Size;
        int size2 = lba2.Size;
        lba1.Join(lba3);
        lba2.Write("/tmp/lept/bytea/lba2.bya", 0, 0);
        rp.CheckFile("/tmp/lept/bytea/lba2.bya");  /* 0 */

        byte[] d1 = lba1.GetData(out _);
        byte[] d2 = lba2.GetData(out _);
        lba3 = L_Bytea.InitFromMem(d1.AsSpan(0, size1));
        L_Bytea lba4 = L_Bytea.InitFromMem(d1.AsSpan(size1, size2));
        byte[] d3 = lba3.GetData(out int sz3);
        byte[] d4 = lba4.GetData(out int sz4);
        rp.CompareStrings(d1.AsSpan(0, size1), d3.AsSpan(0, sz3));  /* 1 */
        rp.CompareStrings(d2.AsSpan(0, size2), d4.AsSpan(0, sz4));  /* 2 */

        lba1.Split(size1, out lba4);
        d4 = lba4.GetData(out sz4);
        rp.CompareStrings(d2.AsSpan(0, size2), d4.AsSpan(0, sz4));  /* 3 */

        byte[] rawKernel = Utils.BinaryRead(Path.Combine(progDir, "kernel_reg.c"));
        string str1 = Utils.StringRemoveChars(Encoding.UTF8.GetString(rawKernel), "\r");
        lba1 = L_Bytea.InitFromMem(Encoding.UTF8.GetBytes(str1));
        Sarray sa = Sarray.CreateLinesFromString(str1, 1);
        lba2 = L_Bytea.Create(0);
        int n = sa.Count;
        for (int i = 0; i < n; i++) {
            string str2 = sa.GetString(i, L.L_NOCOPY);
            lba2.AppendString(str2);
            lba2.AppendString("\n");
        }
        d1 = lba1.GetData(out size1);
        d2 = lba2.GetData(out size2);
        rp.CompareStrings(d1.AsSpan(0, size1), d2.AsSpan(0, size2));  /* 4 */

        string breviarPath = Path.Combine(progDir, "breviar.38.150.jpg");
        const int slice = 1000;
        long total = Utils.NbytesInFile(breviarPath);
        lba1 = L_Bytea.Create(100);
        n = (int)(2 + total / slice);
        for (int i = 0, start = 0; i < n; i++, start += slice) {
            if (start > total)
                continue;
            byte[] chunk = Utils.BinaryReadSelect(breviarPath, start, slice);
            lba1.AppendData(chunk);
        }
        d1 = lba1.GetData(out size1);
        d2 = Utils.BinaryRead(breviarPath);
        rp.CompareStrings(d1.AsSpan(0, size1), d2);  /* 5 */

        PdfIo.ConvertToPdf(Path.Combine(progDir, "test24.jpg"), L.L_JPEG_ENCODE, 0,
                           "/tmp/lept/bytea/test24.pdf", 0, 0, 100, null);
        lba1 = L_Bytea.InitFromFile("/tmp/lept/bytea/test24.pdf");
        L_Dna? da = lba1.FindEachSequence(" 0 obj\n"u8);
        n = da?.Count ?? 0;
        rp.CompareValues(6, n, 0.0f);  /* 6 */

        lba1 = L_Bytea.InitFromFile(Path.Combine(progDir, "feyn.tif"));
        size1 = lba1.Size;
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/bytea/feyn.dat", "wb")) {
            for (int start = 0; start < size1; start += 1000)
                lba1.WriteStream(fp, start, 1000);
        }
        lba2 = L_Bytea.InitFromFile("/tmp/lept/bytea/feyn.dat");
        d1 = lba1.GetData(out size1);
        d2 = lba2.GetData(out size2);
        rp.CompareStrings(d1.AsSpan(0, size1), d2.AsSpan(0, size2));  /* 7 */

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void ByteaNoJpeg()
    {
        Pix.LeptDebugOK = true;
        string progDir = TestPaths.ProgDir;
        string goldenDir = TestPaths.GoldenDir!;

        Utils.LeptMkdir("lept/bytea");

            /* 0: Test basic init, copy, join, and write vs bytea_golden.00.bya */
        L_Bytea lba1 = L_Bytea.InitFromFile(Path.Combine(progDir, "feyn.tif"));
        L_Bytea lba2 = L_Bytea.InitFromFile(Path.Combine(progDir, "test24.jpg"));
        L_Bytea lba3 = lba2.Copy(L.L_COPY);
        int size1 = lba1.Size;
        int size2 = lba2.Size;
        lba1.Join(lba3);
        string lba2Path = Utils.GenPathname("/tmp/lept/bytea/lba2.bya", null);
        lba2.Write(lba2Path, 0, 0);
        Assert.Equal(File.ReadAllBytes(Path.Combine(goldenDir, "bytea_golden.00.bya")),
                     File.ReadAllBytes(lba2Path));

            /* 1, 2: Test split using init from memory */
        byte[] d1 = lba1.GetData(out _);
        byte[] d2 = lba2.GetData(out _);
        lba3 = L_Bytea.InitFromMem(d1.AsSpan(0, size1));
        L_Bytea lba4 = L_Bytea.InitFromMem(d1.AsSpan(size1, size2));
        byte[] d3 = lba3.GetData(out int sz3);
        byte[] d4 = lba4.GetData(out int sz4);
        Assert.True(Utils.BinaryCompare(d1.AsSpan(0, size1), d3.AsSpan(0, sz3)));
        Assert.True(Utils.BinaryCompare(d2.AsSpan(0, size2), d4.AsSpan(0, sz4)));

            /* 3: Test split using Split() */
        lba1.Split(size1, out lba4);
        d4 = lba4.GetData(out sz4);
        Assert.True(Utils.BinaryCompare(d2.AsSpan(0, size2), d4.AsSpan(0, sz4)));

            /* 4: Test appending with strings */
        byte[] rawKernel = Utils.BinaryRead(Path.Combine(progDir, "kernel_reg.c"));
        string str1 = Utils.StringRemoveChars(Encoding.UTF8.GetString(rawKernel), "\r");
        lba1 = L_Bytea.InitFromMem(Encoding.UTF8.GetBytes(str1));
        Sarray sa = Sarray.CreateLinesFromString(str1, 1);
        lba2 = L_Bytea.Create(0);
        for (int i = 0; i < sa.Count; i++) {
            lba2.AppendString(sa.GetString(i, L.L_NOCOPY));
            lba2.AppendString("\n");
        }
        d1 = lba1.GetData(out size1);
        d2 = lba2.GetData(out size2);
        Assert.True(Utils.BinaryCompare(d1.AsSpan(0, size1), d2.AsSpan(0, size2)));

            /* 5: Test appending with binary data */
        string breviarPath = Path.Combine(progDir, "breviar.38.150.jpg");
        const int slice = 1000;
        long total = Utils.NbytesInFile(breviarPath);
        lba1 = L_Bytea.Create(100);
        int n = (int)(2 + total / slice);
        for (int i = 0, start = 0; i < n; i++, start += slice) {
            if (start > total)
                continue;
            byte[] chunk = Utils.BinaryReadSelect(breviarPath, start, slice);
            lba1.AppendData(chunk);
        }
        d1 = lba1.GetData(out size1);
        d2 = Utils.BinaryRead(breviarPath);
        Assert.True(Utils.BinaryCompare(d1.AsSpan(0, size1), d2));

            /* 6: Test sequence search on a 6-object single-page non-cmap PDF (weasel32.png) */
        string pdfPath = Utils.GenPathname("/tmp/lept/bytea/weasel32.pdf", null);
        PdfIo.ConvertToPdf(Path.Combine(progDir, "weasel32.png"), L.L_FLATE_ENCODE, 0,
                           pdfPath, 0, 0, 100, null);
        lba1 = L_Bytea.InitFromFile(pdfPath);
        L_Dna? da = lba1.FindEachSequence(" 0 obj\n"u8);
        Assert.NotNull(da);
        Assert.Equal(6, da!.Count);

            /* 7: Test write to file in chunks */
        lba1 = L_Bytea.InitFromFile(Path.Combine(progDir, "feyn.tif"));
        size1 = lba1.Size;
        string feynDat = Utils.GenPathname("/tmp/lept/bytea/feyn.dat", null);
        using (FileStream fp = Utils.FopenWriteStream(feynDat, "wb")) {
            for (int start = 0; start < size1; start += 1000)
                lba1.WriteStream(fp, start, 1000);
        }
        lba2 = L_Bytea.InitFromFile(feynDat);
        d1 = lba1.GetData(out size1);
        d2 = lba2.GetData(out size2);
        Assert.True(Utils.BinaryCompare(d1.AsSpan(0, size1), d2.AsSpan(0, size2)));
    }
}
