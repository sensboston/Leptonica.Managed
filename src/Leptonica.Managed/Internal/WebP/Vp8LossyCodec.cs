using System;
using System.Runtime.CompilerServices;
using static Leptonica.Internal.WebP.Vp8Tables;

namespace Leptonica.Internal.WebP;

/// <summary>
/// Pure managed RFC 6386 VP8 intra-frame decoder and encoder.
/// </summary>
internal static class Vp8LossyCodec
{
    public static bool TryGetHeader(ReadOnlySpan<byte> payload, out int width, out int height)
    {
        width = height = 0;
        if (payload.Length < 10)
            return false;
        uint tag = (uint)payload[0] | ((uint)payload[1] << 8) | ((uint)payload[2] << 16);
        if ((tag & 1U) != 0) // must be keyframe (bit 0 == 0)
            return false;
        if (payload[3] != 0x9d || payload[4] != 0x01 || payload[5] != 0x2a)
            return false;
        ushort wRaw = (ushort)(payload[6] | (payload[7] << 8));
        ushort hRaw = (ushort)(payload[8] | (payload[9] << 8));
        width = wRaw & 0x3fff;
        height = hRaw & 0x3fff;
        return width > 0 && height > 0;
    }

    // =========================================================================
    // Decoder Structures & Entry Point
    // =========================================================================

    private struct QuantMatrix
    {
        public int Y1Dc, Y1Ac;
        public int Y2Dc, Y2Ac;
        public int UvDc, UvAc;
    }

    private struct FInfo
    {
        public int FLimit;
        public int FILevel;
        public bool FInner;
        public int HevThresh;
    }

    private sealed class MbData
    {
        public readonly short[] Coeffs = new short[384];
        public bool IsI4x4;
        public readonly byte[] IModes = new byte[16];
        public byte UvMode;
        public uint NonZeroY;
        public uint NonZeroUv;
        public bool Skip;
        public byte Segment;
    }

    private struct MbContext
    {
        public uint Nz;
        public uint NzDc;
    }

