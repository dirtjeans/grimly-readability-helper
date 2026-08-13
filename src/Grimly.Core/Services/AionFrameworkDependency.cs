using System.Runtime.InteropServices;

namespace Grimly.Services;

/// <summary>
/// Adds a process-scoped dynamic dependency on the installed Aion Instruct
/// Preview framework package, putting the framework directory on the DLL
/// search path so this unpackaged app can activate AionInstructPreview.Text
/// and load its native binaries.
///
/// Adapted from Microsoft's unpackaged-WPF sample:
/// https://github.com/microsoft/Aion-Instruct-Preview-Sample (MIT).
/// Must run before the first WinRT activation of any AionInstructPreview
/// type — WindowsAiClient calls it inside its availability probe, which
/// happens before any model call by construction.
/// </summary>
internal static class AionFrameworkDependency
{
    // PackageFamilyName of the installed framework MSIX
    // (Microsoft.AionInstructPreview.Framework.1.0). Verify with:
    //   Get-AppxPackage *AionInstructPreview* | Select PackageFamilyName
    private const string FamilyName =
        "Microsoft.AionInstructPreview.Framework.1.0_8wekyb3d8bbwe";

    // PackageDependencyProcessorArchitectures (appmodel.h)
    private const int ArchArm64 = 0x10;
    private const int ArchX64 = 0x4;

    // PackageDependencyLifetimeKind — Process: released at process exit.
    private const int LifetimeKindProcess = 0;

    [DllImport("kernelbase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int TryCreatePackageDependency(
        IntPtr user,
        string packageFamilyName,
        ulong minVersion,
        int processorArchitectures,
        int lifetimeKind,
        string? lifetimeArtifact,
        int options,
        out IntPtr packageDependencyId);

    [DllImport("kernelbase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AddPackageDependency(
        IntPtr packageDependencyId,
        int rank,
        int options,
        out IntPtr packageDependencyContext,
        out IntPtr packageFullName);

    private static bool _loaded;

    /// <summary>
    /// Take the framework dependency. Throws when the framework package
    /// isn't installed on this machine — callers treat that as
    /// "Windows AI unavailable" rather than an error. Idempotent.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;

        int arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? ArchArm64
            : ArchX64;

        // minVersion 0 = any installed version of this family.
        int hr = TryCreatePackageDependency(
            IntPtr.Zero,
            FamilyName,
            minVersion: 0UL,
            arch,
            LifetimeKindProcess,
            lifetimeArtifact: null,
            options: 0,
            out IntPtr depId);
        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"TryCreatePackageDependency({FamilyName}) failed (HRESULT 0x{hr:X8}). " +
                "Is the Aion Instruct Preview framework package installed?",
                Marshal.GetExceptionForHR(hr));
        }

        hr = AddPackageDependency(depId, rank: 0, options: 0, out _, out _);
        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"AddPackageDependency failed (HRESULT 0x{hr:X8}).",
                Marshal.GetExceptionForHR(hr));
        }

        // depId is HeapAlloc'd; intentionally leaked — the Process-lifetime
        // dependency is released automatically at process exit.
        _loaded = true;
    }
}
