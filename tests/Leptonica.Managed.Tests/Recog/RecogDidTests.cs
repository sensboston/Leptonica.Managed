using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.RecogTests;

public class RecogDidTests
{
    [RegFact]
    public void ViterbiDidSplitsTouchingDigitsAndExtractsNumber()
    {
        string paPath = Path.Combine(TestPaths.ProgDir, "recog", "digits", "digit_set01.pa");
        Pixa pixa = Pixa.Read(paPath);
        Recog recog = Recog.CreateFromPixa(pixa, 0, 0, 0, 128, 1);
        Assert.Equal(0, recog.AverageSamples(0));
        Assert.True(recog.AveDone);

            /* Build a touching 2-digit bitmap from two real unscaled digit templates */
        Pix d1 = pixa.GetPix(0, L.L_CLONE);
        Pix d2 = pixa.GetPix(pixa.Count - 1, L.L_CLONE);
        int h = System.Math.Max(d1.Height, d2.Height) + 4;
        int overlap = 2;
        int w = d1.Width + d2.Width - overlap + 8;
        Pix touching = Pix.Create(w, h, 1);
        touching.Rasterop(4, 2, d1.Width, d1.Height, L.PIX_SRC, d1, 0, 0);
        touching.Rasterop(4 + d1.Width - overlap, 2, d2.Width, d2.Height, L.PIX_SRC | L.PIX_DST, d2, 0, 0);

            /* Decode with debug path (exercises Viterbi + RescoreDidResult + ShowPath) */
        Boxa? splitBoxes = recog.Decode(touching, 2, out Pix? pixdb);
        Assert.NotNull(splitBoxes);
        Assert.True(splitBoxes!.Count >= 2);
        Assert.NotNull(pixdb);
        Assert.True(recog.DidExists);

            /* Full IdentifyMultiple + ExtractNumbers + ShowExtractNumbers pipeline */
        int ret = recog.IdentifyMultiple(touching, 5, 0, out Boxa? idBoxes, out Pixa? idPixa, 0);
        Assert.Equal(0, ret);
        Assert.NotNull(idBoxes);
        Assert.NotNull(idPixa);

        Sarray? nums = recog.ExtractNumbers(idBoxes!, 0.5f, -1, out Boxaa? baa, out Numaa? naa);
        Assert.NotNull(nums);
        Assert.Equal(1, nums!.Count);
        Assert.Equal($"{d1.Text}{d2.Text}", nums.GetString(0, L.L_NOCOPY));

        Pixa shown = Recog.ShowExtractNumbers(touching, nums, baa!, naa!, out Pix? shownDb);
        Assert.Equal(1, shown.Count);
        Assert.NotNull(shownDb);

        recog.DestroyDid();
        Assert.False(recog.DidExists);
    }
}
