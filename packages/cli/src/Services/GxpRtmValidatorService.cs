using Cadence.Compliance;

namespace Cadence.Cli.Services;

public static class GxpRtmValidatorService
{
    /// <summary>
    /// The <c>rtm</c> gate: unknown requirement IDs and broken integrity stamps fail it. The committed snapshot's test
    /// mapping must be current only while taking a snapshot (ADR-2532); on a feature branch it is the baseline's.
    /// </summary>
    public static bool ValidateGxpRtm()
    {
        return RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, requireCurrentSnapshot: GxpSnapshotPolicy.IsSnapshotMode()) == 0;
    }
}
