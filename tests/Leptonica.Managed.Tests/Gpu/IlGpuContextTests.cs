using ILGPU;
using Leptonica.Gpu;
using Xunit;

namespace Leptonica.Managed.Tests.Gpu;

/// <summary>
/// ILGPU 1.5.x ignores Context.Builder.DebugSymbols; the accelerator sets the mode through the
/// property setter instead. Basic is set here because without a debugger Auto already resolves to
/// Disabled, so only a mode other than the default shows that the setter really took effect.
/// </summary>
public sealed class IlGpuContextTests
{
    [Theory]
    [InlineData(DebugSymbolsMode.Basic)]
    [InlineData(DebugSymbolsMode.Disabled)]
    public void SetDebugSymbolsMode_IsApplied(DebugSymbolsMode mode)
    {
        using Context context = Context.Create(builder => {
            builder.Default();
            IlGpuAccelerator.SetDebugSymbolsMode(builder, mode);
        });
        Assert.Equal(mode, context.Properties.DebugSymbolsMode);
    }
}