    public static bool TryDecodeRgba(ReadOnlySpan<byte> payload, out byte[] rgba, out int width, out int height)
    {
        rgba = Array.Empty<byte>();
        if (!TryGetHeader(payload, out width, out height))
            return false;

        uint tag = (uint)payload[0] | ((uint)payload[1] << 8) | ((uint)payload[2] << 16);
        int part0Len = (int)(tag >> 5);
        if (10 + part0Len > payload.Length)
            return false;

        byte[] buf = payload.ToArray();
        Vp8BoolDecoder br = new Vp8BoolDecoder(buf, 10, part0Len);

        // Color space & clamp type
        _ = br.ReadFlag();
        _ = br.ReadFlag();

        // Segment header
        bool useSegment = br.ReadFlag();
        bool updateMap = false;
        bool absDelta = true;
        Span<int> segQuant = stackalloc int[NumMbSegments];
        Span<int> segFilter = stackalloc int[NumMbSegments];
        segQuant.Clear();
        segFilter.Clear();
        Span<byte> segProbs = stackalloc byte[MbFeatureTreeProbs];
        segProbs.Fill(255);

        if (useSegment)
        {
            updateMap = br.ReadFlag();
            if (br.ReadFlag())
            {
                absDelta = br.ReadFlag();
                for (int i = 0; i < NumMbSegments; i++)
                    segQuant[i] = br.ReadFlag() ? br.ReadSigned(7) : 0;
                for (int i = 0; i < NumMbSegments; i++)
                    segFilter[i] = br.ReadFlag() ? br.ReadSigned(6) : 0;
            }
            if (updateMap)
            {
                for (int i = 0; i < MbFeatureTreeProbs; i++)
                    segProbs[i] = br.ReadFlag() ? (byte)br.ReadLiteral(8) : (byte)255;
            }
        }

        // Filter header
        bool filterSimple = br.ReadFlag();
        int filterLevel = (int)br.ReadLiteral(6);
        int filterSharpness = (int)br.ReadLiteral(3);
        bool useLfDelta = br.ReadFlag();
        Span<int> refLfDelta = stackalloc int[NumRefLfDeltas];
        Span<int> modeLfDelta = stackalloc int[NumModeLfDeltas];
        refLfDelta.Clear();
        modeLfDelta.Clear();
        if (useLfDelta && br.ReadFlag())
        {
            for (int i = 0; i < NumRefLfDeltas; i++)
                if (br.ReadFlag()) refLfDelta[i] = br.ReadSigned(6);
            for (int i = 0; i < NumModeLfDeltas; i++)
                if (br.ReadFlag()) modeLfDelta[i] = br.ReadSigned(6);
        }
        int filterType = filterLevel == 0 ? 0 : (filterSimple ? 1 : 2);

        // Token partitions
        int numParts = 1 << (int)br.ReadLiteral(2);
        int afterPart0 = 10 + part0Len;
        int remLen = buf.Length - afterPart0;
        int lastPart = numParts - 1;
        if (remLen < 3 * lastPart)
            return false;

        Vp8BoolDecoder[] tokenBrs = new Vp8BoolDecoder[numParts];
        int pStart = afterPart0 + 3 * lastPart;
        int pLeft = remLen - 3 * lastPart;
        for (int p = 0; p < lastPart; p++)
        {
            int sz = buf[afterPart0 + 3 * p]
                   | (buf[afterPart0 + 3 * p + 1] << 8)
                   | (buf[afterPart0 + 3 * p + 2] << 16);
            int psize = Math.Min(sz, pLeft);
            tokenBrs[p] = new Vp8BoolDecoder(buf, pStart, psize);
            pStart += psize;
            pLeft -= psize;
        }
        tokenBrs[lastPart] = new Vp8BoolDecoder(buf, pStart, pLeft);

        // Quantizer header
        int baseQ0 = (int)br.ReadLiteral(7);
        int dqy1Dc = br.ReadFlag() ? br.ReadSigned(4) : 0;
        int dqy2Dc = br.ReadFlag() ? br.ReadSigned(4) : 0;
        int dqy2Ac = br.ReadFlag() ? br.ReadSigned(4) : 0;
        int dquvDc = br.ReadFlag() ? br.ReadSigned(4) : 0;
        int dquvAc = br.ReadFlag() ? br.ReadSigned(4) : 0;

        QuantMatrix[] dqm = new QuantMatrix[NumMbSegments];
        for (int i = 0; i < NumMbSegments; i++)
        {
            int q;
            if (useSegment)
                q = absDelta ? segQuant[i] : segQuant[i] + baseQ0;
            else if (i > 0)
            {
                dqm[i] = dqm[0];
                continue;
            }
            else
                q = baseQ0;

            dqm[i] = new QuantMatrix
            {
                Y1Dc = DcTable[Math.Clamp(q + dqy1Dc, 0, 127)],
                Y1Ac = AcTable[Math.Clamp(q, 0, 127)],
                Y2Dc = DcTable[Math.Clamp(q + dqy2Dc, 0, 127)] * 2,
                Y2Ac = Math.Max((AcTable[Math.Clamp(q + dqy2Ac, 0, 127)] * 101581) >> 16, 8),
                UvDc = DcTable[Math.Clamp(q + dquvDc, 0, 117)],
                UvAc = AcTable[Math.Clamp(q + dquvAc, 0, 127)],
            };
        }

        // refresh_entropy_probs on keyframe
        _ = br.ReadFlag();

        // Coefficient probabilities
        byte[] coeffProbs = new byte[CoeffsProba0.Length];
        for (int idx = 0; idx < coeffProbs.Length; idx++)
        {
            coeffProbs[idx] = br.ReadBool(CoeffsUpdateProba[idx])
                ? (byte)br.ReadLiteral(8)
                : CoeffsProba0[idx];
        }

        bool useSkip = br.ReadFlag();
        byte skipP = useSkip ? (byte)br.ReadLiteral(8) : (byte)0;

        int mbW = (width + 15) >> 4;
        int mbH = (height + 15) >> 4;

        // Precompute filter strengths [seg * 2 + isI4x4]
        FInfo[] fstrengths = new FInfo[NumMbSegments * 2];
        for (int s = 0; s < NumMbSegments; s++)
        {
            int baseLvl = useSegment
                ? (absDelta ? segFilter[s] : segFilter[s] + filterLevel)
                : filterLevel;
            for (int i4 = 0; i4 < 2; i4++)
            {
                int lvl = baseLvl;
                if (useLfDelta)
                {
                    lvl += refLfDelta[0];
                    if (i4 == 1)
                        lvl += modeLfDelta[0];
                }
                fstrengths[s * 2 + i4] = MakeFInfo(lvl, filterSharpness, i4 == 1);
            }
        }

        // Allocate padded YUV planes
        int yStride = 1 + mbW * 16 + 4;
        int uvStride = 1 + mbW * 8;
        byte[] yPlane = new byte[yStride * (1 + mbH * 16)];
        byte[] uPlane = new byte[uvStride * (1 + mbH * 8)];
        byte[] vPlane = new byte[uvStride * (1 + mbH * 8)];
        InitPlaneBorders(yPlane, yStride, 1 + mbH * 16);
        InitPlaneBorders(uPlane, uvStride, 1 + mbH * 8);
        InitPlaneBorders(vPlane, uvStride, 1 + mbH * 8);

        MbData[] mbRow = new MbData[mbW];
        for (int x = 0; x < mbW; x++)
            mbRow[x] = new MbData();
        byte[] intraT = new byte[4 * mbW];
        Array.Fill(intraT, B_DC_PRED);
        Span<byte> intraL = stackalloc byte[4];
        MbContext[] mbInfo = new MbContext[mbW + 1];
        FInfo[] allFInfo = new FInfo[mbW * mbH];
        Span<short> whtDc = stackalloc short[16];

        int partMask = numParts - 1;
        for (int mbY = 0; mbY < mbH; mbY++)
        {
            intraL.Fill(B_DC_PRED);
            mbInfo[0] = default;

            // 1. Parse intra modes for row from partition 0
            for (int mbX = 0; mbX < mbW; mbX++)
            {
                MbData mb = mbRow[mbX];
                if (updateMap)
                {
                    mb.Segment = br.ReadBool(segProbs[0])
                        ? (byte)(2 + (br.ReadBool(segProbs[2]) ? 1 : 0))
                        : (byte)(br.ReadBool(segProbs[1]) ? 1 : 0);
                }
                else
                {
                    mb.Segment = 0;
                }

                mb.Skip = useSkip && br.ReadBool(skipP);
                mb.IsI4x4 = !br.ReadBool(145);
                int topOff = 4 * mbX;
                if (mb.IsI4x4)
                {
                    for (int sy = 0; sy < 4; sy++)
                    {
                        byte leftMode = intraL[sy];
                        for (int sx = 0; sx < 4; sx++)
                        {
                            byte topMode = intraT[topOff + sx];
                            byte mode = ReadBMode(br, BModeProbIndex(topMode, leftMode));
                            intraT[topOff + sx] = mode;
                            mb.IModes[sy * 4 + sx] = mode;
                            leftMode = mode;
                        }
                        intraL[sy] = leftMode;
                    }
                }
                else
                {
                    byte ymode = ReadYMode16(br);
                    mb.IModes[0] = ymode;
                    intraT[topOff] = intraT[topOff + 1] = intraT[topOff + 2] = intraT[topOff + 3] = ymode;
                    intraL.Fill(ymode);
                }
                mb.UvMode = ReadUvMode(br);
            }

            // 2. Parse residuals from token partition
            Vp8BoolDecoder tbr = tokenBrs[mbY & partMask];
            for (int mbX = 0; mbX < mbW; mbX++)
            {
                MbData mb = mbRow[mbX];
                Array.Clear(mb.Coeffs);
                if (mb.Skip)
                {
                    mb.NonZeroY = 0;
                    mb.NonZeroUv = 0;
                    mbInfo[mbX + 1].Nz = 0;
                    mbInfo[0].Nz = 0;
                    if (!mb.IsI4x4)
                    {
                        mbInfo[mbX + 1].NzDc = 0;
                        mbInfo[0].NzDc = 0;
                    }
                    continue;
                }

                QuantMatrix q = dqm[mb.Segment];
                uint mbNz = mbInfo[mbX + 1].Nz;
                uint leftNz = mbInfo[0].Nz;
                uint mbNzDc = mbInfo[mbX + 1].NzDc;
                uint leftNzDc = mbInfo[0].NzDc;

                int first, acType;
                if (mb.IsI4x4)
                {
                    first = 0;
                    acType = 3;
                }
                else
                {
                    whtDc.Clear();
                    int ctx = (int)(mbNzDc + leftNzDc);
                    int nz = GetCoeffs(tbr, coeffProbs, 1, ctx, q.Y2Dc, q.Y2Ac, 0, whtDc);
                    uint dcNz = nz > 0 ? 1u : 0u;
                    mbNzDc = dcNz;
                    leftNzDc = dcNz;
                    if (nz > 1)
                    {
                        TransformWht(whtDc, mb.Coeffs);
                    }
                    else
                    {
                        short dc0 = (short)((whtDc[0] + 3) >> 3);
                        for (int i = 0; i < 256; i += 16)
                            mb.Coeffs[i] = dc0;
                    }
                    first = 1;
                    acType = 0;
                }

                uint nonZeroY = 0;
                uint tnz = mbNz & 0x0f;
                uint lnz = leftNz & 0x0f;
                int off = 0;
                for (int sy = 0; sy < 4; sy++)
                {
                    uint l = lnz & 1;
                    uint nzCoeffs = 0;
                    for (int sx = 0; sx < 4; sx++)
                    {
                        int ctx = (int)(l + (tnz & 1));
                        int nz = GetCoeffs(tbr, coeffProbs, acType, ctx, q.Y1Dc, q.Y1Ac, first, mb.Coeffs.AsSpan(off, 16));
                        l = nz > first ? 1u : 0u;
                        tnz = (tnz >> 1) | (l << 7);
                        nzCoeffs = NzCodeBits(nzCoeffs, nz, mb.Coeffs[off] != 0);
                        off += 16;
                    }
                    tnz >>= 4;
                    lnz = (lnz >> 1) | (l << 7);
                    nonZeroY = (nonZeroY << 8) | nzCoeffs;
                }
                uint outTopNz = tnz;
                uint outLeftNz = lnz >> 4;

                uint nonZeroUv = 0;
                for (int ch = 0; ch < 4; ch += 2)
                {
                    uint nzCoeffs = 0;
                    uint ctnz = mbNz >> (4 + ch);
                    uint clnz = leftNz >> (4 + ch);
                    for (int sy = 0; sy < 2; sy++)
                    {
                        uint l = clnz & 1;
                        for (int sx = 0; sx < 2; sx++)
                        {
                            int ctx = (int)(l + (ctnz & 1));
                            int nz = GetCoeffs(tbr, coeffProbs, 2, ctx, q.UvDc, q.UvAc, 0, mb.Coeffs.AsSpan(off, 16));
                            l = nz > 0 ? 1u : 0u;
                            ctnz = (ctnz >> 1) | (l << 3);
                            nzCoeffs = NzCodeBits(nzCoeffs, nz, mb.Coeffs[off] != 0);
                            off += 16;
                        }
                        ctnz >>= 2;
                        clnz = (clnz >> 1) | (l << 5);
                    }
                    nonZeroUv |= nzCoeffs << (4 * ch);
                    outTopNz |= (ctnz << 4) << ch;
                    outLeftNz |= (clnz & 0xf0) << ch;
                }

                mbInfo[mbX + 1].Nz = outTopNz & 0xff;
                mbInfo[0].Nz = outLeftNz & 0xff;
                mbInfo[mbX + 1].NzDc = mbNzDc;
                mbInfo[0].NzDc = leftNzDc;
                mb.NonZeroY = nonZeroY;
                mb.NonZeroUv = nonZeroUv;
            }

            // 3. Reconstruct macroblocks in row
            for (int mbX = 0; mbX < mbW; mbX++)
            {
                MbData mb = mbRow[mbX];
                int yOff = (mbY * 16 + 1) * yStride + (mbX * 16 + 1);
                int uvOff = (mbY * 8 + 1) * uvStride + (mbX * 8 + 1);
                bool hasTop = mbY > 0;
                bool hasLeft = mbX > 0;
                bool isRightmost = mbX == mbW - 1;

                if (mb.IsI4x4)
                {
                    FillTopRightLane(yPlane, yOff, yStride, hasTop, isRightmost);
                    for (int n = 0; n < 16; n++)
                    {
                        int sub = yOff + (n % 4) * 4 + (n / 4) * 4 * yStride;
                        PredictLuma4(yPlane, sub, yStride, mb.IModes[n]);
                        AddResidualBlock(yPlane, mb.Coeffs.AsSpan(n * 16, 16), sub, yStride);
                    }
                }
                else
                {
                    PredictLuma16(yPlane, yOff, yStride, mb.IModes[0], hasTop, hasLeft);
                    for (int n = 0; n < 16; n++)
                    {
                        int sub = yOff + (n % 4) * 4 + (n / 4) * 4 * yStride;
                        AddResidualBlock(yPlane, mb.Coeffs.AsSpan(n * 16, 16), sub, yStride);
                    }
                }

                PredictChroma8(uPlane, uvOff, uvStride, mb.UvMode, hasTop, hasLeft);
                PredictChroma8(vPlane, uvOff, uvStride, mb.UvMode, hasTop, hasLeft);
                for (int n = 0; n < 4; n++)
                {
                    int sub = uvOff + (n % 2) * 4 + (n / 2) * 4 * uvStride;
                    AddResidualBlock(uPlane, mb.Coeffs.AsSpan((16 + n) * 16, 16), sub, uvStride);
                    AddResidualBlock(vPlane, mb.Coeffs.AsSpan((20 + n) * 16, 16), sub, uvStride);
                }

                bool skip = (useSkip && mb.Skip) || ((mb.NonZeroY | mb.NonZeroUv) == 0);
                FInfo fi = fstrengths[mb.Segment * 2 + (mb.IsI4x4 ? 1 : 0)];
                fi.FInner = mb.IsI4x4 || !skip;
                allFInfo[mbY * mbW + mbX] = fi;
            }
        }

        // 4. Loop filter whole frame
        if (filterType != 0)
        {
            for (int mbY = 0; mbY < mbH; mbY++)
            {
                for (int mbX = 0; mbX < mbW; mbX++)
                {
                    FInfo fi = allFInfo[mbY * mbW + mbX];
                    if (fi.FLimit == 0)
                        continue;
                    int yOff = (mbY * 16 + 1) * yStride + (mbX * 16 + 1);
                    int uvOff = (mbY * 8 + 1) * uvStride + (mbX * 8 + 1);
                    bool doLeft = mbX > 0;
                    bool doTop = mbY > 0;
                    if (filterType == 1)
                        FilterMbSimple(yPlane, yOff, yStride, doLeft, doTop, fi);
                    else
                        FilterMbNormal(yPlane, yOff, yStride, uPlane, vPlane, uvOff, uvStride, doLeft, doTop, fi);
                }
            }
        }

        // 5. Fancy YUV420 -> RGBA upsampling
        rgba = UpsampleYuv420ToRgba(yPlane, yStride, uPlane, vPlane, uvStride, width, height);
        return true;
    }

    // =========================================================================
    // Lossy VP8 Encoder
    // =========================================================================

    private struct EncBlock
    {
        public short L0, L1, L2, L3, L4, L5, L6, L7, L8, L9, L10, L11, L12, L13, L14, L15;
        public int Last;

        public void SetFrom(ReadOnlySpan<short> levels, int last)
        {
            L0 = levels[0]; L1 = levels[1]; L2 = levels[2]; L3 = levels[3];
            L4 = levels[4]; L5 = levels[5]; L6 = levels[6]; L7 = levels[7];
            L8 = levels[8]; L9 = levels[9]; L10 = levels[10]; L11 = levels[11];
            L12 = levels[12]; L13 = levels[13]; L14 = levels[14]; L15 = levels[15];
            Last = last;
        }

        public void CopyTo(Span<short> dst)
        {
            dst[0] = L0; dst[1] = L1; dst[2] = L2; dst[3] = L3;
            dst[4] = L4; dst[5] = L5; dst[6] = L6; dst[7] = L7;
            dst[8] = L8; dst[9] = L9; dst[10] = L10; dst[11] = L11;
            dst[12] = L12; dst[13] = L13; dst[14] = L14; dst[15] = L15;
        }
    }

