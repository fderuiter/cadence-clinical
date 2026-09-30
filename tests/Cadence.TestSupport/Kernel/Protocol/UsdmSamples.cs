using System;
using System.IO;

namespace Cadence.TestSupport.Kernel.Protocol;

/// <summary>The USDM documents in <c>tests/fixtures/usdm</c> (see its README for their sources and licence).</summary>
internal static class UsdmSamples
{
    /// <summary>The file name, without extension, of the CDISC USDM 4.0.0 example study "full_1".</summary>
    public const string CdiscExample = "cdisc_usdm4_full_1";

    /// <summary>The distinct CDISC Biomedical Concept codes the CDISC example study uses.</summary>
    public static readonly string[] CdiscExampleConceptCodes = ["C69260", "C28421", "C17049", "C25208", "C25298", "C25299"];

    /// <summary>The CDISC example study's USDM JSON.</summary>
    public static string CdiscExampleJson() => File.ReadAllText(PathOf($"{CdiscExample}.json"));

    /// <summary>The full path of a file in <c>tests/fixtures/usdm</c>.</summary>
    public static string PathOf(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cadence.sln")))
        {
            dir = dir.Parent;
        }

        var root = dir?.FullName ?? throw new InvalidOperationException("Cadence.sln not found above the test output.");
        return Path.Combine(root, "tests", "fixtures", "usdm", fileName);
    }
}
