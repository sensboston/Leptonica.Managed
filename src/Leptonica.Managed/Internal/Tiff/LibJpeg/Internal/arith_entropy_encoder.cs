#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
using System;

namespace BitMiracle.LibJpeg.Classic.Internal
{
    class arith_entropy_encoder : jpeg_entropy_encoder
    {
        public arith_entropy_encoder(jpeg_compress_struct cinfo)
        {
            cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
        }

        public override void start_pass(bool gather_statistics)
        {
            throw new NotImplementedException();
        }
    }
}
