using System.CommandLine;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;
using Cadence.Topology;

using static Cadence.Cli.Commands.CommandRegistrationHelpers;

namespace Cadence.Cli.Commands;

/// <summary>Registers GxP traceability, compliance utility, port diagnostics, and gating pipeline commands.</summary>
internal static class ComplianceAndGxpCommands
{
    public static void Register(RootCommand root)
    {
        AddCommand(root, "generate-rtm", "Regenerates the Requirements Traceability Matrix", ToolCommands.ConfigureGenerateRtm);
        AddCommand(root, "compliance-utility", "Sanitizes document paths and requirement prefixes", ToolCommands.ConfigureComplianceUtility);
        AddCommand(root, "check-ports", "Reports which local service ports are in use", () => PortCheckerService.CheckPorts());
        AddCommand(root, "run-gating-pipeline", "Runs the full gating pipeline", () => GatingPipelineService.RunGatingPipeline());

        var gxp = AddBranch(root, "gxp", "GxP requirement traceability");
        AddCommand(gxp, "sync", "Runs the tests and validates requirement tags and results on a branch; writes a preview, never docs/SDLC", GxPSyncCommand.Configure);
        AddCommand(gxp, "snapshot", "Integration owner only: runs the tests and writes and stages the RTM and IQ/OQ/PQ snapshot", GxPSnapshotCommand.Configure);
        AddCommand(gxp, "validate", "Validates requirement tags and integrity stamps without running tests", GxPComplianceService.ValidateCompliance);
        AddCommand(gxp, "diff", "Compares the regenerated RTM and IQ/OQ/PQ report with the committed copies, ignoring the evidence block",
            () => GxpDocsDiffService.Compare(RepositoryRoot.Find()));
    }
}