    public static byte[] EncodeVp8(ReadOnlySpan<byte> rgba, int width, int height, int stride, int quality)
    {
        int qClamped = Math.Clamp(quality, 0, 100);
        int baseQ = QualityToBaseQ[qClamped];

        int dcQ = DcTable[baseQ];
        int acQ = AcTable[baseQ];
        int y2DcQ = dcQ * 2;
        int y2AcQ = Math.Max((acQ * 101581) >> 16, 8);
        int uvDcQ = DcTable[Math.Clamp(baseQ, 0, 117)];
        int uvAcQ = acQ;

        // Moderate loop filter level for smooth blocking suppression without blurring
        int filterLevel = Math.Clamp((baseQ * 3) >> 3, 0, 63);

        int mbW = (width + 15) >> 4;
        int mbH = (height + 15) >> 4;
        int srcYStride = mbW * 16;
        int srcUvStride = mbW * 8;

        // Convert RGBA -> Studio-swing YUV420 (padded to macroblocks)
        byte[] srcY = new byte[srcYStride * mbH * 16];
        byte[] srcU = new byte[srcUvStride * mbH * 8];
        byte[] srcV = new byte[srcUvStride * mbH * 8];
        ConvertRgbaToYuv420(rgba, width, height, stride, srcY, srcYStride, srcU, srcV, srcUvStride);

        // Reconstruction planes with borders matching decoder
        int recYStride = 1 + mbW * 16 + 4;
        int recUvStride = 1 + mbW * 8;
        byte[] recY = new byte[recYStride * (1 + mbH * 16)];
        byte[] recU = new byte[recUvStride * (1 + mbH * 8)];
        byte[] recV = new byte[recUvStride * (1 + mbH * 8)];
        InitPlaneBorders(recY, recYStride, 1 + mbH * 16);
        InitPlaneBorders(recU, recUvStride, 1 + mbH * 8);
        InitPlaneBorders(recV, recUvStride, 1 + mbH * 8);

        Vp8BoolEncoder ctrlEnc = new Vp8BoolEncoder();
        Vp8BoolEncoder tokEnc = new Vp8BoolEncoder();

        // Partition 0 control header
        ctrlEnc.PutFlag(false); // color space
        ctrlEnc.PutFlag(false); // clamp type
        ctrlEnc.PutFlag(false); // use_segment = false
        ctrlEnc.PutFlag(false); // filter.simple = false
        ctrlEnc.PutLiteral(6, (uint)filterLevel);
        ctrlEnc.PutLiteral(3, 0); // sharpness = 0
        ctrlEnc.PutFlag(false); // use_lf_delta = false
        ctrlEnc.PutLiteral(2, 0); // 1 token partition
        ctrlEnc.PutLiteral(7, (uint)baseQ);
        for (int i = 0; i < 5; i++)
            ctrlEnc.PutFlag(false); // zero quant deltas
        ctrlEnc.PutFlag(false); // refresh_entropy_probs
        for (int i = 0; i < CoeffsUpdateProba.Length; i++)
            ctrlEnc.PutBool(CoeffsUpdateProba[i], false); // use default CoeffsProba0
        ctrlEnc.PutFlag(false); // use_skip = false

        byte[] intraT = new byte[4 * mbW];
        Array.Fill(intraT, B_DC_PRED);
        Span<byte> intraL = stackalloc byte[4];
        uint[] topNz = new uint[mbW];
        uint[] topNzDc = new uint[mbW];

        Span<byte> candPred16 = stackalloc byte[256];
        Span<byte> bestPred16 = stackalloc byte[256];
        Span<short> residual = stackalloc short[16];
        Span<short> dct = stackalloc short[16];
        Span<short> qLevels = stackalloc short[16];
        Span<short> qRecon = stackalloc short[16];
        Span<short> dc16 = stackalloc short[16];
        Span<short> whtCoeffs = stackalloc short[16];
        Span<short> whtLevels = stackalloc short[16];
        Span<short> whtRecon = stackalloc short[16];
        Span<short> fullRecon16 = stackalloc short[384];
        EncBlock[] lumaBlocks16 = new EncBlock[16];
        EncBlock[] lumaBlocks4 = new EncBlock[16];
        EncBlock[] chromaBlocks = new EncBlock[8];
        Span<byte> chosenBModes = stackalloc byte[16];
        Span<byte> savedYBlock = stackalloc byte[16 * 20];

        int rdLambda = Math.Max(4, (acQ * acQ) >> 3);

        for (int mbY = 0; mbY < mbH; mbY++)
        {
            intraL.Fill(B_DC_PRED);
            uint leftNz = 0;
            uint leftNzDc = 0;

            for (int mbX = 0; mbX < mbW; mbX++)
            {
                bool hasTop = mbY > 0;
                bool hasLeft = mbX > 0;
                bool isRightmost = mbX == mbW - 1;
                int yOff = (mbY * 16 + 1) * recYStride + (mbX * 16 + 1);
                int uvOff = (mbY * 8 + 1) * recUvStride + (mbX * 8 + 1);
                int srcYOff = mbY * 16 * srcYStride + mbX * 16;
                int srcUvOff = mbY * 8 * srcUvStride + mbX * 8;

                // --- Evaluate 16x16 Luma Modes ---
                byte bestMode16 = DC_PRED;
                int bestSad16 = int.MaxValue;
                for (byte m = 0; m < 4; m++)
                {
                    if ((m == V_PRED && !hasTop) || (m == H_PRED && !hasLeft) || (m == TM_PRED && (!hasTop || !hasLeft)))
                        continue;

                    PredictLuma16ToBuf(recY, yOff, recYStride, m, hasTop, hasLeft, candPred16);
                    int sad = 0;
                    for (int r = 0; r < 16; r++)
                    {
                        int sRow = srcYOff + r * srcYStride;
                        int pRow = r * 16;
                        for (int c = 0; c < 16; c++)
                            sad += Math.Abs(srcY[sRow + c] - candPred16[pRow + c]);
                    }
                    if (sad < bestSad16)
                    {
                        bestSad16 = sad;
                        bestMode16 = m;
                        candPred16.CopyTo(bestPred16);
                    }
                }

                // Transform + Quantize 16x16 candidate
                fullRecon16.Clear();
                for (int n = 0; n < 16; n++)
                {
                    int bx = (n & 3) * 4;
                    int by = (n >> 2) * 4;
                    for (int r = 0; r < 4; r++)
                    {
                        int sIdx = srcYOff + (by + r) * srcYStride + bx;
                        int pIdx = (by + r) * 16 + bx;
                        for (int c = 0; c < 4; c++)
                            residual[r * 4 + c] = (short)(srcY[sIdx + c] - bestPred16[pIdx + c]);
                    }
                    Fdct4x4(residual, dct);
                    dc16[n] = dct[0];
                    int lastAc = QuantizeBlock(dct, dcQ, acQ, 1, qLevels, qRecon);
                    lumaBlocks16[n].SetFrom(qLevels, lastAc);
                    qRecon.CopyTo(fullRecon16.Slice(n * 16, 16));
                }

                Fwht(dc16, whtCoeffs);
                int y2Last = QuantizeBlock(whtCoeffs, y2DcQ, y2AcQ, 0, whtLevels, whtRecon);
                EncBlock y2Block = default;
                y2Block.SetFrom(whtLevels, y2Last);
                TransformWht(whtRecon, fullRecon16);

                // Compute 16x16 reconstructed SSE + rate proxy
                long cost16 = 0;
                int nzCount16 = y2Last >= 0 ? (y2Last + 1) : 0;
                for (int n = 0; n < 16; n++)
                {
                    int bx = (n & 3) * 4;
                    int by = (n >> 2) * 4;
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            candPred16[(by + r) * 16 + bx + c] = bestPred16[(by + r) * 16 + bx + c];

                    AddResidualBlock(candPred16, fullRecon16.Slice(n * 16, 16), by * 16 + bx, 16);
                    if (lumaBlocks16[n].Last >= 1)
                        nzCount16 += lumaBlocks16[n].Last;
                }
                for (int r = 0; r < 16; r++)
                {
                    int sRow = srcYOff + r * srcYStride;
                    int pRow = r * 16;
                    for (int c = 0; c < 16; c++)
                    {
                        int d = srcY[sRow + c] - candPred16[pRow + c];
                        cost16 += d * d;
                    }
                }
                cost16 += (long)rdLambda * (4 + nzCount16 * 3);

                // --- Check whether 4x4 Intra Search is worthwhile (high-detail macroblocks) ---
                bool useI4x4 = false;
                if (bestSad16 > 512)
                {
                    // Save current 16x16 patch in recY
                    for (int r = 0; r < 16; r++)
                        recY.AsSpan(yOff + r * recYStride, 20).CopyTo(savedYBlock.Slice(r * 20, 20));

                    FillTopRightLane(recY, yOff, recYStride, hasTop, isRightmost);
                    long cost4 = (long)rdLambda * 44; // overhead of coding 16 B-modes
                    for (int n = 0; n < 16; n++)
                    {
                        int sx = n & 3;
                        int sy = n >> 2;
                        int sub = yOff + sx * 4 + sy * 4 * recYStride;
                        int sSub = srcYOff + sx * 4 + sy * 4 * srcYStride;

                        byte bestBm = B_DC_PRED;
                        int bestSubSad = int.MaxValue;
                        for (byte bm = 0; bm < NumBModes; bm++)
                        {
                            PredictLuma4(recY, sub, recYStride, bm);
                            int sad = 0;
                            for (int r = 0; r < 4; r++)
                            {
                                int sr = sSub + r * srcYStride;
                                int pr = sub + r * recYStride;
                                for (int c = 0; c < 4; c++)
                                    sad += Math.Abs(srcY[sr + c] - recY[pr + c]);
                            }
                            if (sad < bestSubSad)
                            {
                                bestSubSad = sad;
                                bestBm = bm;
                            }
                        }

                        chosenBModes[n] = bestBm;
                        PredictLuma4(recY, sub, recYStride, bestBm);
                        for (int r = 0; r < 4; r++)
                        {
                            int sr = sSub + r * srcYStride;
                            int pr = sub + r * recYStride;
                            for (int c = 0; c < 4; c++)
                                residual[r * 4 + c] = (short)(srcY[sr + c] - recY[pr + c]);
                        }
                        Fdct4x4(residual, dct);
                        int last4 = QuantizeBlock(dct, dcQ, acQ, 0, qLevels, qRecon);
                        lumaBlocks4[n].SetFrom(qLevels, last4);
                        AddResidualBlock(recY, qRecon, sub, recYStride);

                        for (int r = 0; r < 4; r++)
                        {
                            int sr = sSub + r * srcYStride;
                            int pr = sub + r * recYStride;
                            for (int c = 0; c < 4; c++)
                            {
                                int d = srcY[sr + c] - recY[pr + c];
                                cost4 += d * d;
                            }
                        }
                        if (last4 >= 0)
                            cost4 += (long)rdLambda * (last4 + 1) * 3;
                    }

                    if (cost4 < cost16)
                    {
                        useI4x4 = true;
                    }
                    else
                    {
                        for (int r = 0; r < 16; r++)
                            savedYBlock.Slice(r * 20, 20).CopyTo(recY.AsSpan(yOff + r * recYStride, 20));
                    }
                }

                if (!useI4x4)
                {
                    for (int r = 0; r < 16; r++)
                        candPred16.Slice(r * 16, 16).CopyTo(recY.AsSpan(yOff + r * recYStride, 16));
                }

                // --- Chroma 8x8 Mode Search + Reconstruction ---
                byte bestUvMode = DC_PRED;
                int bestUvSad = int.MaxValue;
                for (byte m = 0; m < 4; m++)
                {
                    if ((m == V_PRED && !hasTop) || (m == H_PRED && !hasLeft) || (m == TM_PRED && (!hasTop || !hasLeft)))
                        continue;

                    PredictChroma8(recU, uvOff, recUvStride, m, hasTop, hasLeft);
                    PredictChroma8(recV, uvOff, recUvStride, m, hasTop, hasLeft);
                    int sad = 0;
                    for (int r = 0; r < 8; r++)
                    {
                        int sr = srcUvOff + r * srcUvStride;
                        int pr = uvOff + r * recUvStride;
                        for (int c = 0; c < 8; c++)
                            sad += Math.Abs(srcU[sr + c] - recU[pr + c]) + Math.Abs(srcV[sr + c] - recV[pr + c]);
                    }
                    if (sad < bestUvSad)
                    {
                        bestUvSad = sad;
                        bestUvMode = m;
                    }
                }

                PredictChroma8(recU, uvOff, recUvStride, bestUvMode, hasTop, hasLeft);
                PredictChroma8(recV, uvOff, recUvStride, bestUvMode, hasTop, hasLeft);
                for (int n = 0; n < 4; n++)
                {
                    int sx = (n & 1) * 4;
                    int sy = (n >> 1) * 4;
                    int sub = uvOff + sx + sy * recUvStride;
                    int sSub = srcUvOff + sx + sy * srcUvStride;

                    // U sub-block
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            residual[r * 4 + c] = (short)(srcU[sSub + r * srcUvStride + c] - recU[sub + r * recUvStride + c]);
                    Fdct4x4(residual, dct);
                    int uLast = QuantizeBlock(dct, uvDcQ, uvAcQ, 0, qLevels, qRecon);
                    chromaBlocks[n].SetFrom(qLevels, uLast);
                    AddResidualBlock(recU, qRecon, sub, recUvStride);

                    // V sub-block
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            residual[r * 4 + c] = (short)(srcV[sSub + r * srcUvStride + c] - recV[sub + r * recUvStride + c]);
                    Fdct4x4(residual, dct);
                    int vLast = QuantizeBlock(dct, uvDcQ, uvAcQ, 0, qLevels, qRecon);
                    chromaBlocks[4 + n].SetFrom(qLevels, vLast);
                    AddResidualBlock(recV, qRecon, sub, recUvStride);
                }

                // --- Emit Control Partition Intra Modes ---
                int topOff = 4 * mbX;
                ctrlEnc.PutBool(145, !useI4x4);
                if (useI4x4)
                {
                    for (int sy = 0; sy < 4; sy++)
                    {
                        byte leftMode = intraL[sy];
                        for (int sx = 0; sx < 4; sx++)
                        {
                            byte topMode = intraT[topOff + sx];
                            byte bm = chosenBModes[sy * 4 + sx];
                            PutBMode(ctrlEnc, BModeProbIndex(topMode, leftMode), bm);
                            intraT[topOff + sx] = bm;
                            leftMode = bm;
                        }
                        intraL[sy] = leftMode;
                    }
                }
                else
                {
                    PutYMode16(ctrlEnc, bestMode16);
                    intraT[topOff] = intraT[topOff + 1] = intraT[topOff + 2] = intraT[topOff + 3] = bestMode16;
                    intraL.Fill(bestMode16);
                }
                PutUvMode(ctrlEnc, bestUvMode);

                // --- Emit Token Partition Residuals ---
                uint mbNz = topNz[mbX];
                uint mbNzDc = topNzDc[mbX];
                int first, acType;
                if (useI4x4)
                {
                    first = 0;
                    acType = 3;
                }
                else
                {
                    y2Block.CopyTo(qLevels);
                    int c = (int)(mbNzDc + leftNzDc);
                    int nz = PutCoeffs(tokEnc, CoeffsProba0, 1, c, qLevels, 0, y2Block.Last);
                    uint dcNz = nz > 0 ? 1u : 0u;
                    mbNzDc = dcNz;
                    leftNzDc = dcNz;
                    first = 1;
                    acType = 0;
                }

                uint tnz = mbNz & 0x0f;
                uint lnz = leftNz & 0x0f;
                int bIdx = 0;
                EncBlock[] activeLuma = useI4x4 ? lumaBlocks4 : lumaBlocks16;
                for (int sy = 0; sy < 4; sy++)
                {
                    uint l = lnz & 1;
                    for (int sx = 0; sx < 4; sx++)
                    {
                        activeLuma[bIdx].CopyTo(qLevels);
                        int c = (int)(l + (tnz & 1));
                        int nz = PutCoeffs(tokEnc, CoeffsProba0, acType, c, qLevels, first, activeLuma[bIdx].Last);
                        l = nz > first ? 1u : 0u;
                        tnz = (tnz >> 1) | (l << 7);
                        bIdx++;
                    }
                    tnz >>= 4;
                    lnz = (lnz >> 1) | (l << 7);
                }
                uint outTopNz = tnz;
                uint outLeftNz = lnz >> 4;

                for (int ch = 0; ch < 4; ch += 2)
                {
                    uint ctnz = mbNz >> (4 + ch);
                    uint clnz = leftNz >> (4 + ch);
                    for (int sy = 0; sy < 2; sy++)
                    {
                        uint l = clnz & 1;
                        for (int sx = 0; sx < 2; sx++)
                        {
                            int cIdx = ch * 2 + sy * 2 + sx;
                            chromaBlocks[cIdx].CopyTo(qLevels);
                            int c = (int)(l + (ctnz & 1));
                            int nz = PutCoeffs(tokEnc, CoeffsProba0, 2, c, qLevels, 0, chromaBlocks[cIdx].Last);
                            l = nz > 0 ? 1u : 0u;
                            ctnz = (ctnz >> 1) | (l << 3);
                        }
                        ctnz >>= 2;
                        clnz = (clnz >> 1) | (l << 5);
                    }
                    outTopNz |= (ctnz << 4) << ch;
                    outLeftNz |= (clnz & 0xf0) << ch;
                }

                topNz[mbX] = outTopNz & 0xff;
                leftNz = outLeftNz & 0xff;
                topNzDc[mbX] = mbNzDc;
            }
        }

        byte[] part0Bytes = ctrlEnc.Finish();
        byte[] tokBytes = tokEnc.Finish();
        uint frameTag = (1u << 4) | ((uint)part0Bytes.Length << 5);

        byte[] vp8 = new byte[10 + part0Bytes.Length + tokBytes.Length];
        vp8[0] = (byte)(frameTag & 0xff);
        vp8[1] = (byte)((frameTag >> 8) & 0xff);
        vp8[2] = (byte)((frameTag >> 16) & 0xff);
        vp8[3] = 0x9d;
        vp8[4] = 0x01;
        vp8[5] = 0x2a;
        vp8[6] = (byte)(width & 0xff);
        vp8[7] = (byte)((width >> 8) & 0x3f);
        vp8[8] = (byte)(height & 0xff);
        vp8[9] = (byte)((height >> 8) & 0x3f);

        Buffer.BlockCopy(part0Bytes, 0, vp8, 10, part0Bytes.Length);
        Buffer.BlockCopy(tokBytes, 0, vp8, 10 + part0Bytes.Length, tokBytes.Length);
        return vp8;
    }

