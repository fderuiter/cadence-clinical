using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Compliance;

/// <summary>
/// Who may change the generated GxP snapshots (ADR-2532, #5151). The RTM and the IQ/OQ/PQ report are taken from the
/// integrated candidate by the integration owner with <c>gxp snapshot</c>; a feature branch validates its tags and
/// results without writing or committing them, so parallel branches never conflict on the two documents.
/// </summary>
public static class GxpSnapshotPolicy
{
    /// <summary>Set to <c>1</c> while taking and committing a snapshot; the snapshot guard then admits the documents.</summary>
    public const string SnapshotEnvironmentVariable = "CADENCE_GXP_SNAPSHOT";

    /// <summary>Git commit trailer key required on snapshot commits so CI can verify authorized snapshot provenance.</summary>
    public const string SnapshotTrailerKey = "Gxp-Snapshot";

    /// <summary>Git commit trailer value written by <c>cadence gxp snapshot --commit</c>.</summary>
    public const string SnapshotTrailerValue = "refreshed";

    /// <summary>Canonical Git commit trailer required for CI snapshot authorization.</summary>
    public const string SnapshotTrailer = "Gxp-Snapshot: refreshed";

    /// <summary>Default commit subject written by <c>cadence gxp snapshot --commit</c>.</summary>
    public const string SnapshotCommitSubject = "docs(rtm): take the GxP snapshot from the integrated candidate";

    /// <summary>The generated documents, relative to the repository root.</summary>
    public static readonly IReadOnlyList<string> Documents =
    [
        "docs/SDLC/Requirements_Traceability_Matrix.md",
        "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
    ];

    /// <summary>The archived run reports a snapshot also writes, as a directory prefix.</summary>
    public const string RunReportsPrefix = "docs/SDLC/runs/";

    /// <summary>Where <c>gxp sync</c> and <c>cadence ci</c> write their preview of the documents on a feature branch (gitignored).</summary>
    public const string PreviewDir = "TestResults/gxp/preview";

    /// <summary>True when <see cref="SnapshotEnvironmentVariable"/> is <c>1</c> or <c>true</c>.</summary>
    public static bool IsSnapshotMode(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        string? value = environment(SnapshotEnvironmentVariable);
        return value is not null &&
            (value.Trim() == "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The paths among <paramref name="changedPaths"/> that only a snapshot may change, in the order given.</summary>
    public static IReadOnlyList<string> SnapshotPaths(IEnumerable<string> changedPaths) =>
        changedPaths
            .Select(p => p.Replace('\\', '/').Trim())
            .Where(p => Documents.Contains(p, StringComparer.Ordinal) || p.StartsWith(RunReportsPrefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
