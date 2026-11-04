using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class Sel1Tests
{
    private static string Text(byte[] data) => Encoding.UTF8.GetString(data);

    private static string Serialize(Sel sel)
    {
        using MemoryStream ms = new MemoryStream();
        sel.WriteStream(ms);
        return Text(ms.ToArray());
    }

    private static string Serialize(Sela sela)
    {
        using MemoryStream ms = new MemoryStream();
        sela.WriteStream(ms);
        return Text(ms.ToArray());
    }

    /* 3 x 3 sel with hits, misses and don't-cares; origin (1,1) */
    private static Sel MakeSel(string name = "t1") => Sel.CreateFromString("x oxCxo x", 3, 3, name);

    private static int[][] Rows(Sel sel)
    {
        sel.GetParameters(out int sy, out int sx, out _, out _);
        int[][] rows = new int[sy][];
        for (int i = 0; i < sy; i++) {
            rows[i] = new int[sx];
            for (int j = 0; j < sx; j++)
                rows[i][j] = sel.GetElement(i, j);
        }
        return rows;
    }

    /*------------------------- create / copy -------------------------*/

    [Fact]
    public void Create_InitializesToDontCare()
    {
        Sel sel = Sel.Create(2, 4, "abc");
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 4, 0, 0), (sy, sx, cy, cx));
        Assert.Equal("abc", sel.Name);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 4; j++)
                Assert.Equal(L.SEL_DONT_CARE, sel.GetElement(i, j));
        Assert.Null(Sel.Create(1, 1).Name);
    }

    [Fact]
    public void Create_Errors()
    {
        Assert.Throws<ArgumentException>(() => Sel.Create(0, 3));
        Assert.Throws<ArgumentException>(() => Sel.Create(3, 0));
        Assert.Throws<ArgumentException>(() => Sel.Create(10001, 3));
        Assert.Throws<ArgumentException>(() => Sel.Create(3, 10001));
        Assert.NotNull(Sel.Create(1, 10000));
    }

    [Fact]
    public void CreateBrick_FillsAndSetsOrigin()
    {
        Sel sel = Sel.CreateBrick(2, 3, 1, 2, L.SEL_MISS);
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 3, 1, 2), (sy, sx, cy, cx));
        Assert.All(Rows(sel), row => Assert.All(row, v => Assert.Equal(L.SEL_MISS, v)));
        Assert.Null(sel.Name);
    }

    [Fact]
    public void CreateBrick_Errors()
    {
        Assert.Throws<ArgumentException>(() => Sel.CreateBrick(0, 3, 0, 0, L.SEL_HIT));
        Assert.Throws<ArgumentException>(() => Sel.CreateBrick(3, -1, 0, 0, L.SEL_HIT));
        Assert.Throws<ArgumentException>(() => Sel.CreateBrick(3, 3, 1, 1, 3));
    }

    [Fact]
    public void CreateComb_Horizontal()
    {
        Sel sel = Sel.CreateComb(3, 2, Sel.L_HORIZ);
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((1, 6, 0, 3), (sy, sx, cy, cx));
        Assert.Equal(new[] { 0, 1, 0, 0, 1, 0 }, Rows(sel)[0]);
    }

    [Fact]
    public void CreateComb_Vertical()
    {
        Sel sel = Sel.CreateComb(2, 3, Sel.L_VERT);
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((6, 1, 3, 0), (sy, sx, cy, cx));
        Assert.Equal(new[] { 0, 1, 0, 1, 0, 1 }, Array.ConvertAll(Rows(sel), r => r[0]));
    }

    [Fact]
    public void CreateComb_Errors()
    {
        Assert.Throws<ArgumentException>(() => Sel.CreateComb(0, 2, Sel.L_HORIZ));
        Assert.Throws<ArgumentException>(() => Sel.CreateComb(2, 0, Sel.L_HORIZ));
        Assert.Throws<ArgumentException>(() => Sel.CreateComb(2, 2, 0));
    }

    [Fact]
    public void Copy_IsIndependent()
    {
        Sel sel = MakeSel();
        Sel csel = sel.Copy();
        Assert.Equal("t1", csel.Name);
        Assert.Equal(Rows(sel), Rows(csel));
        csel.GetParameters(out _, out _, out int cy, out int cx);
        Assert.Equal((1, 1), (cy, cx));

        csel.SetElement(0, 0, L.SEL_MISS);
        csel.Name = "other";
        Assert.Equal(L.SEL_HIT, sel.GetElement(0, 0));
        Assert.Equal("t1", sel.Name);
    }

    [Fact]
    public void Create2dIntArray_Shape()
    {
        int[][] a = Sel.Create2dIntArray(3, 5);
        Assert.Equal(3, a.Length);
        Assert.All(a, row => Assert.Equal(5, row.Length));
        Assert.Throws<ArgumentException>(() => Sel.Create2dIntArray(0, 5));
        Assert.Throws<ArgumentException>(() => Sel.Create2dIntArray(5, 0));
    }

    /*--------------------------- accessors ---------------------------*/

    [Fact]
    public void Element_BoundsAndTypeChecks()
    {
        Sel sel = Sel.Create(2, 3);
        sel.SetElement(1, 2, L.SEL_HIT);
        Assert.Equal(L.SEL_HIT, sel.GetElement(1, 2));
        Assert.Throws<ArgumentException>(() => sel.SetElement(2, 0, L.SEL_HIT));
        Assert.Throws<ArgumentException>(() => sel.SetElement(0, 3, L.SEL_HIT));
        Assert.Throws<ArgumentException>(() => sel.SetElement(-1, 0, L.SEL_HIT));
        Assert.Throws<ArgumentException>(() => sel.SetElement(0, 0, 7));
        Assert.Throws<ArgumentException>(() => sel.GetElement(2, 0));
        Assert.Throws<ArgumentException>(() => sel.GetElement(0, -1));
    }

    [Fact]
    public void GetTypeAtOrigin_ReturnsElementOrThrows()
    {
        Sel sel = MakeSel();
        Assert.Equal(L.SEL_DONT_CARE, sel.GetTypeAtOrigin());
        sel.SetElement(1, 1, L.SEL_MISS);
        Assert.Equal(L.SEL_MISS, sel.GetTypeAtOrigin());
        sel.SetOrigin(5, 5);
        Assert.Throws<ArgumentException>(() => sel.GetTypeAtOrigin());
    }

    [Fact]
    public void FindMaxTranslations_UsesHitsOnly()
    {
        Sel sel = Sel.CreateBrick(1, 5, 0, 1, L.SEL_HIT);
        sel.FindMaxTranslations(out int xp, out int yp, out int xn, out int yn);
        Assert.Equal((1, 0, 3, 0), (xp, yp, xn, yn));

        sel.SetElement(0, 4, L.SEL_MISS);   /* a miss does not count */
        sel.FindMaxTranslations(out xp, out yp, out xn, out yn);
        Assert.Equal((1, 0, 2, 0), (xp, yp, xn, yn));

        Sel sel3 = Sel.CreateBrick(3, 3, 1, 1, L.SEL_HIT);
        sel3.FindMaxTranslations(out xp, out yp, out xn, out yn);
        Assert.Equal((1, 1, 1, 1), (xp, yp, xn, yn));
    }

    [Fact]
    public void GetCompositeParameters_Table()
    {
        Sel.GetCompositeParameters(4, out int s1, out int s2, out string h1, out string h2,
                                   out string v1, out string v2);
        Assert.Equal((2, 2), (s1, s2));
        Assert.Equal(("sel_2h", "sel_comb_4h", "sel_2v", "sel_comb_4v"), (h1, h2, v1, v2));

        Sel.GetCompositeParameters(2, out s1, out s2, out h1, out h2, out v1, out v2);
        Assert.Equal((2, 1), (s1, s2));
        Assert.Equal(("sel_2h", "", "sel_2v", ""), (h1, h2, v1, v2));

        Sel.GetCompositeParameters(13, out s1, out s2, out h1, out h2, out v1, out v2);
        Assert.Equal((4, 3), (s1, s2));
        Assert.Equal(("sel_4h", "sel_comb_12h", "sel_4v", "sel_comb_12v"), (h1, h2, v1, v2));

        Sel.GetCompositeParameters(63, out s1, out s2, out h1, out h2, out v1, out v2);
        Assert.Equal((9, 7), (s1, s2));
        Assert.Equal(("sel_9h", "sel_comb_63h", "sel_9v", "sel_comb_63v"), (h1, h2, v1, v2));

        Assert.Throws<ArgumentException>(() =>
            Sel.GetCompositeParameters(1, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() =>
            Sel.GetCompositeParameters(64, out _, out _, out _, out _, out _, out _));
    }

    /*-------------------------- rotation --------------------------*/

    private static Sel MakeAsymmetricSel()
    {
        Sel sel = Sel.Create(2, 3, "asym");
        sel.SetOrigin(0, 1);
        sel.SetElement(0, 0, L.SEL_HIT);
        sel.SetElement(0, 2, L.SEL_MISS);
        sel.SetElement(1, 1, L.SEL_HIT);
        return sel;
    }

    [Fact]
    public void RotateOrth_Quarter()
    {
        Sel r = MakeAsymmetricSel().RotateOrth(1);
        r.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 2, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(new[] { new[] { 0, 1 }, new[] { 1, 0 }, new[] { 0, 2 } }, Rows(r));
        Assert.Equal("asym", r.Name);
    }

    [Fact]
    public void RotateOrth_Half()
    {
        Sel r = MakeAsymmetricSel().RotateOrth(2);
        r.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 3, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(new[] { new[] { 0, 1, 0 }, new[] { 2, 0, 1 } }, Rows(r));
    }

    [Fact]
    public void RotateOrth_ThreeQuarters()
    {
        Sel r = MakeAsymmetricSel().RotateOrth(3);
        r.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 2, 1, 0), (sy, sx, cy, cx));
        Assert.Equal(new[] { new[] { 2, 0 }, new[] { 0, 1 }, new[] { 1, 0 } }, Rows(r));
    }

    [Fact]
    public void RotateOrth_ZeroFourAndErrors()
    {
        Sel sel = MakeAsymmetricSel();
        Assert.Equal(Rows(sel), Rows(sel.RotateOrth(0)));
        Assert.NotSame(sel, sel.RotateOrth(0));
        Assert.Equal(Rows(sel), Rows(sel.RotateOrth(4)));
        Assert.Equal(Rows(sel), Rows(sel.RotateOrth(1).RotateOrth(3)));
        Assert.Equal(Rows(sel), Rows(sel.RotateOrth(2).RotateOrth(2)));
        Assert.Throws<ArgumentException>(() => sel.RotateOrth(-1));
        Assert.Throws<ArgumentException>(() => sel.RotateOrth(5));
    }

    /*-------------------------- strings --------------------------*/

    [Fact]
    public void CreateFromString_ParsesElementsAndOrigin()
    {
        Sel sel = MakeSel();
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 3, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(new[] { new[] { 1, 0, 2 }, new[] { 1, 0, 1 }, new[] { 2, 0, 1 } }, Rows(sel));
        Assert.Equal("t1", sel.Name);
    }

    [Fact]
    public void CreateFromString_UpperCaseOriginOnHitAndMiss()
    {
        Sel sel = Sel.CreateFromString("xX", 1, 2);
        sel.GetParameters(out _, out _, out int cy, out int cx);
        Assert.Equal((0, 1), (cy, cx));
        Assert.Equal(L.SEL_HIT, sel.GetTypeAtOrigin());

        sel = Sel.CreateFromString("Oo", 1, 2);
        Assert.Equal(L.SEL_MISS, sel.GetTypeAtOrigin());
    }

    [Fact]
    public void CreateFromString_Errors()
    {
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString("", 1, 1));
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString("X", 0, 1));
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString("X", 1, 0));
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString("XX", 1, 3));
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString("Xq", 1, 2));
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString("xx", 1, 2));    /* no origin */
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString("XX", 1, 2));    /* two origins */
    }

    [Fact]
    public void PrintToString_InvertsCreateFromString()
    {
        Sel sel = MakeSel();
        string s = sel.PrintToString();
        Assert.Equal("x o\nxCx\no x\n", s);
        /* the length check counts every char, so the row ends must be removed first */
        Assert.Throws<ArgumentException>(() => Sel.CreateFromString(s, 3, 3, "t1"));
        Sel sel2 = Sel.CreateFromString(s.Replace("\n", ""), 3, 3, "t1");
        Assert.Equal(Rows(sel), Rows(sel2));
        Assert.Equal(Serialize(sel), Serialize(sel2));
    }

    [Fact]
    public void PrintToString_MarksOriginOnHitAndMiss()
    {
        Sel sel = Sel.CreateFromString("xOo", 1, 3);
        Assert.Equal("xOo\n", sel.PrintToString());
        sel.SetOrigin(0, 0);
        Assert.Equal("Xoo\n", sel.PrintToString());
    }

    /*--------------------------- Pta / color pix ---------------------------*/

    [Fact]
    public void CreateFromPta_SizesToBoundingRegion()
    {
        Pta pta = Pta.Create(0);
        pta.AddPt(0, 0);
        pta.AddPt(2, 1);
        Sel sel = Sel.CreateFromPta(pta, 1, 1, "pts");
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 3, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(new[] { new[] { 1, 0, 0 }, new[] { 0, 0, 1 } }, Rows(sel));
        Assert.Equal("pts", sel.Name);
    }

    [Fact]
    public void CreateFromPta_Errors()
    {
        Pta pta = Pta.Create(0);
        Assert.Throws<ArgumentException>(() => Sel.CreateFromPta(pta, 0, 0));   /* empty */
        pta.AddPt(1, 1);
        Assert.Throws<ArgumentException>(() => Sel.CreateFromPta(pta, -1, 0));
        Assert.Throws<ArgumentException>(() => Sel.CreateFromPta(pta, 0, -1));
    }

    private static Pix MakeColorPix()
    {
        Pix pix = Pix.Create(3, 2, 32);
        Pix.ComposeRGBPixel(255, 255, 255, out uint white);
        Pix.ComposeRGBPixel(0, 255, 0, out uint green);
        Pix.ComposeRGBPixel(255, 0, 0, out uint red);
        Pix.ComposeRGBPixel(0, 128, 0, out uint darkGreen);
        pix.SetPixel(0, 0, green);
        pix.SetPixel(1, 0, red);
        pix.SetPixel(2, 0, white);
        pix.SetPixel(0, 1, white);
        pix.SetPixel(1, 1, darkGreen);
        pix.SetPixel(2, 1, white);
        return pix;
    }

    [Fact]
    public void CreateFromColorPix_HitsMissesAndOrigin()
    {
        Sel sel = Sel.CreateFromColorPix(MakeColorPix(), "col");
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((2, 3, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(new[] { new[] { 1, 2, 0 }, new[] { 0, 1, 0 } }, Rows(sel));
        Assert.Equal("col", sel.Name);
    }

    [Fact]
    public void CreateFromColorPix_DefaultOriginIsCenter()
    {
        Pix pix = MakeColorPix();
        Pix.ComposeRGBPixel(0, 255, 0, out uint green);
        pix.SetPixel(1, 1, green);
        Sel sel = Sel.CreateFromColorPix(pix, null);
        sel.GetParameters(out _, out _, out int cy, out int cx);
        Assert.Equal((1, 1), (cy, cx));   /* h / 2, w / 2 */
        Assert.Null(sel.Name);
    }

    [Fact]
    public void CreateFromColorPix_Errors()
    {
        Assert.Throws<ArgumentException>(() => Sel.CreateFromColorPix(Pix.Create(3, 3, 8), "x"));

        Pix pix = MakeColorPix();
        Pix.ComposeRGBPixel(0, 0, 255, out uint blue);
        pix.SetPixel(2, 1, blue);
        Assert.Throws<ArgumentException>(() => Sel.CreateFromColorPix(pix, "x"));   /* invalid color */

        Pix nohits = Pix.Create(2, 2, 32);
        Pix.ComposeRGBPixel(255, 255, 255, out uint white);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                nohits.SetPixel(x, y, white);
        Assert.Throws<ArgumentException>(() => Sel.CreateFromColorPix(nohits, "x"));
    }

    /*-------------------------- serialization --------------------------*/

    [Fact]
    public void WriteStream_ExactText()
    {
        Assert.Equal("  Sel Version 1\n" +
                     "  ------  t1  ------\n" +
                     "  sy = 3, sx = 3, cy = 1, cx = 1\n" +
                     "    102\n" +
                     "    101\n" +
                     "    201\n" +
                     "\n",
                     Serialize(MakeSel()));
    }

    [Fact]
    public void WriteStream_UnnamedSelPrintsNull()
    {
        Sel sel = Sel.CreateBrick(1, 2, 0, 0, L.SEL_HIT);
        Assert.Equal("  Sel Version 1\n" +
                     "  ------  (null)  ------\n" +
                     "  sy = 1, sx = 2, cy = 0, cx = 0\n" +
                     "    11\n" +
                     "\n",
                     Serialize(sel));
    }

    [Fact]
    public void ReadStream_RoundTrip()
    {
        Sel sel = MakeSel();
        using MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(Serialize(sel)));
        Sel back = Sel.ReadStream(ms);
        Assert.Equal("t1", back.Name);
        back.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 3, 1, 1), (sy, sx, cy, cx));
        Assert.Equal(Rows(sel), Rows(back));
        Assert.Equal(Serialize(sel), Serialize(back));
    }

    [Fact]
    public void ReadStream_Errors()
    {
        Assert.Throws<ArgumentException>(() => Sel.ReadStream(new MemoryStream(Encoding.ASCII.GetBytes("garbage"))));
        Assert.Throws<ArgumentException>(() =>
            Sel.ReadStream(new MemoryStream(Encoding.ASCII.GetBytes("  Sel Version 2\n  ------  a  ------\n"))));
        Assert.Throws<ArgumentException>(() =>
            Sel.ReadStream(new MemoryStream(Encoding.ASCII.GetBytes("  Sel Version 1\n"))));
        Assert.Throws<ArgumentException>(() =>
            Sel.ReadStream(new MemoryStream(Encoding.ASCII.GetBytes(
                "  Sel Version 1\n  ------  a  ------\n  sy = 0, sx = 3, cy = 0, cx = 0\n"))));
    }

    [Fact]
    public void WriteAndRead_File()
    {
        string path = Path.GetTempFileName();
        try {
            Sel sel = MakeSel();
            sel.Write(path);
            Assert.Equal(Serialize(sel), File.ReadAllText(path));
            Sel back = Sel.Read(path);
            Assert.Equal(Rows(sel), Rows(back));
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => Sel.Read(path));
    }

    /*------------------------------ Sela ------------------------------*/

    private static Sela MakeSela()
    {
        Sela sela = Sela.Create(0);
        sela.AddSel(MakeSel(), null, L.L_INSERT);
        Sel brick = Sel.CreateBrick(1, 3, 0, 1, L.SEL_HIT);
        sela.AddSel(brick, "b", L.L_COPY);
        return sela;
    }

    [Fact]
    public void AddSel_NamingAndFlags()
    {
        Sela sela = Sela.Create(5);
        Assert.Equal(0, sela.Count);

        Sel unnamed = Sel.CreateBrick(1, 1, 0, 0, L.SEL_HIT);
        Assert.Throws<ArgumentException>(() => sela.AddSel(unnamed, null, L.L_INSERT));
        Assert.Throws<ArgumentException>(() => sela.AddSel(unnamed, "n", L.L_CLONE));
        Assert.Equal(0, sela.Count);

        sela.AddSel(unnamed, "n", L.L_COPY);
        Assert.Null(unnamed.Name);                /* the copy got the name */
        Assert.Equal("n", sela.GetSel(0).Name);
        Assert.NotSame(unnamed, sela.GetSel(0));

        sela.AddSel(unnamed, "m", L.L_INSERT);    /* inserted sel takes the name */
        Assert.Equal("m", unnamed.Name);
        Assert.Same(unnamed, sela.GetSel(1));

        sela.AddSel(unnamed, "ignored", L.L_COPY);   /* already has a name */
        Assert.Equal("m", sela.GetSel(2).Name);
        Assert.Equal(3, sela.Count);
    }

    [Fact]
    public void AddSel_ExtendsArray()
    {
        Sela sela = Sela.Create(0);
        Assert.Equal(50, sela.Nalloc);
        for (int i = 0; i < 120; i++)
            sela.AddSel(Sel.CreateBrick(1, 1, 0, 0, L.SEL_HIT), "s" + i, L.L_INSERT);
        Assert.Equal(120, sela.Count);
        Assert.True(sela.Nalloc >= 120);
        Assert.Equal("s119", sela.GetSel(119).Name);
    }

    [Fact]
    public void Create_DefaultSizes()
    {
        Assert.Equal(50, Sela.Create(0).Nalloc);
        Assert.Equal(50, Sela.Create(-3).Nalloc);
        Assert.Equal(50, Sela.Create(10001).Nalloc);
        Assert.Equal(7, Sela.Create(7).Nalloc);
        Assert.Equal(10000, Sela.Create(10000).Nalloc);
    }

    [Fact]
    public void GetSel_IndexChecks()
    {
        Sela sela = MakeSela();
        Assert.Equal("t1", sela.GetSel(0).Name);
        Assert.Throws<ArgumentException>(() => sela.GetSel(-1));
        Assert.Throws<ArgumentException>(() => sela.GetSel(2));
    }

    [Fact]
    public void FindSelByName_FoundAndMissing()
    {
        Sela sela = MakeSela();
        Assert.True(sela.FindSelByName("b", out int index, out Sel? sel));
        Assert.Equal(1, index);
        Assert.Same(sela.GetSel(1), sel);

        Assert.False(sela.FindSelByName("nope", out index, out sel));
        Assert.Equal(-1, index);
        Assert.Null(sel);
    }

    [Fact]
    public void GetBrickName_ByDimensions()
    {
        Sela sela = MakeSela();
        sela.AddSel(Sel.CreateBrick(3, 1, 1, 0, L.SEL_HIT), "v3", L.L_INSERT);
        Assert.Equal("b", sela.GetBrickName(3, 1));
        Assert.Equal("v3", sela.GetBrickName(1, 3));
        Assert.Equal("t1", sela.GetBrickName(3, 3));
        Assert.Throws<ArgumentException>(() => sela.GetBrickName(5, 5));
    }

    [Fact]
    public void GetCombName_FindsOneDimensionalCombs()
    {
        Sela sela = MakeSela();
        sela.AddSel(Sel.CreateComb(2, 3, Sel.L_HORIZ), "sel_comb_6h", L.L_INSERT);
        sela.AddSel(Sel.CreateComb(2, 3, Sel.L_VERT), "sel_comb_6v", L.L_INSERT);
        Assert.Equal("sel_comb_6h", sela.GetCombName(6, Sel.L_HORIZ));
        Assert.Equal("sel_comb_6v", sela.GetCombName(6, Sel.L_VERT));
        Assert.Throws<ArgumentException>(() => sela.GetCombName(8, Sel.L_HORIZ));
        Assert.Throws<ArgumentException>(() => sela.GetCombName(6, 0));
    }

    [Fact]
    public void GetSelnames_ListsNames()
    {
        Sela sela = MakeSela();
        Sarray sa = sela.GetSelnames();
        Assert.Equal(2, sa.Count);
        Assert.Equal("t1", sa.GetString(0, L.L_NOCOPY));
        Assert.Equal("b", sa.GetString(1, L.L_NOCOPY));
        Assert.Throws<ArgumentException>(() => Sela.Create(0).GetSelnames());
    }

    [Fact]
    public void Sela_WriteStream_ExactText()
    {
        Assert.Equal("\nSela Version 1\n" +
                     "Number of Sels = 2\n\n" +
                     "  Sel Version 1\n" +
                     "  ------  t1  ------\n" +
                     "  sy = 3, sx = 3, cy = 1, cx = 1\n" +
                     "    102\n" +
                     "    101\n" +
                     "    201\n" +
                     "\n" +
                     "  Sel Version 1\n" +
                     "  ------  b  ------\n" +
                     "  sy = 1, sx = 3, cy = 0, cx = 1\n" +
                     "    111\n" +
                     "\n",
                     Serialize(MakeSela()));
    }

    [Fact]
    public void Sela_ReadStream_RoundTrip()
    {
        Sela sela = MakeSela();
        string text = Serialize(sela);
        using MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        Sela back = Sela.ReadStream(ms);
        Assert.Equal(2, back.Count);
        Assert.Equal(text, Serialize(back));
        Assert.Equal(Rows(sela.GetSel(0)), Rows(back.GetSel(0)));
        Assert.Equal("b", back.GetSel(1).Name);
    }

    [Fact]
    public void Sela_ReadStream_Errors()
    {
        Assert.Throws<ArgumentException>(() => Sela.ReadStream(new MemoryStream(Encoding.ASCII.GetBytes("junk"))));
        Assert.Throws<ArgumentException>(() =>
            Sela.ReadStream(new MemoryStream(Encoding.ASCII.GetBytes("\nSela Version 9\nNumber of Sels = 0\n\n"))));
        Assert.Throws<ArgumentException>(() =>
            Sela.ReadStream(new MemoryStream(Encoding.ASCII.GetBytes("\nSela Version 1\nNumber of Sels = 1\n\n"))));
    }

    [Fact]
    public void Sela_ReadStream_Empty()
    {
        using MemoryStream ms = new MemoryStream(Encoding.ASCII.GetBytes("\nSela Version 1\nNumber of Sels = 0\n\n"));
        Assert.Equal(0, Sela.ReadStream(ms).Count);
    }

    [Fact]
    public void Sela_WriteAndRead_File()
    {
        string path = Path.GetTempFileName();
        try {
            Sela sela = MakeSela();
            sela.Write(path);
            Assert.Equal(Serialize(sela), File.ReadAllText(path));
            Assert.Equal(Serialize(sela), Serialize(Sela.Read(path)));
        } finally {
            File.Delete(path);
        }
    }

    /*------------------------ file format ------------------------*/

    [Fact]
    public void CreateFromFile_ParsesSels()
    {
        string path = Path.GetTempFileName();
        try {
            File.WriteAllText(path,
                "# diagonal sel\n" +
                "sel_3diag\n" +
                "\"X  \"\n" +
                "\" x \"\n" +
                "\"  x\"\n" +
                "\n" +
                "# another\n" +
                "sel_b\n" +
                "\"xCo\"\n");
            Sela sela = Sela.CreateFromFile(path);
            Assert.Equal(2, sela.Count);

            Sel diag = sela.GetSel(0);
            Assert.Equal("sel_3diag", diag.Name);
            diag.GetParameters(out int sy, out int sx, out int cy, out int cx);
            Assert.Equal((3, 3, 0, 0), (sy, sx, cy, cx));
            Assert.Equal(new[] { new[] { 1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, 0, 1 } }, Rows(diag));

            Sel b = sela.GetSel(1);
            Assert.Equal("sel_b", b.Name);
            b.GetParameters(out sy, out sx, out cy, out cx);
            Assert.Equal((1, 3, 0, 1), (sy, sx, cy, cx));
            Assert.Equal(new[] { new[] { 1, 0, 2 } }, Rows(b));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateFromFile_BadFormatThrows()
    {
        string path = Path.GetTempFileName();
        try {
            File.WriteAllText(path, "sel_bad\nxxx\n");
            Assert.Throws<ArgumentException>(() => Sela.CreateFromFile(path));
            File.WriteAllText(path, "sel_bad\n\"xq\"\n");
            Assert.Throws<ArgumentException>(() => Sela.CreateFromFile(path));
        } finally {
            File.Delete(path);
        }
    }
}