    // =========================================================================
    // Shared Bitstream, Transform, Predictor, Filter & Color Space Kernels
    // =========================================================================

    private static void InitPlaneBorders(byte[] plane, int stride, int rows)
    {
        plane.AsSpan(0, stride).Fill(127);
        for (int r = 1; r < rows; r++)
            plane[r * stride] = 129;
    }

    private static byte ReadYMode16(Vp8BoolDecoder br)
    {
        if (br.ReadBool(156))
            return br.ReadBool(128) ? TM_PRED : H_PRED;
        return br.ReadBool(163) ? V_PRED : DC_PRED;
    }

    private static void PutYMode16(Vp8BoolEncoder enc, byte mode)
    {
        switch (mode)
        {
            case DC_PRED:
                enc.PutBool(156, false);
                enc.PutBool(163, false);
                break;
            case V_PRED:
                enc.PutBool(156, false);
                enc.PutBool(163, true);
                break;
            case H_PRED:
                enc.PutBool(156, true);
                enc.PutBool(128, false);
                break;
            default: // TM_PRED
                enc.PutBool(156, true);
                enc.PutBool(128, true);
                break;
        }
    }

    private static byte ReadUvMode(Vp8BoolDecoder br)
    {
        if (!br.ReadBool(142)) return DC_PRED;
        if (!br.ReadBool(114)) return V_PRED;
        return br.ReadBool(183) ? TM_PRED : H_PRED;
    }

    private static void PutUvMode(Vp8BoolEncoder enc, byte mode)
    {
        switch (mode)
        {
            case V_PRED:
                enc.PutBool(142, true);
                enc.PutBool(114, false);
                break;
            case H_PRED:
                enc.PutBool(142, true);
                enc.PutBool(114, true);
                enc.PutBool(183, false);
                break;
            case TM_PRED:
                enc.PutBool(142, true);
                enc.PutBool(114, true);
                enc.PutBool(183, true);
                break;
            default:
                enc.PutBool(142, false);
                break;
        }
    }

    private static byte ReadBMode(Vp8BoolDecoder br, int pBase)
    {
        if (!br.ReadBool(BModesProba[pBase])) return B_DC_PRED;
        if (!br.ReadBool(BModesProba[pBase + 1])) return B_TM_PRED;
        if (!br.ReadBool(BModesProba[pBase + 2])) return B_VE_PRED;
        if (!br.ReadBool(BModesProba[pBase + 3]))
        {
            if (!br.ReadBool(BModesProba[pBase + 4])) return B_HE_PRED;
            return !br.ReadBool(BModesProba[pBase + 5]) ? B_RD_PRED : B_VR_PRED;
        }
        if (!br.ReadBool(BModesProba[pBase + 6])) return B_LD_PRED;
        if (!br.ReadBool(BModesProba[pBase + 7])) return B_VL_PRED;
        return !br.ReadBool(BModesProba[pBase + 8]) ? B_HD_PRED : B_HU_PRED;
    }

