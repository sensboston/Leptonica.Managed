using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BitMiracle.LibTiff.Classic.Internal;
using Leptonica;

namespace Leptonica.Managed.Tests.IO;

/// <summary>
/// The G4 encoder output must stay byte-identical. The expected digests (first 8 bytes of SHA-256)
/// were produced by the encoder before it was optimized, from the images of <see cref="G4TestImages"/>.
/// </summary>
public class G4EncoderEquivalenceTests
{
    private const string Expected =
        "1,0,473EA0FFC1616C44;1,1,F87B2715889FA911;1,2,8164CAA93271D42E;1,3,6615A68FBA9DFD75;1,4,580999B718CC46DF;1,5,2CC37502814A1C29;1,6,D63E145ABE3FCEA0;1,7,B77EAF2A997FD81E;" +
        "2,0,52B31EFEA01F1911;2,1,35DE063933AE963D;2,2,568DA9E6626B9EA3;2,3,0417453A1ACB4914;2,4,AD81706454C6C2B2;2,5,32861CF0ECD6E6E8;2,6,349082FB382B829C;2,7,423DA6D629298F2B;" +
        "7,0,605D6BC303EBF13E;7,1,8B1EFA5A1ACBAA90;7,2,259C5169FAF005E4;7,3,0107EA75C2990EC8;7,4,96233BD71B36E743;7,5,654984293BAF1749;7,6,FB8DB674C02B3B45;7,7,3F22C28FEB9FCE7A;" +
        "31,0,438CC551970A7519;31,1,CC4978C3AC1AB87C;31,2,8A44BCCE6959FC89;31,3,5131D786996DB70A;31,4,371012EEA9A66BB4;31,5,954F66AC38BDAD64;31,6,54A0C4E92CAD14AD;31,7,6B27285E12438413;" +
        "32,0,BCF20425B6DE98B8;32,1,A4BEE604D38C7EEE;32,2,5D12DBD0DD66AB5A;32,3,BCB2C0645E7E42F6;32,4,BC40DF82119D7007;32,5,A2298CD5DDC9658E;32,6,6EFE477CC229E111;32,7,4FF036A6C04A496C;" +
        "33,0,404B9160614336E8;33,1,584AA02ADB625740;33,2,BF9E84DAFC5ED0C6;33,3,4AB4645FC66B3726;33,4,88AF8EF7D978C103;33,5,BD873403032FBEE7;33,6,39B32D07C60D1975;33,7,B99E8863A4F1F8D5;" +
        "63,0,6A792C0D18C1E805;63,1,6B2E177ECC597A95;63,2,4719DA2F5C1FF3CD;63,3,2E9048F3A4AD83CE;63,4,57D8EF86116FDD9A;63,5,78C86E5401FAF79C;63,6,026BCDB15FCACAD2;63,7,D9DBE4EC184400D3;" +
        "64,0,D9370258F6B5B1F2;64,1,BD57AC8F79B7EE56;64,2,976371C45B4EE421;64,3,C9DACBD5649D9CB1;64,4,C93E827A88F5287D;64,5,AE1538AC15A2202B;64,6,1A3CC56638D16333;64,7,406CB3C9137D5ABC;" +
        "65,0,ECAAFD567DF8F173;65,1,E8A04BF1686FE698;65,2,4D28FEFBB66D4BF8;65,3,9EA6377571D74EF0;65,4,5E6A458F212F3198;65,5,BB694F29033AE954;65,6,6CCBFCF8E1C60115;65,7,24DEC6DD9955BEBC;" +
        "100,0,9C2F6791AD0E273A;100,1,6CC636221C77B6B4;100,2,950191D4E4164EE2;100,3,88FE454E478DA64B;100,4,128B75BB1D4164BF;100,5,B32E5B550FDC86F1;100,6,D28A7A5C40E462D1;100,7,0DE2416CC4FCE73F;" +
        "257,0,460D4F25396CF1AA;257,1,3DD5FBC8E018C330;257,2,3E837CF4B91B8EAD;257,3,7B831548AED04112;257,4,344190A5EDF8E800;257,5,FC2C7D6BFE75D4B5;257,6,635F34E7D3031D8A;257,7,15542FBF69F45458;" +
        "1000,0,505DA77CCF313B73;1000,1,D83D79365C59DB9B;1000,2,39D4AFC995AFF1B1;1000,3,3C07406B7334A46F;1000,4,47E7395B9154CE04;1000,5,93F3778C653622B6;1000,6,508E468DBCFE5C8A;1000,7,11993D84798A209E;" +
        "2528,0,8D38D74603030A78;2528,1,D152E1E379805A8C;2528,2,BE2EED364AA83717;2528,3,AA1D0301EABA7058;2528,4,F402E6E7FD6BFAD9;2528,5,3A75C970FC7E23A0;2528,6,E5AE5F2B2A0FC8FE;2528,7,D9F6AFB415230D8E";

