using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Leptonica;

// not Leptonica.Managed.Tests.Utils: that would hide the class Utils in the other test files
namespace Leptonica.Managed.Tests.UtilsTests;

/* The log sink is global: a test that captures it must not run next to tests that log. */
[CollectionDefinition("LogSink", DisableParallelization = true)]
public class LogSinkCollection
{
}

[Collection("LogSink")]
public class Utils1Tests
{
    private static string TempFile()
    {
        return Path.Combine(Path.GetTempPath(), "utils1_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void MsgSeverityReturnsOldAndSetsLogThreshold()
    {
        int old = Leptonica.Utils.SetMsgSeverity(L.L_SEVERITY_WARNING);
        try {
            Assert.Equal(LogSeverity.Warning, Log.MinSeverity);
            Assert.Equal(L.L_SEVERITY_WARNING, Leptonica.Utils.SetMsgSeverity(L.L_SEVERITY_ERROR));
            Assert.Equal(LogSeverity.Error, Log.MinSeverity);
            Assert.Equal(L.L_SEVERITY_ERROR, Leptonica.Utils.SetMsgSeverity(L.L_SEVERITY_EXTERNAL));
        } finally {
            Leptonica.Utils.SetMsgSeverity(old);
        }
        Assert.Equal(old, (int)Log.MinSeverity);
    }

    [Fact]
    public void ErrorReturnFunctionsLogAndReturnTheValue()
    {
        var messages = new List<(LogSeverity, string)>();
        Action<LogSeverity, string>? oldSink = Log.Sink;
        int oldSev = Leptonica.Utils.SetMsgSeverity(L.L_SEVERITY_INFO);
        Log.Sink = (sev, msg) => messages.Add((sev, msg));
        try {
            Assert.Equal(7, Leptonica.Utils.ReturnErrorInt("bad", "func", 7));
            Assert.Equal(2.5f, Leptonica.Utils.ReturnErrorFloat("bad", "func", 2.5f));
            Assert.Null(Leptonica.Utils.ReturnErrorPtr<string>("bad", "func", null));
            Assert.Equal(1, Leptonica.Utils.ReturnErrorInt1("bad", "arg", "func", 1));
        } finally {
            Log.Sink = oldSink;
            Leptonica.Utils.SetMsgSeverity(oldSev);
        }
        Assert.Equal(4, messages.Count);
        Assert.Equal((LogSeverity.Error, "Error in func: bad"), messages[0]);
        Assert.Equal((LogSeverity.Error, "Error in func: bad: arg"), messages[3]);
    }

    [Fact]
    public void StderrHandlerReceivesFormattedMessage()
    {
        var got = new List<string>();
        Leptonica.Utils.LeptSetStderrHandler(m => got.Add(m));
        try {
            Leptonica.Utils.LeptStderr("Elapsed time = %7.3f sec, n = %d\n", 1.5f, 42);
            Leptonica.Utils.LeptStderr("%s", new string('x', 3000));
        } finally {
            Leptonica.Utils.LeptSetStderrHandler(null);
        }
        Assert.Equal("Elapsed time =   1.500 sec, n = 42\n", got[0]);
        Assert.Equal(1999, got[1].Length);
    }

    [Fact]
    public void FilesAreIdenticalComparesContents()
    {
        string a = TempFile(), b = TempFile(), c = TempFile(), d = TempFile();
        try {
            File.WriteAllBytes(a, new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(b, new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(c, new byte[] { 1, 2, 3, 5 });
            File.WriteAllBytes(d, new byte[] { 1, 2, 3 });
            Assert.True(Leptonica.Utils.FilesAreIdentical(a, b));
            Assert.False(Leptonica.Utils.FilesAreIdentical(a, c));
            Assert.False(Leptonica.Utils.FilesAreIdentical(a, d));
        } finally {
            foreach (string f in new[] { a, b, c, d })
                File.Delete(f);
        }
    }

    [Fact]
    public void ByteSwapping()
    {
        Assert.Equal((ushort)0x3412, Leptonica.Utils.ConvertOnLittleEnd16(0x1234));
        Assert.Equal((ushort)0x1234, Leptonica.Utils.ConvertOnBigEnd16(0x1234));
        Assert.Equal(0x78563412u, Leptonica.Utils.ConvertOnLittleEnd32(0x12345678u));
        Assert.Equal(0x12345678u, Leptonica.Utils.ConvertOnBigEnd32(0x12345678u));
        Assert.Equal(0x000000ffu, Leptonica.Utils.ConvertOnLittleEnd32(0xff000000u));
    }

    [Fact]
    public void CorruptByDeletionRemovesTheRegion()
    {
        string fin = TempFile(), fout = TempFile();
        try {
            byte[] data = new byte[100];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)i;
            File.WriteAllBytes(fin, data);
            Leptonica.Utils.FileCorruptByDeletion(fin, 0.5f, 0.1f, fout);
            byte[] result = File.ReadAllBytes(fout);
            Assert.Equal(90, result.Length);
            for (int i = 0; i < 50; i++)
                Assert.Equal(i, result[i]);
            for (int i = 50; i < 90; i++)
                Assert.Equal(i + 10, result[i]);

                /* size past the end: delete to the end of the file */
            Leptonica.Utils.FileCorruptByDeletion(fin, 0.9f, 0.5f, fout);
            Assert.Equal(90, File.ReadAllBytes(fout).Length);
        } finally {
            File.Delete(fin);
            File.Delete(fout);
        }
    }

    [Fact]
    public void CorruptByDeletionValidatesArguments()
    {
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.FileCorruptByDeletion("a", -0.1f, 0.1f, "b"));
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.FileCorruptByDeletion("a", 1.0f, 0.1f, "b"));
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.FileCorruptByDeletion("a", 0.5f, 0.0f, "b"));
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.FileCorruptByMutation("a", 0.5f, 0.0f, "b"));
    }

    [Fact]
    public void CorruptByMutationChangesOnlyTheRegion()
    {
        string fin = TempFile(), fout = TempFile();
        try {
            byte[] data = new byte[100];
            File.WriteAllBytes(fin, data);
            CRand.Seed(1);
            Leptonica.Utils.FileCorruptByMutation(fin, 0.25f, 0.5f, fout);
            byte[] result = File.ReadAllBytes(fout);
            Assert.Equal(100, result.Length);

            CRand.Seed(1);
            for (int i = 0; i < 100; i++) {
                byte expected = 0;
                if (i >= 25 && i < 75)
                    expected = (byte)(255.9 * ((double)CRand.Next() / (double)CRand.RandMax));
                Assert.Equal(expected, result[i]);
            }
        } finally {
            File.Delete(fin);
            File.Delete(fout);
        }
    }

    [Fact]
    public void ReplaceBytesReplacesAndRemoves()
    {
        string fin = TempFile(), fout = TempFile();
        try {
            File.WriteAllBytes(fin, "0123456789"u8.ToArray());
            Leptonica.Utils.FileReplaceBytes(fin, 2, 3, "ab"u8, fout);
            Assert.Equal("01ab56789", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(fout)));
            Leptonica.Utils.FileReplaceBytes(fin, 2, 3, ReadOnlySpan<byte>.Empty, fout);
            Assert.Equal("0156789", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(fout)));
            Leptonica.Utils.FileReplaceBytes(fin, 8, 2, "XYZ"u8, fout);
            Assert.Equal("01234567XYZ", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(fout)));
        } finally {
            File.Delete(fin);
            File.Delete(fout);
        }
    }

    [Fact]
    public void GenRandomIntOnInterval()
    {
            /* The first rand() after srand(1) is 41 */
        Assert.Equal(1, Leptonica.Utils.GenRandomIntOnInterval(0, 999, 1));
        Assert.Equal(-10 + (int)(21.0 * (41.0 / 32767.0)), Leptonica.Utils.GenRandomIntOnInterval(-10, 10, 1));
        Assert.Equal(5, Leptonica.Utils.GenRandomIntOnInterval(5, 5, 0));
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.GenRandomIntOnInterval(3, 2, 0));
    }

    [Fact]
    public void SimpleMath()
    {
        Assert.Equal(0, Leptonica.Utils.RoundFToI(0.49f));
        Assert.Equal(1, Leptonica.Utils.RoundFToI(0.5f));
        Assert.Equal(0, Leptonica.Utils.RoundFToI(-0.49f));
        Assert.Equal(-1, Leptonica.Utils.RoundFToI(-0.5f));
        Assert.Equal(-3, Leptonica.Utils.RoundFToI(-2.6f));

        Assert.Equal(2, Leptonica.Utils.LeptFloor(2.7f));
        Assert.Equal(-2, Leptonica.Utils.LeptFloor(-2.7f));  /* truncates toward zero, as in C */
        Assert.Equal(0, Leptonica.Utils.LeptFloor(-0.5f));

        Assert.Equal(3, Leptonica.Utils.LeptCeiling(2.1f));
        Assert.Equal(2, Leptonica.Utils.LeptCeiling(2.0f));
        Assert.Equal(-3, Leptonica.Utils.LeptCeiling(-2.1f));
        Assert.Equal(-2, Leptonica.Utils.LeptCeiling(-2.0f));
    }

    // Reference values produced with the C code of utils1.c (gcc, x86-64)
    [Theory]
    [InlineData("a", 10353082222279438336UL, 97UL)]
    [InlineData("abc", 9436816276434428836UL, 136518UL)]
    [InlineData("hello world", 5374358816465634025UL, 513608798117958732UL)]
    [InlineData("Leptonica 1.87.0", 7240825677664805393UL, 7384483188211249883UL)]
    [InlineData("été", 5863800206113842276UL, 374187940UL)]
    public void StringHashes(string str, ulong hash, ulong fast)
    {
        Assert.True(Leptonica.Utils.HashStringToUint64(str, out ulong h));
        Assert.Equal(hash, h);
        Assert.True(Leptonica.Utils.HashStringToUint64Fast(str, out ulong hf));
        Assert.Equal(fast, hf);
    }

    [Fact]
    public void StringHashOfEmptyStringFails()
    {
        Assert.False(Leptonica.Utils.HashStringToUint64("", out ulong h));
        Assert.Equal(0UL, h);
        Assert.False(Leptonica.Utils.HashStringToUint64Fast("", out ulong hf));
        Assert.Equal(0UL, hf);
    }

    [Theory]
    [InlineData(0, 0, 0UL)]
    [InlineData(1, 0, 2173249142UL)]
    [InlineData(0, 1, 3763193258UL)]
    [InlineData(3, 4, 21572520461UL)]
    [InlineData(4, 3, 19982576345UL)]
    [InlineData(19999, 20000, 118726674771009UL)]
    [InlineData(123456, 789, 271269805603323UL)]
    public void PointHash(int x, int y, ulong expected)
    {
        Assert.Equal(expected, Leptonica.Utils.HashPtToUint64(x, y));
    }

    [Theory]
    [InlineData(0.0, 0UL)]
    [InlineData(1.0, 847019UL)]
    [InlineData(3.14159, 2660988UL)]
    [InlineData(-2.5, 543312UL)]
    [InlineData(1e6, 847019667010UL)]
    [InlineData(-1e-3, 217UL)]
    public void Float64Hash(double val, ulong expected)
    {
        Assert.Equal(expected, Leptonica.Utils.HashFloat64ToUint64(val));
    }

    [Fact]
    public void Primes()
    {
            /* Upstream behavior: 2 is even, so it is never reported as prime */
        Assert.Equal(3u, Leptonica.Utils.FindNextLargerPrime(1));
        Assert.Equal(3u, Leptonica.Utils.FindNextLargerPrime(2));
        Assert.Equal(9u, Leptonica.Utils.FindNextLargerPrime(8));  /* upstream: 9 passes as prime */
        Assert.Equal(11u, Leptonica.Utils.FindNextLargerPrime(10));
        Assert.Equal(7919u, Leptonica.Utils.FindNextLargerPrime(7908));
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.FindNextLargerPrime(0));

        Leptonica.Utils.LeptIsPrime(91, out bool isPrime, out uint factor);
        Assert.False(isPrime);
        Assert.Equal(7u, factor);
        Leptonica.Utils.LeptIsPrime(100, out isPrime, out factor);
        Assert.False(isPrime);
        Assert.Equal(2u, factor);
        Leptonica.Utils.LeptIsPrime(7919, out isPrime, out factor);
        Assert.True(isPrime);
        Assert.Equal(0u, factor);
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.LeptIsPrime(0, out _, out _));

            /* Upstream behavior: the divisor equal to the square root is not tried */
        Leptonica.Utils.LeptIsPrime(25, out isPrime, out _);
        Assert.True(isPrime);
    }

    [Fact]
    public void GrayCode()
    {
        Assert.Equal(0u, Leptonica.Utils.ConvertIntToGrayCode(0));
        Assert.Equal(1u, Leptonica.Utils.ConvertIntToGrayCode(1));
        Assert.Equal(3u, Leptonica.Utils.ConvertIntToGrayCode(2));
        Assert.Equal(2u, Leptonica.Utils.ConvertIntToGrayCode(3));
        Assert.Equal(6u, Leptonica.Utils.ConvertIntToGrayCode(4));
        foreach (uint v in new uint[] { 0, 1, 2, 3, 100, 65535, 0x12345678, 0xffffffff })
            Assert.Equal(v, Leptonica.Utils.ConvertGrayCodeToInt(Leptonica.Utils.ConvertIntToGrayCode(v)));
        for (uint v = 0; v < 1000; v++) {
            uint diff = Leptonica.Utils.ConvertIntToGrayCode(v) ^ Leptonica.Utils.ConvertIntToGrayCode(v + 1);
            Assert.Equal(1, System.Numerics.BitOperations.PopCount(diff));
        }
    }

    [Fact]
    public void VersionString()
    {
        Assert.Equal("leptonica-1.87.0", Leptonica.Utils.GetLeptonicaVersion());
    }

    [Fact]
    public void TimersMeasureElapsedTime()
    {
        Leptonica.Utils.StartTimer();
        LTimer outer = Leptonica.Utils.StartTimerNested();
        System.Threading.Thread.Sleep(50);
        LTimer inner = Leptonica.Utils.StartTimerNested();
        System.Threading.Thread.Sleep(50);
        float tInner = Leptonica.Utils.StopTimerNested(inner);
        float tOuter = Leptonica.Utils.StopTimerNested(outer);
        float t = Leptonica.Utils.StopTimer();
        Assert.InRange(tInner, 0.03f, 5f);
        Assert.True(tOuter > tInner);
        Assert.True(t >= tOuter);
    }

    [Fact]
    public void WallTimerAndCurrentTime()
    {
        Leptonica.Utils.GetCurrentTime(out int sec, out int usec);
        Assert.InRange(usec, 0, 999999);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(sec, (int)(now - 5), (int)(now + 5));

        WallTimer? timer = Leptonica.Utils.StartWallTimer();
        System.Threading.Thread.Sleep(50);
        float elapsed = Leptonica.Utils.StopWallTimer(ref timer);
        Assert.InRange(elapsed, 0.03f, 5f);
        Assert.Null(timer);
        Assert.Throws<ArgumentException>(() => Leptonica.Utils.StopWallTimer(ref timer));
    }

    [Fact]
    public void FormattedDate()
    {
        string date = Leptonica.Utils.GetFormattedDate();
        Assert.Matches(new Regex(@"^\d{14}[Z+-]\d{2}'\d{2}'$"), date);
    }
}