    private static void PutBMode(Vp8BoolEncoder enc, int pBase, byte mode)
    {
        if (mode == B_DC_PRED) { enc.PutBool(BModesProba[pBase], false); return; }
        enc.PutBool(BModesProba[pBase], true);
        if (mode == B_TM_PRED) { enc.PutBool(BModesProba[pBase + 1], false); return; }
        enc.PutBool(BModesProba[pBase + 1], true);
        if (mode == B_VE_PRED) { enc.PutBool(BModesProba[pBase + 2], false); return; }
        enc.PutBool(BModesProba[pBase + 2], true);

        switch (mode)
        {
            case B_HE_PRED:
                enc.PutBool(BModesProba[pBase + 3], false);
                enc.PutBool(BModesProba[pBase + 4], false);
                break;
            case B_RD_PRED:
                enc.PutBool(BModesProba[pBase + 3], false);
                enc.PutBool(BModesProba[pBase + 4], true);
                enc.PutBool(BModesProba[pBase + 5], false);
                break;
            case B_VR_PRED:
                enc.PutBool(BModesProba[pBase + 3], false);
                enc.PutBool(BModesProba[pBase + 4], true);
                enc.PutBool(BModesProba[pBase + 5], true);
                break;
            case B_LD_PRED:
                enc.PutBool(BModesProba[pBase + 3], true);
                enc.PutBool(BModesProba[pBase + 6], false);
                break;
            case B_VL_PRED:
                enc.PutBool(BModesProba[pBase + 3], true);
                enc.PutBool(BModesProba[pBase + 6], true);
                enc.PutBool(BModesProba[pBase + 7], false);
                break;
            case B_HD_PRED:
                enc.PutBool(BModesProba[pBase + 3], true);
                enc.PutBool(BModesProba[pBase + 6], true);
                enc.PutBool(BModesProba[pBase + 7], true);
                enc.PutBool(BModesProba[pBase + 8], false);
                break;
            default:
                enc.PutBool(BModesProba[pBase + 3], true);
                enc.PutBool(BModesProba[pBase + 6], true);
                enc.PutBool(BModesProba[pBase + 7], true);
                enc.PutBool(BModesProba[pBase + 8], true);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint NzCodeBits(uint nzCoeffs, int nz, bool dcNz)
    {
        uint code = nz > 3 ? 3u : (nz > 1 ? 2u : (dcNz ? 1u : 0u));
        return (nzCoeffs << 2) | code;
    }

    private static int GetCoeffs(Vp8BoolDecoder br, byte[] probs, int plane, int ctx, int dqDc, int dqAc, int nStart, Span<short> dst)
    {
        int n = nStart;
        int pIdx = CoeffProbIndex(plane, Bands[n], ctx, 0);
        while (true)
        {
            if (!br.ReadBool(probs[pIdx]))
                return n;
            while (!br.ReadBool(probs[pIdx + 1]))
            {
                n++;
                if (n == 16)
                    return 16;
                pIdx = CoeffProbIndex(plane, Bands[n], 0, 0);
            }
            int v;
            if (br.ReadBool(probs[pIdx + 2]))
            {
                v = GetLargeValue(br, probs, pIdx);
                pIdx = CoeffProbIndex(plane, Bands[n + 1], 2, 0);
            }
            else
            {
                v = 1;
                pIdx = CoeffProbIndex(plane, Bands[n + 1], 1, 0);
            }
            int dq = n > 0 ? dqAc : dqDc;
            dst[Zigzag[n]] = (short)(br.ApplySign(v) * dq);
            n++;
            if (n == 16)
                return 16;
        }
    }

    private static int GetLargeValue(Vp8BoolDecoder br, byte[] probs, int pIdx)
    {
        if (!br.ReadBool(probs[pIdx + 3]))
            return br.ReadBool(probs[pIdx + 4]) ? 3 + (br.ReadBool(probs[pIdx + 5]) ? 1 : 0) : 2;
        if (!br.ReadBool(probs[pIdx + 6]))
        {
            if (br.ReadBool(probs[pIdx + 7]))
            {
                int hi = br.ReadBool(165) ? 2 : 0;
                return 7 + hi + (br.ReadBool(145) ? 1 : 0);
            }
            return 5 + (br.ReadBool(159) ? 1 : 0);
        }
        int bit1 = br.ReadBool(probs[pIdx + 8]) ? 1 : 0;
        int bit0 = br.ReadBool(probs[pIdx + 9 + bit1]) ? 1 : 0;
        int cat = 2 * bit1 + bit0;
        byte[] catProbs = Cat3456[cat];
        int v = 0;
        for (int i = 0; i < catProbs.Length; i++)
            v = (v << 1) + (br.ReadBool(catProbs[i]) ? 1 : 0);
        return v + 3 + (8 << cat);
    }

    private static int PutCoeffs(Vp8BoolEncoder enc, byte[] probs, int plane, int ctx, ReadOnlySpan<short> levels, int first, int last)
    {
        int n = first;
        int pIdx = CoeffProbIndex(plane, Bands[n], ctx, 0);
        while (true)
        {
            if (n > last)
            {
                enc.PutBool(probs[pIdx], false);
                return last + 1;
            }
            enc.PutBool(probs[pIdx], true);
            while (levels[n] == 0)
            {
                enc.PutBool(probs[pIdx + 1], false);
                n++;
                pIdx = CoeffProbIndex(plane, Bands[n], 0, 0);
            }
            enc.PutBool(probs[pIdx + 1], true);
            int v = Math.Abs((int)levels[n]);
            int nextIdx;
            if (v == 1)
            {
                enc.PutBool(probs[pIdx + 2], false);
                nextIdx = CoeffProbIndex(plane, Bands[n + 1], 1, 0);
            }
            else
            {
                enc.PutBool(probs[pIdx + 2], true);
                PutLargeValue(enc, probs, pIdx, v);
                nextIdx = CoeffProbIndex(plane, Bands[n + 1], 2, 0);
            }
            enc.PutFlag(levels[n] < 0);
            n++;
            if (n == 16)
                return 16;
            pIdx = nextIdx;
        }
    }

    private static void PutLargeValue(Vp8BoolEncoder enc, byte[] probs, int pIdx, int v)
    {
        if (v <= 4)
        {
            enc.PutBool(probs[pIdx + 3], false);
            if (v == 2)
            {
                enc.PutBool(probs[pIdx + 4], false);
            }
            else
            {
                enc.PutBool(probs[pIdx + 4], true);
                enc.PutBool(probs[pIdx + 5], v == 4);
            }
        }
        else if (v <= 10)
        {
            enc.PutBool(probs[pIdx + 3], true);
            enc.PutBool(probs[pIdx + 6], false);
            if (v <= 6)
            {
                enc.PutBool(probs[pIdx + 7], false);
                enc.PutBool(159, v == 6);
            }
            else
            {
                enc.PutBool(probs[pIdx + 7], true);
                int hi = v - 7;
                enc.PutBool(165, ((hi >> 1) & 1) != 0);
                enc.PutBool(145, (hi & 1) != 0);
            }
        }
        else
        {
            enc.PutBool(probs[pIdx + 3], true);
            enc.PutBool(probs[pIdx + 6], true);
            int cat = v <= 18 ? 0 : (v <= 34 ? 1 : (v <= 66 ? 2 : 3));
            enc.PutBool(probs[pIdx + 8], ((cat >> 1) & 1) != 0);
            enc.PutBool(probs[pIdx + 9 + (cat >> 1)], (cat & 1) != 0);
            int extra = v - 3 - (8 << cat);
            byte[] catProbs = Cat3456[cat];
            int nbits = catProbs.Length;
            for (int i = 0; i < nbits; i++)
                enc.PutBool(catProbs[i], ((extra >> (nbits - 1 - i)) & 1) != 0);
        }
    }

    // -------------------------------------------------------------------------
    // Forward / Inverse DCT & WHT + Quantization
    // -------------------------------------------------------------------------

    private static void Fdct4x4(ReadOnlySpan<short> residual, Span<short> dst)
    {
        Span<int> tmp = stackalloc int[16];
        for (int i = 0; i < 4; i++)
        {
            int d0 = residual[i * 4];
            int d1 = residual[i * 4 + 1];
            int d2 = residual[i * 4 + 2];
            int d3 = residual[i * 4 + 3];
            int a0 = d0 + d3;
            int a1 = d1 + d2;
            int a2 = d1 - d2;
            int a3 = d0 - d3;
            tmp[i * 4] = (a0 + a1) * 8;
            tmp[i * 4 + 1] = (a2 * 2217 + a3 * 5352 + 1812) >> 9;
            tmp[i * 4 + 2] = (a0 - a1) * 8;
            tmp[i * 4 + 3] = (a3 * 2217 - a2 * 5352 + 937) >> 9;
        }
        for (int i = 0; i < 4; i++)
        {
            int a0 = tmp[i] + tmp[12 + i];
            int a1 = tmp[4 + i] + tmp[8 + i];
            int a2 = tmp[4 + i] - tmp[8 + i];
            int a3 = tmp[i] - tmp[12 + i];
            dst[i] = (short)((a0 + a1 + 7) >> 4);
            dst[4 + i] = (short)(((a2 * 2217 + a3 * 5352 + 12000) >> 16) + (a3 != 0 ? 1 : 0));
            dst[8 + i] = (short)((a0 - a1 + 7) >> 4);
            dst[12 + i] = (short)((a3 * 2217 - a2 * 5352 + 51000) >> 16);
        }
    }

    private static void Fwht(ReadOnlySpan<short> dc, Span<short> dst)
    {
        Span<int> tmp = stackalloc int[16];
        for (int i = 0; i < 4; i++)
        {
            int a0 = dc[i * 4] + dc[i * 4 + 2];
            int a1 = dc[i * 4 + 1] + dc[i * 4 + 3];
            int a2 = dc[i * 4 + 1] - dc[i * 4 + 3];
            int a3 = dc[i * 4] - dc[i * 4 + 2];
            tmp[i * 4] = a0 + a1;
            tmp[i * 4 + 1] = a3 + a2;
            tmp[i * 4 + 2] = a3 - a2;
            tmp[i * 4 + 3] = a0 - a1;
        }
        for (int i = 0; i < 4; i++)
        {
            int a0 = tmp[i] + tmp[8 + i];
            int a1 = tmp[4 + i] + tmp[12 + i];
            int a2 = tmp[4 + i] - tmp[12 + i];
            int a3 = tmp[i] - tmp[8 + i];
            dst[i] = (short)((a0 + a1) >> 1);
            dst[4 + i] = (short)((a3 + a2) >> 1);
            dst[8 + i] = (short)((a3 - a2) >> 1);
            dst[12 + i] = (short)((a0 - a1) >> 1);
        }
    }

    private static int QuantizeBlock(ReadOnlySpan<short> coeffs, int dcQ, int acQ, int first, Span<short> levels, Span<short> recon)
    {
        const int QFix = 17;
        const int QBias = 1 << (QFix - 1);
        int dcIq = ((1 << QFix) + dcQ / 2) / dcQ;
        int acIq = ((1 << QFix) + acQ / 2) / acQ;

        levels.Clear();
        recon.Clear();
        int last = first - 1;
        for (int n = first; n < 16; n++)
        {
            int j = Zigzag[n];
            int q = j == 0 ? dcQ : acQ;
            int iq = j == 0 ? dcIq : acIq;
            int c = coeffs[j];
            int mag = Math.Abs(c);
            int lvl = Math.Min(2047, (mag * iq + QBias) >> QFix);
            if (c < 0) lvl = -lvl;
            if (lvl != 0) last = n;
            levels[n] = (short)lvl;
            recon[j] = (short)(lvl * q);
        }
        return last;
    }

    private static void TransformWht(ReadOnlySpan<short> input, Span<short> outCoeffs)
    {
        Span<int> tmp = stackalloc int[16];
        for (int i = 0; i < 4; i++)
        {
            int a0 = input[i] + input[12 + i];
            int a1 = input[4 + i] + input[8 + i];
            int a2 = input[4 + i] - input[8 + i];
            int a3 = input[i] - input[12 + i];
            tmp[i] = a0 + a1;
            tmp[8 + i] = a0 - a1;
            tmp[4 + i] = a3 + a2;
            tmp[12 + i] = a3 - a2;
        }
        for (int i = 0; i < 4; i++)
        {
            int dc = tmp[i * 4] + 3;
            int a0 = dc + tmp[3 + i * 4];
            int a1 = tmp[1 + i * 4] + tmp[2 + i * 4];
            int a2 = tmp[1 + i * 4] - tmp[2 + i * 4];
            int a3 = dc - tmp[3 + i * 4];
            int b = i * 64;
            outCoeffs[b] = (short)((a0 + a1) >> 3);
            outCoeffs[b + 16] = (short)((a3 + a2) >> 3);
            outCoeffs[b + 32] = (short)((a0 - a1) >> 3);
            outCoeffs[b + 48] = (short)((a3 - a2) >> 3);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Mul1(int a) => ((a * 20091) >> 16) + a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Mul2(int a) => (a * 35468) >> 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clip8(int v) => (byte)Math.Clamp(v, 0, 255);

    private static void AddResidualBlock(Span<byte> plane, ReadOnlySpan<short> coeffs, int off, int stride)
    {
        bool hasAc = false;
        for (int k = 1; k < 16; k++)
        {
            if (coeffs[k] != 0)
            {
                hasAc = true;
                break;
            }
        }

        if (!hasAc)
        {
            if (coeffs[0] == 0)
                return;
            int d = (coeffs[0] + 4) >> 3;
            for (int r = 0; r < 4; r++)
            {
                int row = off + r * stride;
                for (int c = 0; c < 4; c++)
                    plane[row + c] = Clip8(plane[row + c] + d);
            }
            return;
        }

        Span<int> tmp = stackalloc int[16];
        for (int i = 0; i < 4; i++)
        {
            int in0 = coeffs[i], in8 = coeffs[8 + i], in4 = coeffs[4 + i], in12 = coeffs[12 + i];
            int a = in0 + in8;
            int b = in0 - in8;
            int c = Mul2(in4) - Mul1(in12);
            int d = Mul1(in4) + Mul2(in12);
            int bp = i * 4;
            tmp[bp] = a + d;
            tmp[bp + 1] = b + c;
            tmp[bp + 2] = b - c;
            tmp[bp + 3] = a - d;
        }
        for (int i = 0; i < 4; i++)
        {
            int dc = tmp[i] + 4;
            int t8 = tmp[8 + i];
            int a = dc + t8;
            int b = dc - t8;
            int c = Mul2(tmp[4 + i]) - Mul1(tmp[12 + i]);
            int d = Mul1(tmp[4 + i]) + Mul2(tmp[12 + i]);
            int row = off + i * stride;
            plane[row] = Clip8(plane[row] + ((a + d) >> 3));
            plane[row + 1] = Clip8(plane[row + 1] + ((b + c) >> 3));
            plane[row + 2] = Clip8(plane[row + 2] + ((b - c) >> 3));
            plane[row + 3] = Clip8(plane[row + 3] + ((a - d) >> 3));
        }
    }

    // -------------------------------------------------------------------------
    // Intra Prediction
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg2(int a, int b) => (byte)((a + b + 1) >> 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Avg3(int a, int b, int c) => (byte)((a + 2 * b + c + 2) >> 2);

    private static void FillTopRightLane(byte[] y, int yOff, int stride, bool hasTop, bool isRightmost)
    {
        int tr = yOff - stride + 16;
        if (hasTop && isRightmost)
        {
            byte last = y[yOff - stride + 15];
            y[tr] = y[tr + 1] = y[tr + 2] = y[tr + 3] = last;
        }
        byte b0 = y[tr], b1 = y[tr + 1], b2 = y[tr + 2], b3 = y[tr + 3];
        ReadOnlySpan<int> rows = stackalloc int[] { 3, 7, 11 };
        foreach (int r in rows)
        {
            int dst = yOff + r * stride + 16;
            y[dst] = b0; y[dst + 1] = b1; y[dst + 2] = b2; y[dst + 3] = b3;
        }
    }

    private static void TrueMotion(byte[] plane, int off, int stride, int size)
    {
        int tl = plane[off - stride - 1];
        Span<byte> top = stackalloc byte[16];
        plane.AsSpan(off - stride, size).CopyTo(top);
        for (int y = 0; y < size; y++)
        {
            int left = plane[off + y * stride - 1];
            int row = off + y * stride;
            for (int x = 0; x < size; x++)
                plane[row + x] = Clip8(top[x] + left - tl);
        }
    }

    private static void PredictLuma16(byte[] plane, int off, int stride, byte mode, bool hasTop, bool hasLeft)
    {
        switch (mode)
        {
            case DC_PRED:
            {
                byte v;
                if (hasTop && hasLeft)
                {
                    int dc = 16;
                    for (int j = 0; j < 16; j++)
                        dc += plane[off - stride + j] + plane[off + j * stride - 1];
                    v = (byte)(dc >> 5);
                }
                else if (hasTop)
                {
                    int dc = 8;
                    for (int j = 0; j < 16; j++) dc += plane[off - stride + j];
                    v = (byte)(dc >> 4);
                }
                else if (hasLeft)
                {
                    int dc = 8;
                    for (int j = 0; j < 16; j++) dc += plane[off + j * stride - 1];
                    v = (byte)(dc >> 4);
                }
                else v = 0x80;

                for (int r = 0; r < 16; r++)
                    plane.AsSpan(off + r * stride, 16).Fill(v);
                break;
            }
            case V_PRED:
                for (int r = 0; r < 16; r++)
                    plane.AsSpan(off - stride, 16).CopyTo(plane.AsSpan(off + r * stride, 16));
                break;
            case H_PRED:
                for (int r = 0; r < 16; r++)
                {
                    int row = off + r * stride;
                    plane.AsSpan(row, 16).Fill(plane[row - 1]);
                }
                break;
            case TM_PRED:
                TrueMotion(plane, off, stride, 16);
                break;
        }
    }

    private static void PredictLuma16ToBuf(byte[] plane, int off, int stride, byte mode, bool hasTop, bool hasLeft, Span<byte> dst16x16)
    {
        switch (mode)
        {
            case DC_PRED:
            {
                byte v;
                if (hasTop && hasLeft)
                {
                    int dc = 16;
                    for (int j = 0; j < 16; j++)
                        dc += plane[off - stride + j] + plane[off + j * stride - 1];
                    v = (byte)(dc >> 5);
                }
                else if (hasTop)
                {
                    int dc = 8;
                    for (int j = 0; j < 16; j++) dc += plane[off - stride + j];
                    v = (byte)(dc >> 4);
                }
                else if (hasLeft)
                {
                    int dc = 8;
                    for (int j = 0; j < 16; j++) dc += plane[off + j * stride - 1];
                    v = (byte)(dc >> 4);
                }
                else v = 0x80;
                dst16x16.Fill(v);
                break;
            }
            case V_PRED:
                for (int r = 0; r < 16; r++)
                    plane.AsSpan(off - stride, 16).CopyTo(dst16x16.Slice(r * 16, 16));
                break;
            case H_PRED:
                for (int r = 0; r < 16; r++)
                    dst16x16.Slice(r * 16, 16).Fill(plane[off + r * stride - 1]);
                break;
            case TM_PRED:
            {
                int tl = plane[off - stride - 1];
                for (int y = 0; y < 16; y++)
                {
                    int left = plane[off + y * stride - 1];
                    for (int x = 0; x < 16; x++)
                        dst16x16[y * 16 + x] = Clip8(plane[off - stride + x] + left - tl);
                }
                break;
            }
        }
    }

    private static void PredictChroma8(byte[] plane, int off, int stride, byte mode, bool hasTop, bool hasLeft)
    {
        switch (mode)
        {
            case DC_PRED:
            {
                byte v;
                if (hasTop && hasLeft)
                {
                    int dc = 8;
                    for (int j = 0; j < 8; j++)
                        dc += plane[off - stride + j] + plane[off + j * stride - 1];
                    v = (byte)(dc >> 4);
                }
                else if (hasTop)
                {
                    int dc = 4;
                    for (int j = 0; j < 8; j++) dc += plane[off - stride + j];
                    v = (byte)(dc >> 3);
                }
                else if (hasLeft)
                {
                    int dc = 4;
                    for (int j = 0; j < 8; j++) dc += plane[off + j * stride - 1];
                    v = (byte)(dc >> 3);
                }
                else v = 0x80;

                for (int r = 0; r < 8; r++)
                    plane.AsSpan(off + r * stride, 8).Fill(v);
                break;
            }
            case V_PRED:
                for (int r = 0; r < 8; r++)
                    plane.AsSpan(off - stride, 8).CopyTo(plane.AsSpan(off + r * stride, 8));
                break;
            case H_PRED:
                for (int r = 0; r < 8; r++)
                {
                    int row = off + r * stride;
                    plane.AsSpan(row, 8).Fill(plane[row - 1]);
                }
                break;
            case TM_PRED:
                TrueMotion(plane, off, stride, 8);
                break;
        }
    }

    private static void PredictLuma4(byte[] p, int off, int s, byte mode)
    {
        switch (mode)
        {
            case B_DC_PRED:
            {
                int dc = 4;
                for (int j = 0; j < 4; j++)
                    dc += p[off - s + j] + p[off + j * s - 1];
                byte v = (byte)(dc >> 3);
                for (int r = 0; r < 4; r++)
                    p.AsSpan(off + r * s, 4).Fill(v);
                break;
            }
            case B_TM_PRED:
                TrueMotion(p, off, s, 4);
                break;
            case B_VE_PRED:
            {
                int tl = p[off - s - 1], t0 = p[off - s], t1 = p[off - s + 1], t2 = p[off - s + 2], t3 = p[off - s + 3], t4 = p[off - s + 4];
                byte v0 = Avg3(tl, t0, t1), v1 = Avg3(t0, t1, t2), v2 = Avg3(t1, t2, t3), v3 = Avg3(t2, t3, t4);
                for (int r = 0; r < 4; r++)
                {
                    int b = off + r * s;
                    p[b] = v0; p[b + 1] = v1; p[b + 2] = v2; p[b + 3] = v3;
                }
                break;
            }
            case B_HE_PRED:
            {
                int a = p[off - s - 1], b = p[off - 1], c = p[off + s - 1], d = p[off + 2 * s - 1], e = p[off + 3 * s - 1];
                p.AsSpan(off, 4).Fill(Avg3(a, b, c));
                p.AsSpan(off + s, 4).Fill(Avg3(b, c, d));
                p.AsSpan(off + 2 * s, 4).Fill(Avg3(c, d, e));
                p.AsSpan(off + 3 * s, 4).Fill(Avg3(d, e, e));
                break;
            }
            case B_RD_PRED:
            {
                int i = p[off - 1], j = p[off + s - 1], k = p[off + 2 * s - 1], l = p[off + 3 * s - 1];
                int x = p[off - s - 1], a = p[off - s], b = p[off - s + 1], c = p[off - s + 2], d = p[off - s + 3];
                p[off + 3 * s] = Avg3(j, k, l);
                p[off + 3 * s + 1] = p[off + 2 * s] = Avg3(i, j, k);
                p[off + 3 * s + 2] = p[off + 2 * s + 1] = p[off + s] = Avg3(x, i, j);
                p[off + 3 * s + 3] = p[off + 2 * s + 2] = p[off + s + 1] = p[off] = Avg3(a, x, i);
                p[off + 2 * s + 3] = p[off + s + 2] = p[off + 1] = Avg3(b, a, x);
                p[off + s + 3] = p[off + 2] = Avg3(c, b, a);
                p[off + 3] = Avg3(d, c, b);
                break;
            }
            case B_VR_PRED:
            {
                int i = p[off - 1], j = p[off + s - 1], k = p[off + 2 * s - 1];
                int x = p[off - s - 1], a = p[off - s], b = p[off - s + 1], c = p[off - s + 2], d = p[off - s + 3];
                p[off] = p[off + 2 * s + 1] = Avg2(x, a);
                p[off + 1] = p[off + 2 * s + 2] = Avg2(a, b);
                p[off + 2] = p[off + 2 * s + 3] = Avg2(b, c);
                p[off + 3] = Avg2(c, d);
                p[off + 3 * s] = Avg3(k, j, i);
                p[off + 2 * s] = Avg3(j, i, x);
                p[off + s] = p[off + 3 * s + 1] = Avg3(i, x, a);
                p[off + s + 1] = p[off + 3 * s + 2] = Avg3(x, a, b);
                p[off + s + 2] = p[off + 3 * s + 3] = Avg3(a, b, c);
                p[off + s + 3] = Avg3(b, c, d);
                break;
            }
            case B_LD_PRED:
            {
                int a = p[off - s], b = p[off - s + 1], c = p[off - s + 2], d = p[off - s + 3];
                int e = p[off - s + 4], f = p[off - s + 5], g = p[off - s + 6], h = p[off - s + 7];
                p[off] = Avg3(a, b, c);
                p[off + 1] = p[off + s] = Avg3(b, c, d);
                p[off + 2] = p[off + s + 1] = p[off + 2 * s] = Avg3(c, d, e);
                p[off + 3] = p[off + s + 2] = p[off + 2 * s + 1] = p[off + 3 * s] = Avg3(d, e, f);
                p[off + s + 3] = p[off + 2 * s + 2] = p[off + 3 * s + 1] = Avg3(e, f, g);
                p[off + 2 * s + 3] = p[off + 3 * s + 2] = Avg3(f, g, h);
                p[off + 3 * s + 3] = Avg3(g, h, h);
                break;
            }
            case B_VL_PRED:
            {
                int a = p[off - s], b = p[off - s + 1], c = p[off - s + 2], d = p[off - s + 3];
                int e = p[off - s + 4], f = p[off - s + 5], g = p[off - s + 6], h = p[off - s + 7];
                p[off] = Avg2(a, b);
                p[off + 1] = p[off + 2 * s] = Avg2(b, c);
                p[off + 2] = p[off + 2 * s + 1] = Avg2(c, d);
                p[off + 3] = p[off + 2 * s + 2] = Avg2(d, e);
                p[off + s] = Avg3(a, b, c);
                p[off + s + 1] = p[off + 3 * s] = Avg3(b, c, d);
                p[off + s + 2] = p[off + 3 * s + 1] = Avg3(c, d, e);
                p[off + s + 3] = p[off + 2 * s + 3] = Avg3(d, e, f);
                p[off + 3 * s + 2] = Avg3(e, f, g);
                p[off + 3 * s + 3] = Avg3(f, g, h);
                break;
            }
            case B_HD_PRED:
            {
                int i = p[off - 1], j = p[off + s - 1], k = p[off + 2 * s - 1], l = p[off + 3 * s - 1];
                int x = p[off - s - 1], a = p[off - s], b = p[off - s + 1], c = p[off - s + 2];
                p[off] = p[off + s + 2] = Avg2(i, x);
                p[off + s] = p[off + 2 * s + 2] = Avg2(j, i);
                p[off + 2 * s] = p[off + 3 * s + 2] = Avg2(k, j);
                p[off + 3 * s] = Avg2(l, k);
                p[off + 3] = Avg3(a, b, c);
                p[off + 2] = Avg3(x, a, b);
                p[off + 1] = p[off + s + 3] = Avg3(i, x, a);
                p[off + s + 1] = p[off + 2 * s + 3] = Avg3(j, i, x);
                p[off + 2 * s + 1] = p[off + 3 * s + 3] = Avg3(k, j, i);
                p[off + 3 * s + 1] = Avg3(l, k, j);
                break;
            }
            case B_HU_PRED:
            {
                int i = p[off - 1], j = p[off + s - 1], k = p[off + 2 * s - 1], l = p[off + 3 * s - 1];
                p[off] = Avg2(i, j);
                p[off + 2] = p[off + s] = Avg2(j, k);
                p[off + s + 2] = p[off + 2 * s] = Avg2(k, l);
                p[off + 1] = Avg3(i, j, k);
                p[off + 3] = p[off + s + 1] = Avg3(j, k, l);
                p[off + s + 3] = p[off + 2 * s + 1] = Avg3(k, l, l);
                byte lv = (byte)l;
                p[off + 2 * s + 2] = p[off + 2 * s + 3] = lv;
                p[off + 3 * s] = p[off + 3 * s + 1] = p[off + 3 * s + 2] = p[off + 3 * s + 3] = lv;
                break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // In-Loop Deblocking Filter
    // -------------------------------------------------------------------------

    private static FInfo MakeFInfo(int rawLevel, int sharpness, bool i4x4)
    {
        int level = Math.Clamp(rawLevel, 0, 63);
        if (level == 0)
            return default;
        int inner = level;
        if (sharpness > 0)
        {
            inner >>= sharpness > 4 ? 2 : 1;
            int cap = 9 - sharpness;
            if (inner > cap)
                inner = cap;
        }
        if (inner < 1)
            inner = 1;
        return new FInfo
        {
            FLimit = 2 * level + inner,
            FILevel = inner,
            FInner = i4x4,
            HevThresh = level >= 40 ? 2 : (level >= 15 ? 1 : 0)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SClip1(int v) => Math.Clamp(v, -128, 127);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SClip2(int v) => Math.Clamp(v, -16, 15);

    private static void SimpleFilterEdge(byte[] p, int pos, int step, int thresh2)
    {
        int p1 = p[pos - 2 * step], p0 = p[pos - step], q0 = p[pos], q1 = p[pos + step];
        if (4 * Math.Abs(p0 - q0) + Math.Abs(p1 - q1) <= thresh2)
        {
            int a = 3 * (q0 - p0) + SClip1(p1 - q1);
            int a1 = SClip2((a + 4) >> 3);
            int a2 = SClip2((a + 3) >> 3);
            p[pos - step] = Clip8(p0 + a2);
            p[pos] = Clip8(q0 - a1);
        }
    }

    private static void FilterMbSimple(byte[] y, int off, int stride, bool doLeft, bool doTop, FInfo info)
    {
        int lim = info.FLimit;
        if (doLeft)
        {
            int t2 = 2 * (lim + 4) + 1;
            for (int i = 0; i < 16; i++) SimpleFilterEdge(y, off + i * stride, 1, t2);
        }
        if (info.FInner)
        {
            int t2 = 2 * lim + 1;
            for (int c = 4; c <= 12; c += 4)
                for (int i = 0; i < 16; i++) SimpleFilterEdge(y, off + c + i * stride, 1, t2);
        }
        if (doTop)
        {
            int t2 = 2 * (lim + 4) + 1;
            for (int i = 0; i < 16; i++) SimpleFilterEdge(y, off + i, stride, t2);
        }
        if (info.FInner)
        {
            int t2 = 2 * lim + 1;
            for (int r = 4; r <= 12; r += 4)
                for (int i = 0; i < 16; i++) SimpleFilterEdge(y, off + r * stride + i, stride, t2);
        }
    }

    private static void FilterLoop(byte[] p, int off, int hstride, int vstride, int size, int thresh, int ithresh, int hevThresh, bool useFilter6)
    {
        int thresh2 = 2 * thresh + 1;
        int pos = off;
        for (int i = 0; i < size; i++, pos += vstride)
        {
            int p3 = p[pos - 4 * hstride], p2 = p[pos - 3 * hstride], p1 = p[pos - 2 * hstride], p0 = p[pos - hstride];
            int q0 = p[pos], q1 = p[pos + hstride], q2 = p[pos + 2 * hstride], q3 = p[pos + 3 * hstride];
            if (4 * Math.Abs(p0 - q0) + Math.Abs(p1 - q1) > thresh2)
                continue;
            if (Math.Abs(p3 - p2) > ithresh || Math.Abs(p2 - p1) > ithresh || Math.Abs(p1 - p0) > ithresh ||
                Math.Abs(q3 - q2) > ithresh || Math.Abs(q2 - q1) > ithresh || Math.Abs(q1 - q0) > ithresh)
                continue;

            bool isHev = Math.Abs(p1 - p0) > hevThresh || Math.Abs(q1 - q0) > hevThresh;
            if (isHev)
            {
                int a = 3 * (q0 - p0) + SClip1(p1 - q1);
                int a1 = SClip2((a + 4) >> 3);
                int a2 = SClip2((a + 3) >> 3);
                p[pos - hstride] = Clip8(p0 + a2);
                p[pos] = Clip8(q0 - a1);
            }
            else if (useFilter6)
            {
                int a = SClip1(3 * (q0 - p0) + SClip1(p1 - q1));
                int a1 = (27 * a + 63) >> 7;
                int a2 = (18 * a + 63) >> 7;
                int a3 = (9 * a + 63) >> 7;
                p[pos - 3 * hstride] = Clip8(p2 + a3);
                p[pos - 2 * hstride] = Clip8(p1 + a2);
                p[pos - hstride] = Clip8(p0 + a1);
                p[pos] = Clip8(q0 - a1);
                p[pos + hstride] = Clip8(q1 - a2);
                p[pos + 2 * hstride] = Clip8(q2 - a3);
            }
            else
            {
                int a = 3 * (q0 - p0);
                int a1 = SClip2((a + 4) >> 3);
                int a2 = SClip2((a + 3) >> 3);
                int a3 = (a1 + 1) >> 1;
                p[pos - 2 * hstride] = Clip8(p1 + a3);
                p[pos - hstride] = Clip8(p0 + a2);
                p[pos] = Clip8(q0 - a1);
                p[pos + hstride] = Clip8(q1 - a3);
            }
        }
    }

    private static void FilterMbNormal(byte[] y, int yOff, int yStride, byte[] u, byte[] v, int uvOff, int uvStride, bool doLeft, bool doTop, FInfo info)
    {
        int outer = info.FLimit + 4;
        int inner = info.FLimit;
        int il = info.FILevel;
        int hev = info.HevThresh;

        if (doLeft)
        {
            FilterLoop(y, yOff, 1, yStride, 16, outer, il, hev, true);
            FilterLoop(u, uvOff, 1, uvStride, 8, outer, il, hev, true);
            FilterLoop(v, uvOff, 1, uvStride, 8, outer, il, hev, true);
        }
        if (info.FInner)
        {
            for (int c = 4; c <= 12; c += 4)
                FilterLoop(y, yOff + c, 1, yStride, 16, inner, il, hev, false);
            FilterLoop(u, uvOff + 4, 1, uvStride, 8, inner, il, hev, false);
            FilterLoop(v, uvOff + 4, 1, uvStride, 8, inner, il, hev, false);
        }
        if (doTop)
        {
            FilterLoop(y, yOff, yStride, 1, 16, outer, il, hev, true);
            FilterLoop(u, uvOff, uvStride, 1, 8, outer, il, hev, true);
            FilterLoop(v, uvOff, uvStride, 1, 8, outer, il, hev, true);
        }
        if (info.FInner)
        {
            for (int r = 4; r <= 12; r += 4)
                FilterLoop(y, yOff + r * yStride, yStride, 1, 16, inner, il, hev, false);
            FilterLoop(u, uvOff + 4 * uvStride, uvStride, 1, 8, inner, il, hev, false);
            FilterLoop(v, uvOff + 4 * uvStride, uvStride, 1, 8, inner, il, hev, false);
        }
    }

    // -------------------------------------------------------------------------
    // Studio-swing YUV <-> RGBA
    // -------------------------------------------------------------------------

    private static void ConvertRgbaToYuv420(ReadOnlySpan<byte> rgba, int width, int height, int stride,
                                            byte[] dstY, int yStride, byte[] dstU, byte[] dstV, int uvStride)
    {
        const int YuvFix = 16;
        const int YuvHalf = 1 << (YuvFix - 1);
        const int UvRound = YuvHalf << 2;
        const int UvOffset = 128 << (YuvFix + 2);

        int yH = dstY.Length / yStride;
        int uvH = dstU.Length / uvStride;

        for (int py = 0; py < yH; py++)
        {
            int cy = Math.Min(py, height - 1);
            ReadOnlySpan<byte> row = rgba.Slice(cy * stride, width * 4);
            int yRow = py * yStride;
            for (int px = 0; px < yStride; px++)
            {
                int cx = Math.Min(px, width - 1) * 4;
                int r = row[cx], g = row[cx + 1], b = row[cx + 2];
                int luma = 16839 * r + 33059 * g + 6420 * b;
                dstY[yRow + px] = (byte)Math.Clamp((luma + YuvHalf + (16 << YuvFix)) >> YuvFix, 0, 255);
            }
        }

        for (int cy = 0; cy < uvH; cy++)
        {
            int y0 = Math.Min(2 * cy, height - 1);
            int y1 = Math.Min(2 * cy + 1, height - 1);
            ReadOnlySpan<byte> row0 = rgba.Slice(y0 * stride, width * 4);
            ReadOnlySpan<byte> row1 = rgba.Slice(y1 * stride, width * 4);
            int uvRow = cy * uvStride;
            for (int cx = 0; cx < uvStride; cx++)
            {
                int x0 = Math.Min(2 * cx, width - 1) * 4;
                int x1 = Math.Min(2 * cx + 1, width - 1) * 4;
                int sr = row0[x0] + row0[x1] + row1[x0] + row1[x1];
                int sg = row0[x0 + 1] + row0[x1 + 1] + row1[x0 + 1] + row1[x1 + 1];
                int sb = row0[x0 + 2] + row0[x1 + 2] + row1[x0 + 2] + row1[x1 + 2];
                int uAcc = -9719 * sr - 19081 * sg + 28800 * sb;
                int vAcc = 28800 * sr - 24116 * sg - 4684 * sb;
                dstU[uvRow + cx] = (byte)Math.Clamp((uAcc + UvRound + UvOffset) >> (YuvFix + 2), 0, 255);
                dstV[uvRow + cx] = (byte)Math.Clamp((vAcc + UvRound + UvOffset) >> (YuvFix + 2), 0, 255);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MultHi(int v, int coeff) => (v * coeff) >> 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Vp8Clip8(int v) => (byte)(Math.Clamp(v, 0, (256 << 6) - 1) >> 6);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitYuvPixel(byte yVal, uint packedUv, Span<byte> dstRow, int col)
    {
        int y = yVal;
        int u = (int)(packedUv & 0xffU);
        int v = (int)((packedUv >> 16) & 0xffU);
        int o = col * 4;
        dstRow[o] = Vp8Clip8(MultHi(y, 19077) + MultHi(v, 26149) - 14234);
        dstRow[o + 1] = Vp8Clip8(MultHi(y, 19077) - MultHi(u, 6419) - MultHi(v, 13320) + 8708);
        dstRow[o + 2] = Vp8Clip8(MultHi(y, 19077) + MultHi(u, 33050) - 17685);
        dstRow[o + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint LoadUv(byte u, byte v) => u | ((uint)v << 16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MidpointUv(uint a, uint b) => (a & b) + (((a ^ b) & 0xfefefefeU) >> 1);

    private static byte[] UpsampleYuv420ToRgba(byte[] yPlane, int yStride, byte[] uPlane, byte[] vPlane, int uvStride, int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int chromaRows = (height + 1) >> 1;
        int lastPair = (width - 1) >> 1;

        for (int outY = 0; outY < height; outY++)
        {
            int nearRow, farRow;
            if (outY == 0)
            {
                nearRow = farRow = 0;
            }
            else if ((outY & 1) == 1)
            {
                nearRow = (outY - 1) >> 1;
                int f = (outY + 1) >> 1;
                farRow = f < chromaRows ? f : nearRow;
            }
            else
            {
                nearRow = outY >> 1;
                farRow = nearRow - 1;
            }

            // Account for 1-pixel top and left borders in padded planes
            int yBase = (outY + 1) * yStride + 1;
            int nearOff = (nearRow + 1) * uvStride + 1;
            int farOff = (farRow + 1) * uvStride + 1;
            Span<byte> dstRow = rgba.AsSpan(outY * width * 4, width * 4);

            uint nearL = LoadUv(uPlane[nearOff], vPlane[nearOff]);
            uint farL = LoadUv(uPlane[farOff], vPlane[farOff]);
            uint first = (3u * nearL + farL + 0x00020002U) >> 2;
            EmitYuvPixel(yPlane[yBase], first, dstRow, 0);

            for (int x = 1; x <= lastPair; x++)
            {
                uint nearR = LoadUv(uPlane[nearOff + x], vPlane[nearOff + x]);
                uint farR = LoadUv(uPlane[farOff + x], vPlane[farOff + x]);
                uint dL = (nearL + 3u * nearR + 3u * farL + farR + 0x00080008U) >> 3;
                uint dR = (3u * nearL + nearR + farL + 3u * farR + 0x00080008U) >> 3;
                EmitYuvPixel(yPlane[yBase + 2 * x - 1], MidpointUv(dL, nearL), dstRow, 2 * x - 1);
                EmitYuvPixel(yPlane[yBase + 2 * x], MidpointUv(dR, nearR), dstRow, 2 * x);
                nearL = nearR;
                farL = farR;
            }

            if ((width & 1) == 0)
            {
                uint last = (3u * nearL + farL + 0x00020002U) >> 2;
                EmitYuvPixel(yPlane[yBase + width - 1], last, dstRow, width - 1);
            }
        }
        return rgba;
    }
}
