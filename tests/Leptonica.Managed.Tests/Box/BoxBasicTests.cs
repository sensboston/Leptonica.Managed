using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

[Collection("LeptDebugOK")]
public class BoxBasicTests
{
    private static string Text(byte[] data) => Encoding.ASCII.GetString(data);

    private static Boxa MakeBoxa(int count)
    {
        Boxa boxa = Boxa.Create(0);
        for (int i = 0; i < count; i++)
            boxa.AddBox(Box.Create(10 * i, 20 * i, 30 + i, 40 + i), L.L_INSERT);
        return boxa;
    }

    /*------------------------------- Box -------------------------------*/

    [Fact]
    public void BoxCreate_ClipsToPlusQuad()
    {
        Box box = Box.Create(-5, -10, 20, 30);
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Assert.Equal((0, 0, 15, 20), (x, y, w, h));
    }

    [Fact]
    public void BoxCreate_Errors()
    {
        Assert.Throws<ArgumentException>(() => Box.Create(0, 0, -1, 5));
        Assert.Throws<ArgumentException>(() => Box.Create(0, 0, 5, -1));
        Assert.Throws<ArgumentException>(() => Box.Create(-10, 0, 10, 5));
        Assert.Throws<ArgumentException>(() => Box.Create(0, -10, 5, 10));
    }

    [Fact]
    public void BoxCreate_AllowsZeroSizeButCreateValidDoesNot()
    {
        Box box = Box.Create(1, 2, 0, 0);
        Assert.False(box.IsValid());
        Assert.Throws<ArgumentException>(() => Box.CreateValid(1, 2, 0, 5));
        Assert.Throws<ArgumentException>(() => Box.CreateValid(1, 2, 5, 0));
        Assert.True(Box.CreateValid(1, 2, 5, 5).IsValid());
    }

    [Fact]
    public void BoxCopyAndClone()
    {
        Box box = Box.Create(1, 2, 3, 4);
        Box copy = box.Copy();
        Box clone = box.Clone();
        Assert.NotSame(box, copy);
        Assert.Same(box, clone);

        box.SetGeometry(9, -1, -1, -1);
        Assert.Equal(9, clone.X);
        Assert.Equal(1, copy.X);
    }

    [Fact]
    public void BoxSetGeometry_MinusOneLeavesUnchanged()
    {
        Box box = Box.Create(1, 2, 3, 4);
        box.SetGeometry(-1, 20, -1, 40);
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Assert.Equal((1, 20, 3, 40), (x, y, w, h));
    }

    [Fact]
    public void BoxSideLocations()
    {
        Box box = Box.Create(10, 20, 30, 40);
        box.GetSideLocations(out int l, out int r, out int t, out int b);
        Assert.Equal((10, 39, 20, 59), (l, r, t, b));

        box.SetSideLocations(5, -1, -1, 100);
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Assert.Equal((5, 20, 35, 81), (x, y, w, h));

        box.SetSideLocations(-1, 50, 30, -1);
        box.GetGeometry(out x, out y, out w, out h);
        Assert.Equal((5, 30, 46, 71), (x, y, w, h));
    }

    [Fact]
    public void BoxPrintStreamInfo_Format()
    {
        using MemoryStream ms = new MemoryStream();
        Box.Create(1, 2, 3, 4).PrintStreamInfo(ms);
        Assert.Equal(" Box: x = 1, y = 2, w = 3, h = 4\n", Text(ms.ToArray()));
    }

    /*------------------------------- Boxa ------------------------------*/

    [Fact]
    public void BoxaCreate_DefaultAndExplicitSize()
    {
        Assert.Equal(20, Boxa.Create(0).Nalloc);
        Assert.Equal(20, Boxa.Create(-3).Nalloc);
        Assert.Equal(20, Boxa.Create(10000001).Nalloc);
        Assert.Equal(7, Boxa.Create(7).Nalloc);
        Assert.Equal(0, Boxa.Create(7).Count);
    }

