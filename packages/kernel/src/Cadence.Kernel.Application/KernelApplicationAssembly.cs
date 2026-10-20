namespace Cadence.Kernel.Application;

/// <summary>
/// Anchor for the kernel application layer. The command pipeline (#4779), authorization (#4781) and consent
/// evaluation land here (spec §5.1, §9.3, §9.4). Depends only on Kernel.Abstractions and Kernel.Domain.
/// </summary>
public static class KernelApplicationAssembly
{
    /// <summary>The assembly name, used by architecture tests.</summary>
    public const string Name = "Cadence.Kernel.Application";
}