    [Fact]
    public void EncodedBytesAreUnchanged()
    {
        var expected = new Dictionary<(int, int), string>();
        foreach (string e in Expected.Split(';'))
        {
            string[] f = e.Split(',');
            expected[(int.Parse(f[0]), int.Parse(f[1]))] = f[2];
        }

        foreach (int w in G4TestImages.Widths)
            for (int seed = 0; seed < 8; seed++)
            {
                Pix p = G4TestImages.Make(w, 5 + seed * 3, seed);
                byte[] g4 = Pix.WriteMem(p, L.IFF_TIFF_G4);
                string actual = Convert.ToHexString(SHA256.HashData(g4))[..16];
                Assert.True(expected[(w, seed)] == actual, $"width {w} seed {seed}");
            }
    }

    [Fact]
    public void EncodedImagesDecodeToTheSource()
    {
        foreach (int w in G4TestImages.Widths)
            for (int seed = 0; seed < 8; seed++)
            {
                Pix p = G4TestImages.Make(w, 5 + seed * 3, seed);
                Pix back = Pix.ReadMem(Pix.WriteMem(p, L.IFF_TIFF_G4));
                Assert.True(Pix.Equal(p, back), $"width {w} seed {seed}");
            }
    }

    [Fact]
    public void FillRunsMatchesBitReference()
    {
        var rng = new Random(9);
        foreach (int width in G4TestImages.Widths)
        {
            for (int iter = 0; iter < 40; iter++)
            {
                // random alternating white/black runs summing to the width
                var runs = new List<int>();
                int x = 0;
                while (x < width)
                {
                    int r = Math.Min(width - x, rng.Next(iter % 2 == 0 ? 6 : 700));
                    runs.Add(r);
                    x += r;
                }
                if (runs.Count % 2 == 1) runs.Add(0);
                int[] arr = new int[runs.Count + 4];
                runs.CopyTo(arr);

                int rowBytes = (width + 7) / 8;
                var buf = new byte[rowBytes + 3];
                rng.NextBytes(buf);
                var expected = new byte[buf.Length];
                Array.Copy(buf, expected, buf.Length);
                int pos = 0;
                for (int k = 0; k < runs.Count; k++)
                {
                    bool black = (k & 1) == 1;
                    for (int i = 0; i < runs[k]; i++, pos++)
                    {
                        int mask = 0x80 >> (pos & 7);
                        if (black) expected[pos >> 3] |= (byte)mask;
                        else expected[pos >> 3] &= (byte)~mask;
                    }
                }

                CCITTCodec.fax3FillRuns(buf, 0, arr, 0, runs.Count, width);
                Assert.Equal(expected, buf);
            }
        }
    }

    [Fact]
    public void FindSpanMatchesScalarVersion()
    {
        var rng = new Random(3);
        foreach (int len in new[] { 1, 3, 8, 9, 15, 16, 17, 40, 64, 65, 200 })
        {
            for (int iter = 0; iter < 300; iter++)
            {
                var buf = new byte[len];
                int kind = iter % 3;
                for (int i = 0; i < len; i++)
                    buf[i] = kind == 0 ? (byte)rng.Next(256) : kind == 1 ? (byte)0 : (byte)255;
                if (kind != 0)
                    for (int k = rng.Next(3); k > 0; k--)
                        buf[rng.Next(len)] ^= (byte)(1 << rng.Next(8));
                int off = len > 8 ? rng.Next(3) : 0;
                int maxBits = 8 * (len - off);
                int be = rng.Next(maxBits + 1);
                int bs = rng.Next(be + 1);
                Assert.Equal(CCITTCodec.find0spanScalar(buf, off, bs, be), CCITTCodec.find0span(buf, off, bs, be));
                Assert.Equal(CCITTCodec.find1spanScalar(buf, off, bs, be), CCITTCodec.find1span(buf, off, bs, be));
            }
        }
    }
}
