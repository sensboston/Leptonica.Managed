#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
using System.Collections;
using System.Diagnostics;

namespace BitMiracle.LibTiff.Classic.Internal
{
    internal class TagCompare : IComparer
    {
        int IComparer.Compare(object x, object y)
        {
            TiffFieldInfo ta = x as TiffFieldInfo;
            TiffFieldInfo tb = y as TiffFieldInfo;

            Debug.Assert(ta != null);
            Debug.Assert(tb != null);

            if (ta.Tag != tb.Tag)
                return ((int)ta.Tag - (int)tb.Tag);

            return (ta.Type == TiffType.ANY) ? 0 : ((int)tb.Type - (int)ta.Type);
        }
    }
}