    [Fact]
    public void BoxaAddBox_CopyFlags()
    {
        Boxa boxa = Boxa.Create(2);
        Box box = Box.Create(1, 2, 3, 4);
        boxa.AddBox(box, L.L_INSERT);
        boxa.AddBox(box, L.L_COPY);
        boxa.AddBox(box, L.L_CLONE);
        Assert.Equal(3, boxa.Count);
        Assert.Equal(4, boxa.Nalloc);   // doubled when the third box was added
        Assert.Same(box, boxa.GetBox(0, L.L_CLONE));
        Assert.NotSame(box, boxa.GetBox(1, L.L_CLONE));
        Assert.Same(box, boxa.GetBox(2, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => boxa.AddBox(box, 99));
    }

    [Fact]
    public void BoxaGetBox_CopyVersusClone()
    {
        Boxa boxa = MakeBoxa(3);
        Box clone = boxa.GetBox(1, L.L_CLONE);
        Box copy = boxa.GetBox(1, L.L_COPY);
        clone.SetGeometry(77, -1, -1, -1);
        Assert.Equal(77, boxa.GetBox(1, L.L_CLONE).X);
        Assert.Equal(10, copy.X);
        Assert.Throws<ArgumentException>(() => boxa.GetBox(3, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => boxa.GetBox(-1, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => boxa.GetBox(0, 99));
    }

    [Fact]
    public void BoxaCopy_Flags()
    {
        Boxa boxa = MakeBoxa(3);
        Assert.Same(boxa, boxa.Copy(L.L_CLONE));

        Boxa copy = boxa.Copy(L.L_COPY);
        Assert.NotSame(boxa, copy);
        Assert.Equal(3, copy.Count);
        Assert.Equal(boxa.Nalloc, copy.Nalloc);
        Assert.NotSame(boxa.GetBox(1, L.L_CLONE), copy.GetBox(1, L.L_CLONE));

        Boxa cc = boxa.Copy(L.L_COPY_CLONE);
        Assert.NotSame(boxa, cc);
        Assert.Same(boxa.GetBox(1, L.L_CLONE), cc.GetBox(1, L.L_CLONE));

        Assert.Throws<ArgumentException>(() => boxa.Copy(99));
    }

    [Fact]
    public void BoxaExtendArray()
    {
        Boxa boxa = Boxa.Create(4);
        boxa.ExtendArray();
        Assert.Equal(8, boxa.Nalloc);
        boxa.ExtendArrayToSize(5);          // too small: no change
        Assert.Equal(8, boxa.Nalloc);
        boxa.ExtendArrayToSize(100);
        Assert.Equal(100, boxa.Nalloc);
        Assert.Throws<ArgumentException>(() => boxa.ExtendArrayToSize(10000001));
        Assert.Equal(100, boxa.Nalloc);
    }

    [Fact]
    public void BoxaInsertBox()
    {
        Boxa boxa = MakeBoxa(3);
        Box box = Box.Create(500, 600, 7, 8);
        boxa.InsertBox(1, box);
        Assert.Equal(4, boxa.Count);
        Assert.Equal(0, boxa.GetBox(0, L.L_CLONE).X);
        Assert.Same(box, boxa.GetBox(1, L.L_CLONE));
        Assert.Equal(10, boxa.GetBox(2, L.L_CLONE).X);
        Assert.Equal(20, boxa.GetBox(3, L.L_CLONE).X);

        boxa.InsertBox(4, Box.Create(1, 1, 1, 1));   // append position is allowed
        Assert.Equal(5, boxa.Count);
        Assert.Throws<ArgumentException>(() => boxa.InsertBox(6, box));
        Assert.Throws<ArgumentException>(() => boxa.InsertBox(-1, box));
    }

    [Fact]
    public void BoxaInsertBox_ExtendsArray()
    {
        Boxa boxa = Boxa.Create(2);
        for (int i = 0; i < 5; i++)
            boxa.InsertBox(0, Box.Create(i, 0, 1, 1));
        Assert.Equal(5, boxa.Count);
        Assert.Equal(8, boxa.Nalloc);
        Assert.Equal(4, boxa.GetBox(0, L.L_CLONE).X);
        Assert.Equal(0, boxa.GetBox(4, L.L_CLONE).X);
    }

    [Fact]
    public void BoxaRemoveBox_ShiftsAndSaves()
    {
        Boxa boxa = MakeBoxa(4);
        boxa.RemoveBoxAndSave(1, out Box? removed);
        Assert.NotNull(removed);
        Assert.Equal(10, removed!.X);
        Assert.Equal(3, boxa.Count);
        Assert.Equal(20, boxa.GetBox(1, L.L_CLONE).X);
        Assert.Equal(30, boxa.GetBox(2, L.L_CLONE).X);

        boxa.RemoveBox(0);
        Assert.Equal(2, boxa.Count);
        Assert.Equal(20, boxa.GetBox(0, L.L_CLONE).X);
        Assert.Throws<ArgumentException>(() => boxa.RemoveBox(2));
        Assert.Throws<ArgumentException>(() => boxa.RemoveBox(-1));
    }

    [Fact]
    public void BoxaReplaceBox()
    {
        Boxa boxa = MakeBoxa(3);
        Box box = Box.Create(5, 5, 5, 5);
        boxa.ReplaceBox(1, box);
        Assert.Same(box, boxa.GetBox(1, L.L_CLONE));
        Assert.Equal(3, boxa.Count);
        Assert.Throws<ArgumentException>(() => boxa.ReplaceBox(3, box));
    }

    [Fact]
    public void BoxaValidBoxes()
    {
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Box.Create(0, 0, 5, 5), L.L_INSERT);
        boxa.AddBox(Box.Create(0, 0, 0, 5), L.L_INSERT);
        boxa.AddBox(Box.Create(0, 0, 5, 0), L.L_INSERT);
        boxa.AddBox(Box.Create(1, 1, 2, 2), L.L_INSERT);

        Assert.Equal(4, boxa.Count);
        Assert.Equal(2, boxa.GetValidCount());
        Assert.NotNull(boxa.GetValidBox(0, L.L_COPY));
        Assert.Null(boxa.GetValidBox(1, L.L_COPY));
        Assert.Null(boxa.GetValidBox(2, L.L_CLONE));

        Boxa valid = boxa.SaveValid(L.L_COPY);
        Assert.Equal(2, valid.Count);
        Assert.Equal(1, valid.GetBox(1, L.L_CLONE).X);
        Assert.Throws<ArgumentException>(() => boxa.SaveValid(L.L_INSERT));

        // All boxes valid: no invalid boxes
        Assert.Null(MakeBoxa(3).FindInvalidBoxes());
    }

    [Fact]
    public void BoxaGetBoxGeometry()
    {
        Boxa boxa = MakeBoxa(3);
        boxa.GetBoxGeometry(2, out int x, out int y, out int w, out int h);
        Assert.Equal((20, 40, 32, 42), (x, y, w, h));
        Assert.Throws<ArgumentException>(() => boxa.GetBoxGeometry(3, out _, out _, out _, out _));
    }

    [Fact]
    public void BoxaInitFullAndClear()
    {
        Boxa boxa = Boxa.Create(5);
        boxa.InitFull(null);
        Assert.Equal(5, boxa.Count);
        Assert.True(boxa.IsFull());
        Assert.Equal(0, boxa.GetValidCount());
        Assert.Null(boxa.GetValidBox(3, L.L_CLONE));

        Box tmpl = Box.Create(1, 2, 3, 4);
        boxa.InitFull(tmpl);
        Assert.Equal(5, boxa.GetValidCount());
        Assert.NotSame(tmpl, boxa.GetBox(0, L.L_CLONE));
        Assert.NotSame(boxa.GetBox(0, L.L_CLONE), boxa.GetBox(1, L.L_CLONE));

        boxa.Clear();
        Assert.Equal(0, boxa.Count);
        Assert.Equal(5, boxa.Nalloc);
    }

    [Fact]
    public void BoxaIsFull_WithNullPlaceholder()
    {
        Boxa boxa = Boxa.Create(3);
        boxa.ExtendArrayToSize(3);
        boxa.InitFull(Box.Create(1, 1, 1, 1));
        Assert.True(boxa.IsFull());
    }

    /*---------------------------- Boxa serialization --------------------*/

    [Fact]
    public void BoxaWriteMem_ExactFormat()
    {
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Box.Create(10, 20, 30, 40), L.L_INSERT);
        boxa.AddBox(Box.Create(0, 0, 0, 0), L.L_INSERT);
        boxa.AddBox(Box.Create(123456, 7, 8, 9), L.L_INSERT);

        string expected =
            "\nBoxa Version 2\n" +
            "Number of boxes = 3\n" +
            "  Box[0]: x = 10, y = 20, w = 30, h = 40\n" +
            "  Box[1]: x = 0, y = 0, w = 0, h = 0\n" +
            "  Box[2]: x = 123456, y = 7, w = 8, h = 9\n";
        Assert.Equal(expected, Text(boxa.WriteMem()));
    }

    [Fact]
    public void BoxaWriteMem_Empty()
    {
        Assert.Equal("\nBoxa Version 2\nNumber of boxes = 0\n", Text(Boxa.Create(0).WriteMem()));
    }

    [Fact]
    public void BoxaWriteRead_RoundTrip()
    {
        Boxa boxa = MakeBoxa(7);
        byte[] data = boxa.WriteMem();
        Boxa back = Boxa.ReadMem(data);
        Assert.Equal(7, back.Count);
        for (int i = 0; i < 7; i++) {
            boxa.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
            back.GetBoxGeometry(i, out int x2, out int y2, out int w2, out int h2);
            Assert.Equal((x, y, w, h), (x2, y2, w2, h2));
        }
        Assert.Equal(data, back.WriteMem());
    }

    [Fact]
    public void BoxaWriteRead_Empty()
    {
        Boxa back = Boxa.ReadMem(Boxa.Create(0).WriteMem());
        Assert.Equal(0, back.Count);
    }

    [Fact]
    public void BoxaReadStream_ToleratesWhitespaceAndMissingFinalNewline()
    {
        string text = "\n\nBoxa Version 2\n\nNumber of boxes = 2\n Box[0]:   x = 1, y = 2, w = 3, h = 4\n" +
                      "Box[1]: x = -0, y = +5, w = 6, h = 7";
        Boxa boxa = Boxa.ReadMem(Encoding.ASCII.GetBytes(text));
        Assert.Equal(2, boxa.Count);
        boxa.GetBoxGeometry(1, out int x, out int y, out int w, out int h);
        Assert.Equal((0, 5, 6, 7), (x, y, w, h));
    }

    [Fact]
    public void BoxaReadStream_Errors()
    {
        Func<string, Boxa> read = s => Boxa.ReadMem(Encoding.ASCII.GetBytes(s));
        Assert.Throws<ArgumentException>(() => read(""));
        Assert.Throws<ArgumentException>(() => read("\nBoxa Version 1\nNumber of boxes = 0\n"));
        Assert.Throws<ArgumentException>(() => read("\nBoxa Version 2\nNumber of boxes = -1\n"));
        Assert.Throws<ArgumentException>(() => read("\nBoxa Version 2\nNumber of boxes = 10000001\n"));
        Assert.Throws<ArgumentException>(() => read("\nBoxa Version 2\nNumber of boxes = 1\n  Box[0]: x = 1, y = 2\n"));
        Assert.Throws<ArgumentException>(() => read("\nBoxaa Version 2\nNumber of boxes = 0\n"));
    }

    [Fact]
    public void BoxaReadStream_LeavesStreamAfterData()
    {
        byte[] data = Boxa.Create(0).WriteMem();
        byte[] all = new byte[data.Length + 3];
        data.CopyTo(all, 0);
        Encoding.ASCII.GetBytes("XYZ").CopyTo(all, data.Length);
        using MemoryStream ms = new MemoryStream(all);
        Boxa.ReadStream(ms);
        Assert.Equal('X', ms.ReadByte());
    }

    [Fact]
    public void BoxaWriteReadFile_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"boxa_{Guid.NewGuid():N}.ba");
        try {
            Boxa boxa = MakeBoxa(4);
            boxa.Write(path);
            Assert.Equal(boxa.WriteMem(), File.ReadAllBytes(path));
            Boxa back = Boxa.Read(path);
            Assert.Equal(boxa.WriteMem(), back.WriteMem());
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => Boxa.Read(path));
    }

    [Fact]
    public void BoxaWriteDebug_OnlyWhenEnabled()
    {
        string path = Path.Combine(Path.GetTempPath(), $"boxad_{Guid.NewGuid():N}.ba");
        try {
            Boxa boxa = MakeBoxa(2);
            WriteFile.SetLeptDebugOK(false);
            boxa.WriteDebug(path);
            Assert.False(File.Exists(path));
            WriteFile.SetLeptDebugOK(true);
            boxa.WriteDebug(path);
            Assert.True(File.Exists(path));
        } finally {
            WriteFile.SetLeptDebugOK(false);
            File.Delete(path);
        }
    }

    [Fact]
    public void BoxaWriteStream_NullWritesToStderr()
    {
        TextWriter old = Console.Error;
        StringWriter sw = new StringWriter();
        Console.SetError(sw);
        try {
            MakeBoxa(1).WriteStream(null);
        } finally {
            Console.SetError(old);
        }
        Assert.Equal("\nBoxa Version 2\nNumber of boxes = 1\n  Box[0]: x = 0, y = 0, w = 30, h = 40\n",
                     sw.ToString());
    }

    /*------------------------------- Boxaa ------------------------------*/

    private static Boxaa MakeBoxaa()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(MakeBoxa(2), L.L_INSERT);
        baa.AddBoxa(MakeBoxa(3), L.L_INSERT);
        return baa;
    }

    [Fact]
    public void BoxaaCreateAndCounts()
    {
        Assert.Equal(20, Boxaa.Create(0).Nalloc);
        Assert.Equal(20, Boxaa.Create(1000001).Nalloc);
        Boxaa baa = MakeBoxaa();
        Assert.Equal(2, baa.Count);
        Assert.Equal(5, baa.GetBoxCount());
    }

    [Fact]
    public void BoxaaAddBoxa_CopyFlags()
    {
        Boxaa baa = Boxaa.Create(1);
        Boxa boxa = MakeBoxa(2);
        baa.AddBoxa(boxa, L.L_INSERT);
        baa.AddBoxa(boxa, L.L_COPY);
        baa.AddBoxa(boxa, L.L_CLONE);
        Assert.Equal(3, baa.Count);
        Assert.Equal(4, baa.Nalloc);
        Assert.Same(boxa, baa.GetBoxa(0, L.L_CLONE));
        Assert.NotSame(boxa, baa.GetBoxa(1, L.L_CLONE));
        Assert.Same(boxa, baa.GetBoxa(2, L.L_CLONE));
        Assert.NotSame(boxa, baa.GetBoxa(0, L.L_COPY));
        Assert.Throws<ArgumentException>(() => baa.AddBoxa(boxa, 99));
        Assert.Throws<ArgumentException>(() => baa.GetBoxa(3, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => baa.GetBoxa(0, L.L_INSERT));
    }

    [Fact]
    public void BoxaaGetBox()
    {
        Boxaa baa = MakeBoxaa();
        Box? box = baa.GetBox(1, 2, L.L_CLONE);
        Assert.NotNull(box);
        Assert.Equal(20, box!.X);
        Assert.Null(baa.GetBox(0, 5, L.L_CLONE));   // logged, not thrown
        Assert.Throws<ArgumentException>(() => baa.GetBox(2, 0, L.L_CLONE));
    }

    [Fact]
    public void BoxaaCopy()
    {
        Boxaa baa = MakeBoxaa();
        Boxaa copy = baa.Copy(L.L_COPY);
        Assert.NotSame(baa.GetBoxa(0, L.L_CLONE), copy.GetBoxa(0, L.L_CLONE));
        Boxaa clone = baa.Copy(L.L_CLONE);
        Assert.NotSame(baa, clone);
        Assert.Same(baa.GetBoxa(0, L.L_CLONE), clone.GetBoxa(0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => baa.Copy(L.L_COPY_CLONE));
    }

    [Fact]
    public void BoxaaInsertReplaceRemove()
    {
        Boxaa baa = MakeBoxaa();
        Boxa extra = MakeBoxa(1);
        baa.InsertBoxa(1, extra);
        Assert.Equal(3, baa.Count);
        Assert.Same(extra, baa.GetBoxa(1, L.L_CLONE));
        Assert.Equal(3, baa.GetBoxa(2, L.L_CLONE).Count);

        Boxa repl = MakeBoxa(4);
        baa.ReplaceBoxa(0, repl);
        Assert.Same(repl, baa.GetBoxa(0, L.L_CLONE));

        baa.RemoveBoxa(0);
        Assert.Equal(2, baa.Count);
        Assert.Same(extra, baa.GetBoxa(0, L.L_CLONE));

        Assert.Throws<ArgumentException>(() => baa.InsertBoxa(3, extra));
        Assert.Throws<ArgumentException>(() => baa.ReplaceBoxa(2, extra));
        Assert.Throws<ArgumentException>(() => baa.RemoveBoxa(2));
    }

    [Fact]
    public void BoxaaAddBox_AddsToExistingBoxa()
    {
        Boxaa baa = MakeBoxaa();
        baa.AddBox(1, Box.Create(1, 1, 1, 1), L.L_INSERT);
        Assert.Equal(4, baa.GetBoxa(1, L.L_CLONE).Count);
        Assert.Throws<ArgumentException>(() => baa.AddBox(2, Box.Create(1, 1, 1, 1), L.L_INSERT));
        Assert.Throws<ArgumentException>(() => baa.AddBox(0, Box.Create(1, 1, 1, 1), 99));
    }

    [Fact]
    public void BoxaaInitFullAndExtendWithInit()
    {
        Boxaa baa = Boxaa.Create(3);
        Boxa tmpl = MakeBoxa(2);
        baa.InitFull(tmpl);
        Assert.Equal(3, baa.Count);
        Assert.NotSame(baa.GetBoxa(0, L.L_CLONE), baa.GetBoxa(1, L.L_CLONE));
        Assert.Equal(2, baa.GetBoxa(2, L.L_CLONE).Count);

        baa.ExtendWithInit(5, Boxa.Create(0));
        Assert.Equal(6, baa.Count);
        Assert.Equal(6, baa.Nalloc);
        Assert.Equal(0, baa.GetBoxa(5, L.L_CLONE).Count);
        baa.ExtendWithInit(2, Boxa.Create(0));   // no-op
        Assert.Equal(6, baa.Count);
    }

    [Fact]
    public void BoxaaExtendArray()
    {
        Boxaa baa = Boxaa.Create(2);
        baa.ExtendArray();
        Assert.Equal(4, baa.Nalloc);
        baa.ExtendArrayToSize(3);
        Assert.Equal(4, baa.Nalloc);
        Assert.Throws<ArgumentException>(() => baa.ExtendArrayToSize(1000001));
        Assert.Throws<ArgumentException>(() => baa.ExtendArrayToSize(-1));
    }

    /*---------------------------- Boxaa serialization -------------------*/

    [Fact]
    public void BoxaaWriteMem_EmptyExactFormat()
    {
        Assert.Equal("\nBoxaa Version 3\nNumber of boxa = 0\n", Text(Boxaa.Create(0).WriteMem()));
    }

    [Fact]
    public void BoxaaWriteMem_RoundTrips()
    {
        Boxaa baa = MakeBoxaa();
        Boxaa back = Boxaa.ReadMem(baa.WriteMem());
        Assert.Equal(2, back.Count);
        Assert.Equal(5, back.GetBoxCount());
    }

    private const string BoxaaText =
        "\nBoxaa Version 3\n" +
        "Number of boxa = 2\n" +
        "\nBoxa[0] extent: x = 0, y = 0, w = 40, h = 60" +
        "\nBoxa Version 2\n" +
        "Number of boxes = 2\n" +
        "  Box[0]: x = 0, y = 0, w = 30, h = 40\n" +
        "  Box[1]: x = 10, y = 20, w = 31, h = 41\n" +
        "\nBoxa[1] extent: x = 0, y = 0, w = 0, h = 0" +
        "\nBoxa Version 2\n" +
        "Number of boxes = 0\n";

    [Fact]
    public void BoxaaReadMem_ParsesExactFormat()
    {
        Boxaa baa = Boxaa.ReadMem(Encoding.ASCII.GetBytes(BoxaaText));
        Assert.Equal(2, baa.Count);
        Assert.Equal(2, baa.GetBoxa(0, L.L_CLONE).Count);
        Assert.Equal(0, baa.GetBoxa(1, L.L_CLONE).Count);
        baa.GetBoxa(0, L.L_CLONE).GetBoxGeometry(1, out int x, out int y, out int w, out int h);
        Assert.Equal((10, 20, 31, 41), (x, y, w, h));
    }

    [Fact]
    public void BoxaaReadMem_EmptyRoundTrip()
    {
        Boxaa back = Boxaa.ReadMem(Boxaa.Create(0).WriteMem());
        Assert.Equal(0, back.Count);
    }

    [Fact]
    public void BoxaaReadMem_Errors()
    {
        Func<string, Boxaa> read = s => Boxaa.ReadMem(Encoding.ASCII.GetBytes(s));
        Assert.Throws<ArgumentException>(() => read(""));
        Assert.Throws<ArgumentException>(() => read("\nBoxaa Version 2\nNumber of boxa = 0\n"));
        Assert.Throws<ArgumentException>(() => read("\nBoxaa Version 3\nNumber of boxa = -1\n"));
        Assert.Throws<ArgumentException>(() => read("\nBoxaa Version 3\nNumber of boxa = 1000001\n"));
        Assert.Throws<ArgumentException>(() => read("\nBoxaa Version 3\nNumber of boxa = 1\n"));
        Assert.Throws<ArgumentException>(() =>
            read("\nBoxaa Version 3\nNumber of boxa = 1\n\nBoxa[0] extent: x = 0, y = 0, w = 0, h = 0\nxx"));
    }

    [Fact]
    public void BoxaaReadFile_ParsesExactFormat()
    {
        string path = Path.Combine(Path.GetTempPath(), $"boxaa_{Guid.NewGuid():N}.baa");
        try {
            File.WriteAllText(path, BoxaaText);
            Boxaa baa = Boxaa.Read(path);
            Assert.Equal(2, baa.Count);
            Assert.Equal(2, baa.GetBoxCount());
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => Boxaa.Read(path));
    }
}
