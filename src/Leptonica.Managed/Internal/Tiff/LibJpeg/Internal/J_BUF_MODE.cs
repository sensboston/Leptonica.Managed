#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibJpeg.Classic.Internal
{
    /// <summary>
    /// Operating modes for buffer controllers
    /// </summary>
    enum J_BUF_MODE
    {
        JBUF_PASS_THRU,         /* Plain stripwise operation */

        /* Remaining modes require a full-image buffer to have been created */

        JBUF_SAVE_SOURCE,       /* Run source subobject only, save output */
        JBUF_CRANK_DEST,        /* Run dest subobject only, using saved data */
        JBUF_SAVE_AND_PASS      /* Run both subobjects, save output */
    }
}
