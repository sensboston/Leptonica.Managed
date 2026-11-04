#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
using System;

namespace BitMiracle.LibJpeg.Classic.Internal
{
    class arith_entropy_decoder : jpeg_entropy_decoder
    {
        public arith_entropy_decoder(jpeg_decompress_struct cinfo)
        {
            cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
        }

        public override void start_pass()
        {
            throw new NotImplementedException();
        }
    }
}
