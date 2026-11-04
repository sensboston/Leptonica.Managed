using System.IO;
using System.Text;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.RecogTests;

[Collection("LeptDebugOK")]
public class RecogTrainTests
{
    [RegFact]
    public void TrainSerializeOutliersAndIdentifyRealDigitsRoundTrip()
    {
        using var debugScope = new LeptDebugScope(false);

        string paPath = Path.Combine(TestPaths.ProgDir, "recog", "digits", "digit_set01.pa");
        Pixa pixa = Pixa.Read(paPath);
        Assert.True(pixa.Count > 10);

            /* Train from labeled pixa, serialize & deserialize bit-identically */
        Recog recog = Recog.CreateFromPixa(pixa, 0, 40, 0, 128, 1);
        Assert.True(recog.TrainDone);
        Assert.Equal(10, recog.Count);

        byte[] bytes = recog.WriteMem();
        Recog recog2 = Recog.ReadMem(bytes);
        Assert.Equal(bytes, recog2.WriteMem());

            /* Identify every training character against the deserialized recognizer */
        int correct = 0;
        for (int i = 0; i < pixa.Count; i++) {
            Pix ch = pixa.GetPix(i, L.L_CLONE);
            recog2.IdentifyPix(ch);
            recog2.Rch!.Extract(out _, out float score, out string? pred, out _, out _, out _, out _);
            if (pred == ch.Text && score > 0.7f)
                correct++;
        }
        Assert.Equal(pixa.Count, correct);

            /* FilterPixaBySize & RemoveOutliers1/2 */
        Pixa filtered = Recog.FilterPixaBySize(pixa, 10, 5, 2.5f, out Numa counts);
        Assert.Equal(10, counts.Count);
        Assert.True(filtered.Count <= 50 && filtered.Count >= 10);

        Pixa out1 = pixa.RemoveOutliers1(0.7f, 2, 1, out Pix? save1, out _);
        Assert.True(out1.Count >= 10);
        Assert.NotNull(save1);

        Pixa out2 = pixa.RemoveOutliers2(0.7f, 1, out Pix? save2, out _);
        Assert.True(out2.Count >= 10);
        Assert.NotNull(save2);

            /* TrainFromBoot on unlabeled copies */
        Pixa unlabeled = Pixa.Create(10);
        for (int i = 0; i < 10; i++) {
            Pix c = pixa.GetPix(i, L.L_COPY);
            c.Text = null;
            unlabeled.AddPix(c, L.L_INSERT);
        }
        Pixa bootLabeled = recog.TrainFromBoot(unlabeled, 0.75f, 128, 0);
        Assert.Equal(10, bootLabeled.Count);

            /* ShowContent & DebugAverages */
        using MemoryStream ms = new();
        recog.ShowContent(ms, 0, 0);
        Assert.Contains("Setsize: 10", Encoding.UTF8.GetString(ms.ToArray()));

        recog.DebugAverages(0);
        Assert.NotNull(recog.PixdbAve);
    }
}
