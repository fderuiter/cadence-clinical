using Cadence.Compliance;

namespace Cadence.Cli.Services;

public static class GxpRtmValidatorService
{
    /// <summary>
    /// The <c>rtm</c> gate: unknown or misapplied requirement IDs, broken integrity stamps, a committed draft or
    /// unrecorded snapshot, and excessive snapshot commit lag on <c>refs/heads/main</c> fail it. The committed
    /// snapshot's test mapping must be current only while taking a snapshot (ADR-2532); on a feature branch it is the
    /// baseline's.
    /// </summary>
    public static bool ValidateGxpRtm(
        string? repoRoot = null,
        string? eventName = null,
        string? gitRef = null,
        int maxMainSnapshotCommitLag = RtmGeneratorService.DefaultMaxMainSnapshotCommitLag)
    {
        return RtmGeneratorService.GenerateRtm(
            draft: true,
            validateOnly: true,
            repoRoot: repoRoot,
            requireCurrentSnapshot: GxpSnapshotPolicy.IsSnapshotMode(),
            requireRecordedEvidence: true,
            eventName: eventName,
            gitRef: gitRef,
            maxMainSnapshotCommitLag: maxMainSnapshotCommitLag) == 0;
    }
}
