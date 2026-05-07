// NEXIM — ILGPU accelerator lifecycle manager.
// Creates and owns the ILGPU Context and Accelerator for the lifetime of
// the GPU Monte Carlo solver.  Falls back gracefully to the ILGPU CPU
// accelerator when no GPU is available.
//
// Required NuGet: ILGPU 1.5.3 (included in NEXIM.Core.csproj)
//
// Reference:
//   ILGPU documentation — https://ilgpu.net/docs/

using ILGPU;
using ILGPU.Runtime;

namespace NEXIM.Core.Atmospheric.MonteCarlo.Gpu;

/// <summary>
/// Manages the lifecycle of an ILGPU <see cref="Context"/> and
/// <see cref="Accelerator"/>.
///
/// Usage:
/// <code>
/// using var ctx = IlgpuContext.Create(preferGpu: true);
/// var accel = ctx.Accelerator;
/// </code>
/// </summary>
public sealed class IlgpuContext : IDisposable
{
    private bool _disposed;

    /// <summary>The underlying ILGPU context.</summary>
    public Context Context { get; }

    /// <summary>
    /// The selected accelerator (CUDA/OpenCL if available, otherwise CPU).
    /// </summary>
    public Accelerator Accelerator { get; }

    /// <summary>
    /// <c>true</c> when a hardware GPU accelerator was selected;
    /// <c>false</c> when falling back to the ILGPU software CPU accelerator.
    /// </summary>
    public bool IsGpu { get; }

    private IlgpuContext(Context ctx, Accelerator accel, bool isGpu)
    {
        Context     = ctx;
        Accelerator = accel;
        IsGpu       = isGpu;
    }

    /// <summary>
    /// Create an <see cref="IlgpuContext"/>, preferring a hardware GPU when
    /// <paramref name="preferGpu"/> is <c>true</c>.
    /// </summary>
    public static IlgpuContext Create(bool preferGpu = true)
    {
        var ctx = Context.Create(builder => builder.Default());

        Accelerator? accel = null;
        bool         isGpu = false;

        if (preferGpu)
        {
            // Try every non-CPU device; take the first one that succeeds.
            foreach (var device in ctx)
            {
                if (device.AcceleratorType == AcceleratorType.CPU) continue;
                try
                {
                    accel = device.CreateAccelerator(ctx);
                    isGpu = true;
                    break;
                }
                catch
                {
                    // This device failed (driver not installed, etc.) — try next.
                }
            }
        }

        // CPU fallback — iterate context devices and pick the first CPU type
        if (accel == null)
        {
            foreach (var device in ctx)
            {
                if (device.AcceleratorType == AcceleratorType.CPU)
                {
                    accel = device.CreateAccelerator(ctx);
                    break;
                }
            }
            // Last resort: use GetPreferredDevice with preferCPU = true
            accel ??= ctx.GetPreferredDevice(preferCPU: true).CreateAccelerator(ctx);
            isGpu = false;
        }

        return new IlgpuContext(ctx, accel, isGpu);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Accelerator.Dispose();
        Context.Dispose();
    }
}
