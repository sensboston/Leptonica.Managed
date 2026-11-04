using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/* LeptDebugOK is global and RegTest turns it on: test classes that depend on
 * its value run alone in this collection and set it with LeptDebugScope. */
[CollectionDefinition("LeptDebugOK", DisableParallelization = true)]
public class LeptDebugOKCollection
{
}

/// <summary>Sets LeptDebugOK for the lifetime of the scope and restores the previous value.</summary>
internal sealed class LeptDebugScope : IDisposable
{
    private readonly bool saved = WriteFile.LeptDebugOK;

    public LeptDebugScope(bool on) { WriteFile.SetLeptDebugOK(on); }

    public void Dispose() => WriteFile.SetLeptDebugOK(saved);
}
