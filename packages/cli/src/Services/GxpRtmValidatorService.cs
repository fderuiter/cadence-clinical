using Cadence.Compliance;

namespace Cadence.Cli.Services;

public static class GxpRtmValidatorService
{
    /// <summary>
    /// The <c>rtm</c> gate: unknown requirement IDs, broken integrity stamps and a committed draft or unrecorded
    /// snapshot fail it. The committed snapshot's test
    /// mapping must be current only while taking a snapshot (ADR-2532); on a feature branch it is the baseline's.
    /// </summary>
    public static bool ValidateGxpRtm()
    {
        return RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, requireCurrentSnapshot: GxpSnapshotPolicy.IsSnapshotMode(),
            requireRecordedEvidence: true) == 0;
    }
}
