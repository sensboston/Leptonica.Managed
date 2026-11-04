using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class ColormapTests
{
    private static string Text(byte[] data) => Encoding.ASCII.GetString(data);

    private static PixColormap Make(int depth, params (int r, int g, int b)[] colors)
    {
        PixColormap cmap = PixColormap.Create(depth);
        foreach (var (r, g, b) in colors)
            cmap.AddColor(r, g, b);
        return cmap;
    }

    /*------------------------------ creation --------------------------------*/

    [Fact]
    public void CreateRandom_BlackAndWhiteEnds()
    {
        CRand.Seed(1);
        PixColormap cmap = PixColormap.CreateRandom(4, true, true);
        Assert.Equal(16, cmap.Count);
        Assert.Equal(4, cmap.Depth);
        cmap.GetColor(0, out int r, out int g, out int b);
        Assert.Equal((0, 0, 0), (r, g, b));
        cmap.GetColor(15, out r, out g, out b);
        Assert.Equal((255, 255, 255), (r, g, b));
    }

    [Fact]
    public void CreateRandom_UsesCRandSequence()
    {
        CRand.Seed(7);
        PixColormap cmap = PixColormap.CreateRandom(2, false, true);
        CRand.Seed(7);
        for (int i = 0; i < 3; i++) {
            int rr = CRand.Next() & 0xff;
            int gg = CRand.Next() & 0xff;
            int bb = CRand.Next() & 0xff;
            cmap.GetColor(i, out int r, out int g, out int b);
            Assert.Equal((rr, gg, bb), (r, g, b));
        }
        cmap.GetColor(3, out int wr, out int wg, out int wb);
        Assert.Equal((255, 255, 255), (wr, wg, wb));
    }

    [Fact]
    public void CreateRandom_Counts()
    {
        Assert.Equal(4, PixColormap.CreateRandom(2, false, false).Count);
        Assert.Equal(256, PixColormap.CreateRandom(8, true, false).Count);
        Assert.Throws<ArgumentException>(() => PixColormap.CreateRandom(1, true, true));
    }

    [Fact]
    public void CreateLinear_Levels()
    {
        PixColormap cmap = PixColormap.CreateLinear(2, 4);
        Assert.Equal(4, cmap.Count);
        int[] expected = { 0, 85, 170, 255 };
        for (int i = 0; i < 4; i++) {
            cmap.GetColor(i, out int r, out int g, out int b);
            Assert.Equal((expected[i], expected[i], expected[i]), (r, g, b));
        }
        Assert.Throws<ArgumentException>(() => PixColormap.CreateLinear(2, 5));
        Assert.Throws<ArgumentException>(() => PixColormap.CreateLinear(2, 1));
        Assert.Throws<ArgumentException>(() => PixColormap.CreateLinear(3, 4));
    }

    /*------------------------------ adding colors ---------------------------*/

    [Fact]
    public void AddNewColor_ReusesExistingColor()
    {
        PixColormap cmap = Make(2, (1, 2, 3), (4, 5, 6));
        Assert.Equal(0, cmap.AddNewColor(4, 5, 6, out int index));
        Assert.Equal(1, index);
        Assert.Equal(2, cmap.Count);
        Assert.Equal(0, cmap.AddNewColor(7, 8, 9, out index));
        Assert.Equal(2, index);
        Assert.Equal(3, cmap.Count);
    }

    [Fact]
    public void AddNewColor_FullReturns2()
    {
        PixColormap cmap = Make(1, (0, 0, 0), (255, 255, 255));
        Assert.Equal(2, cmap.AddNewColor(10, 10, 10, out int index));
        Assert.Equal(0, index);
        Assert.Equal(0, cmap.AddNewColor(255, 255, 255, out index));
        Assert.Equal(1, index);
    }

    [Fact]
    public void AddNearestColor_AddsThenFindsNearest()
    {
        PixColormap cmap = Make(1, (0, 0, 0));
        cmap.AddNearestColor(200, 200, 200, out int index);
        Assert.Equal(1, index);
        Assert.Equal(2, cmap.Count);
        cmap.AddNearestColor(250, 250, 250, out index);
        Assert.Equal(1, index);
        cmap.AddNearestColor(30, 20, 10, out index);
        Assert.Equal(0, index);
        Assert.Equal(2, cmap.Count);
    }

    [Fact]
    public void UsableColor()
    {
        PixColormap cmap = Make(1, (0, 0, 0));
        Assert.True(cmap.UsableColor(9, 9, 9));
        cmap.AddColor(255, 255, 255);
        Assert.False(cmap.UsableColor(9, 9, 9));
        Assert.True(cmap.UsableColor(255, 255, 255));
    }

    [Fact]
    public void AddBlackOrWhite_AddsOrFindsClosest()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddBlackOrWhite(0, out int index);
        Assert.Equal(0, index);
        cmap.AddBlackOrWhite(0, out index);
        Assert.Equal(0, index);
        cmap.AddBlackOrWhite(1, out index);
        Assert.Equal(1, index);
        Assert.Equal(2, cmap.Count);

        PixColormap full = Make(1, (100, 100, 100), (50, 50, 50));
        full.AddBlackOrWhite(0, out index);
        Assert.Equal(1, index);
        full.AddBlackOrWhite(1, out index);
        Assert.Equal(0, index);
    }

    [Fact]
    public void SetBlackAndWhite()
    {
        PixColormap cmap = Make(2, (10, 10, 10), (200, 100, 50), (240, 240, 240), (90, 90, 90));
        cmap.SetBlackAndWhite(true, true);
        cmap.GetColor(0, out int r, out int g, out int b);
        Assert.Equal((0, 0, 0), (r, g, b));
        cmap.GetColor(2, out r, out g, out b);
        Assert.Equal((255, 255, 255), (r, g, b));
        cmap.GetColor(1, out r, out g, out b);
        Assert.Equal((200, 100, 50), (r, g, b));

        PixColormap empty = PixColormap.Create(2);
        empty.SetBlackAndWhite(true, true);   /* no entries: C logs and carries on */
        Assert.Equal(0, empty.Count);
    }

    [Fact]
    public void MinDepth_FreeCount_Clear()
    {
        PixColormap cmap = PixColormap.Create(8);
        Assert.Equal(2, cmap.MinDepth);
        for (int i = 0; i < 5; i++) cmap.AddColor(i, i, i);
        Assert.Equal(4, cmap.MinDepth);
        for (int i = 5; i < 17; i++) cmap.AddColor(i, i, i);
        Assert.Equal(8, cmap.MinDepth);
        Assert.Equal(256 - 17, cmap.FreeCount);
        cmap.Clear();
        Assert.Equal(0, cmap.Count);
        Assert.Equal(256, cmap.FreeCount);
    }

    /*------------------------------ random access ---------------------------*/

    [Fact]
    public void GetColor_OutOfBoundsLogsAndReturnsFalse()
    {
        PixColormap cmap = Make(2, (1, 2, 3));
        Assert.True(cmap.GetColor(0, out int r, out int g, out int b));
        Assert.Equal((1, 2, 3), (r, g, b));
        Assert.False(cmap.GetColor(1, out r, out g, out b));
        Assert.Equal((0, 0, 0), (r, g, b));
        Assert.False(cmap.GetColor(-1, out _, out _, out _));
        Assert.False(cmap.GetRGBA(5, out r, out g, out b, out int a));
        Assert.Equal((0, 0, 0, 0), (r, g, b, a));
    }

    [Fact]
    public void GetColor32_GetRGBA32()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddRGBA(0x11, 0x22, 0x33, 0x44);
        Assert.Equal(0x112233ffu, cmap.GetColor32(0));
        Assert.Equal(0x11223344u, cmap.GetRGBA32(0));
        Assert.Throws<LeptonicaException>(() => cmap.GetColor32(3));
        Assert.Throws<LeptonicaException>(() => cmap.GetRGBA32(3));
    }

    [Fact]
    public void ResetColor_SetsOpaque()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddRGBA(1, 2, 3, 4);
        cmap.ResetColor(0, 9, 8, 7);
        cmap.GetRGBA(0, out int r, out int g, out int b, out int a);
        Assert.Equal((9, 8, 7, 255), (r, g, b, a));
        Assert.True(cmap.ResetColor(0, 1, 1, 1));
        Assert.False(cmap.ResetColor(1, 0, 0, 0));
        Assert.False(cmap.ResetColor(-1, 0, 0, 0));
    }

    [Fact]
    public void GetIndex()
    {
        PixColormap cmap = Make(2, (1, 2, 3), (4, 5, 6));
        Assert.True(cmap.GetIndex(4, 5, 6, out int index));
        Assert.Equal(1, index);
        Assert.False(cmap.GetIndex(4, 5, 7, out index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void HasColor_BlackAndWhite_GrayCount()
    {
        PixColormap gray = Make(2, (0, 0, 0), (255, 255, 255));
        Assert.False(gray.HasColor());
        Assert.True(gray.IsBlackAndWhite());
        Assert.Equal(2, gray.CountGrayColors());

        PixColormap color = Make(2, (0, 0, 0), (255, 255, 254));
        Assert.True(color.HasColor());
        Assert.False(color.IsBlackAndWhite());
        Assert.Equal(1, color.CountGrayColors());

        PixColormap three = Make(2, (0, 0, 0), (255, 255, 255), (7, 7, 7));
        Assert.False(three.IsBlackAndWhite());
        Assert.Equal(3, three.CountGrayColors());

        PixColormap dup = Make(2, (5, 5, 5), (5, 5, 5), (6, 6, 6));
        Assert.Equal(2, dup.CountGrayColors());
        Assert.False(PixColormap.Create(2).HasColor());
    }

    [Fact]
    public void GetRankIntensity()
    {
        PixColormap cmap = Make(2, (100, 100, 100), (10, 10, 10), (250, 250, 250), (50, 60, 70));
        Assert.Equal(1, cmap.GetRankIntensity(0.0f));
        Assert.Equal(3, cmap.GetRankIntensity(0.4f));
        Assert.Equal(0, cmap.GetRankIntensity(0.5f));
        Assert.Equal(2, cmap.GetRankIntensity(1.0f));
        Assert.Throws<ArgumentException>(() => cmap.GetRankIntensity(1.5f));
        Assert.Equal(0, PixColormap.Create(2).GetRankIntensity(0.0f));
    }

    [Fact]
    public void GetNearestIndex()
    {
        PixColormap cmap = Make(2, (0, 0, 0), (100, 0, 0), (255, 255, 255));
        cmap.GetNearestIndex(100, 0, 0, out int index);
        Assert.Equal(1, index);
        cmap.GetNearestIndex(70, 10, 0, out index);
        Assert.Equal(1, index);
        cmap.GetNearestIndex(240, 250, 250, out index);
        Assert.Equal(2, index);
        PixColormap.Create(2).GetNearestIndex(1, 2, 3, out index);
        Assert.Equal(L.UNDEF, index);
    }

    [Fact]
    public void GetNearestGrayIndex()
    {
        PixColormap cmap = PixColormap.CreateLinear(2, 4);
        cmap.GetNearestGrayIndex(90, out int index);
        Assert.Equal(1, index);
        cmap.GetNearestGrayIndex(255, out index);
        Assert.Equal(3, index);
        Assert.Throws<ArgumentException>(() => cmap.GetNearestGrayIndex(256, out _));
    }

    [Fact]
    public void GetDistanceToColor()
    {
        PixColormap cmap = Make(2, (10, 20, 30));
        Assert.Equal(1 + 4 + 9, cmap.GetDistanceToColor(0, 11, 22, 33));
        Assert.Throws<ArgumentException>(() => cmap.GetDistanceToColor(1, 0, 0, 0));
    }

    [Fact]
    public void GetRangeValues()
    {
        PixColormap cmap = Make(2, (10, 200, 30), (50, 20, 90), (30, 100, 60));
        cmap.GetRangeValues(L.L_SELECT_RED, out int minval, out int maxval, out int imin, out int imax);
        Assert.Equal((10, 50, 0, 1), (minval, maxval, imin, imax));
        cmap.GetRangeValues(L.L_SELECT_GREEN, out minval, out maxval, out imin, out imax);
        Assert.Equal((20, 200, 1, 0), (minval, maxval, imin, imax));
        cmap.GetRangeValues(L.L_SELECT_BLUE, out minval, out maxval, out imin, out imax);
        Assert.Equal((30, 90, 0, 1), (minval, maxval, imin, imax));
        cmap.GetRangeValues(L.L_SELECT_AVERAGE, out minval, out maxval, out imin, out imax);
        Assert.Equal((53, 80, 1, 0), (minval, maxval, imin, imax));
        Assert.Throws<ArgumentException>(() => cmap.GetRangeValues(99, out _, out _, out _, out _));
        PixColormap.Create(2).GetRangeValues(L.L_SELECT_RED, out minval, out maxval, out imin, out imax);
        Assert.Equal((100000, -1, L.UNDEF, L.UNDEF), (minval, maxval, imin, imax));
    }

    /*------------------------------ conversion ------------------------------*/

    [Fact]
    public void GrayToFalseColor_EndPoints()
    {
        PixColormap cmap = PixColormap.GrayToFalseColor(0.0f);
        Assert.Equal(256, cmap.Count);
        cmap.GetColor(0, out int r, out int g, out int b);
        Assert.Equal((0, 0), (r, g));
        Assert.Equal(128, b);   /* curve[32] = (int)(255 * 0.5 + 0.5) */
        cmap.GetColor(32, out r, out g, out b);
        Assert.Equal((0, 0, 255), (r, g, b));
        cmap.GetColor(128, out r, out g, out b);
        Assert.Equal(255, g);
        cmap.GetColor(191, out r, out g, out b);
        Assert.Equal((255, 0), (r, b));
        cmap.GetColor(255, out r, out g, out b);
        Assert.Equal((0, 0), (g, b));
        Assert.True(r > 0);
    }

    [Fact]
    public void GrayToColor_FadesToWhite()
    {
        PixColormap cmap = PixColormap.GrayToColor(0x40800000u);
        cmap.GetColor(0, out int r, out int g, out int b);
        Assert.Equal((0x40, 0x80, 0), (r, g, b));
        cmap.GetColor(255, out r, out g, out b);
        Assert.Equal((255, 255, 255), (r, g, b));
        cmap.GetColor(100, out r, out g, out b);
        Assert.Equal((0x40 + 100 * (255 - 0x40) / 255, 0x80 + 100 * (255 - 0x80) / 255,
                      100 * 255 / 255), (r, g, b));
    }

    [Fact]
    public void ColorToGray_Weights()
    {
        PixColormap cmap = Make(2, (255, 0, 0), (0, 255, 0), (0, 0, 255));
        PixColormap gray = cmap.ColorToGray(0.5f, 0.25f, 0.25f);
        int[] expected = { 128, 64, 64 };   /* (int)(0.5 * 255 + 0.5) = 128, (int)(0.25 * 255 + 0.5) = 64 */
        for (int i = 0; i < 3; i++) {
            gray.GetColor(i, out int r, out int g, out int b);
            Assert.Equal((expected[i], expected[i], expected[i]), (r, g, b));
        }
        /* the source colormap is unchanged */
        cmap.GetColor(0, out int r0, out _, out _);
        Assert.Equal(255, r0);

        /* weights are normalized if they do not add to 1 */
        PixColormap gray2 = cmap.ColorToGray(2f, 1f, 1f);
        gray2.GetColor(0, out int rr, out _, out _);
        Assert.Equal(128, rr);

        Assert.Throws<ArgumentException>(() => cmap.ColorToGray(-1f, 1f, 1f));
        PixColormap gray3 = cmap.ColorToGray(0f, 0f, 0f);   /* equal weights 0.33333 */
        gray3.GetColor(0, out rr, out _, out _);
        Assert.Equal((int)(0.33333f * 255 + 0.5), rr);
    }

    [Fact]
    public void ConvertTo4_ConvertTo8()
    {
        PixColormap cmap = Make(2, (1, 2, 3), (4, 5, 6));
        PixColormap c4 = cmap.ConvertTo4();
        Assert.Equal(4, c4.Depth);
        Assert.Equal(2, c4.Count);
        Assert.Equal(14, c4.FreeCount);

        PixColormap c8 = cmap.ConvertTo8();
        Assert.Equal(8, c8.Depth);
        Assert.Equal(2, c8.Count);
        PixColormap c8b = c4.ConvertTo8();
        Assert.Equal(8, c8b.Depth);
        Assert.Equal(cmap.GetColor32(1), c8b.GetColor32(1));

        PixColormap copy = c8.ConvertTo8();
        Assert.NotSame(c8, copy);
        Assert.Equal(2, copy.Count);

        Assert.Throws<ArgumentException>(() => c4.ConvertTo4());
        Assert.Throws<ArgumentException>(() => Make(1, (0, 0, 0)).ConvertTo8());
    }

    /*------------------------------ I/O -------------------------------------*/

    [Fact]
    public void WriteMem_ExactTextFormat()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(0, 0, 0);
        cmap.AddRGBA(255, 128, 7, 100);
        Assert.Equal(
            "\nPixcmap: depth = 2 bpp; 2 colors\n" +
            "Color    R-val    G-val    B-val   Alpha\n" +
            "----------------------------------------\n" +
            "  0         0        0        0      255\n" +
            "  1       255      128        7      100\n" +
            "\n",
            Text(cmap.WriteMem()));
    }

    [Fact]
    public void WriteStream_TextWriterMatchesStream()
    {
        PixColormap cmap = PixColormap.CreateLinear(2, 4);
        using StringWriter sw = new StringWriter();
        cmap.WriteStream(sw);
        Assert.Equal(Text(cmap.WriteMem()), sw.ToString());
    }

    [Fact]
    public void RoundTrip_Mem()
    {
        PixColormap cmap = PixColormap.Create(4);
        for (int i = 0; i < 11; i++)
            cmap.AddRGBA(i * 23, 255 - i * 20, i, 255 - i);
        PixColormap cmap2 = PixColormap.ReadMem(cmap.WriteMem());
        Assert.Equal(4, cmap2.Depth);
        Assert.Equal(11, cmap2.Count);
        Assert.Equal(cmap.WriteMem(), cmap2.WriteMem());
        cmap2.GetRGBA(10, out int r, out int g, out int b, out int a);
        Assert.Equal((230, 55, 10, 245), (r, g, b, a));
    }

    [Fact]
    public void RoundTrip_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cmap_{Guid.NewGuid():N}.txt");
        try {
            PixColormap cmap = PixColormap.GrayToColor(0x10203000u);
            cmap.Write(path);
            Assert.Equal(cmap.WriteMem(), File.ReadAllBytes(path));
            PixColormap cmap2 = PixColormap.Read(path);
            Assert.Equal(256, cmap2.Count);
            Assert.Equal(cmap.WriteMem(), cmap2.WriteMem());
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => PixColormap.Read(path));
    }

    [Fact]
    public void ReadStream_LeavesFollowingDataInStream()
    {
        PixColormap cmap = PixColormap.CreateLinear(1, 2);
        byte[] text = cmap.WriteMem();
        byte[] all = new byte[text.Length + 4];
        text.CopyTo(all, 0);
        Encoding.ASCII.GetBytes("tail").CopyTo(all, text.Length);
        using MemoryStream ms = new MemoryStream(all);
        PixColormap cmap2 = PixColormap.ReadStream(ms);
        Assert.Equal(2, cmap2.Count);
        Assert.Equal("tail", Encoding.ASCII.GetString(all, (int)ms.Position, 4));
    }

    [Fact]
    public void ReadMem_Invalid()
    {
        Assert.Throws<ArgumentException>(() => PixColormap.ReadMem(Encoding.ASCII.GetBytes("garbage")));
        /* depth 3 is invalid */
        Assert.Throws<ArgumentException>(() => PixColormap.ReadMem(
            Encoding.ASCII.GetBytes("\nPixcmap: depth = 3 bpp; 2 colors\n")));
        /* fewer than 2 colors is invalid */
        Assert.Throws<ArgumentException>(() => PixColormap.ReadMem(
            Encoding.ASCII.GetBytes("\nPixcmap: depth = 2 bpp; 1 colors\n")));
        /* a missing entry */
        Assert.Throws<ArgumentException>(() => PixColormap.ReadMem(Encoding.ASCII.GetBytes(
            "\nPixcmap: depth = 2 bpp; 2 colors\n" +
            "Color    R-val    G-val    B-val   Alpha\n" +
            "----------------------------------------\n" +
            "  0         0        0        0      255\n")));
    }

    /*------------------------------ arrays and serialization ----------------*/

    [Fact]
    public void ToRGBTable()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddRGBA(1, 2, 3, 4);
        cmap.AddColor(5, 6, 7);
        uint[] tab = cmap.ToRGBTable(out int ncolors);
        Assert.Equal(2, ncolors);
        Assert.Equal(new uint[] { 0x01020304u, 0x050607ffu }, tab);
    }

    [Fact]
    public void SerializeToMemory_Deserialize()
    {
        PixColormap cmap = PixColormap.Create(4);
        for (int i = 0; i < 5; i++)
            cmap.AddRGBA(i, 10 + i, 20 + i, 250 + i);

        byte[] rgb = cmap.SerializeToMemory(3, out int ncolors);
        Assert.Equal(5, ncolors);
        Assert.Equal(15, rgb.Length);
        Assert.Equal(new byte[] { 0, 10, 20, 1, 11, 21 }, rgb[..6]);

        byte[] rgba = cmap.SerializeToMemory(4, out ncolors);
        Assert.Equal(20, rgba.Length);
        Assert.Equal(new byte[] { 0, 10, 20, 250, 1, 11, 21, 251 }, rgba[..8]);

        PixColormap c4 = PixColormap.DeserializeFromMemory(rgba, 4, 5);
        Assert.Equal(4, c4.Depth);   /* 5 colors need 4 bpp */
        Assert.Equal(cmap.WriteMem(), c4.WriteMem());

        PixColormap c3 = PixColormap.DeserializeFromMemory(rgb, 3, 5);
        c3.GetRGBA(4, out int r, out int g, out int b, out int a);
        Assert.Equal((4, 14, 24, 255), (r, g, b, a));

        Assert.Equal(1, PixColormap.DeserializeFromMemory(rgb, 3, 2).Depth);
        Assert.Equal(2, PixColormap.DeserializeFromMemory(rgb, 3, 3).Depth);
        Assert.Throws<ArgumentException>(() => cmap.SerializeToMemory(5, out _));
        Assert.Throws<ArgumentException>(() => PixColormap.DeserializeFromMemory(rgb, 5, 1));
        Assert.Throws<ArgumentException>(() => PixColormap.DeserializeFromMemory(rgb, 3, 0));
        Assert.Throws<ArgumentException>(() => PixColormap.DeserializeFromMemory(rgb, 3, 257));
    }

    [Fact]
    public void ConvertToHex()
    {
        byte[] data = { 0x00, 0x0f, 0xff, 0xab, 0xcd, 0x01 };
        Assert.Equal("< 000fff abcd01 >", PixColormap.ConvertToHex(data, 2));
        Assert.Equal("< 000fff >", PixColormap.ConvertToHex(data, 1));
        Assert.Throws<ArgumentException>(() => PixColormap.ConvertToHex(data, 0));
    }

    /*------------------------------ transforms ------------------------------*/

    [Fact]
    public void ShiftIntensity()
    {
        PixColormap cmap = Make(2, (100, 200, 0), (255, 255, 255));
        cmap.ShiftIntensity(-0.5f);
        cmap.GetColor(0, out int r, out int g, out int b);
        Assert.Equal((50, 100, 0), (r, g, b));
        cmap.GetColor(1, out r, out g, out b);
        Assert.Equal((127, 127, 127), (r, g, b));

        cmap.ShiftIntensity(0.5f);
        cmap.GetColor(0, out r, out g, out b);
        Assert.Equal((50 + (int)(0.5f * 205), 100 + (int)(0.5f * 155), 0 + (int)(0.5f * 255)), (r, g, b));

        Assert.Throws<ArgumentException>(() => cmap.ShiftIntensity(1.5f));
    }

    [Fact]
    public void GammaTRC_NoOpAndArguments()
    {
        PixColormap cmap = Make(2, (1, 2, 3));
        cmap.GammaTRC(1.0f, 0, 255);   /* no-op: numaGammaTRC is not needed */
        cmap.GetColor(0, out int r, out int g, out int b);
        Assert.Equal((1, 2, 3), (r, g, b));
        Assert.Throws<ArgumentException>(() => cmap.GammaTRC(2.0f, 100, 100));
    }
}
