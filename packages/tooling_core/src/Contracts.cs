using System.Collections.Generic;

namespace Cadence.ToolingCore;

public record CommandEnvelope<T>(
    bool Success,
    int ExitCode = 0,
    IReadOnlyDictionary<string, object?>? Summary = null,
    string? Remediation = null,
    string? ZoomToken = null,
    T? Data = default
);

public record FastTestRequest(
    string? Subsystem = null,
    bool FailedFirst = false,
    bool Summary = true
);

public record FastTestResponse(
    int Passed,
    int Failed,
    double DurationSeconds,
    IReadOnlyList<string> Command,
    IReadOnlyList<string>? FailedTests = null
);

public record SentinelRunRequest(
    string? Gate = null,
    bool Parallel = true,
    bool Summary = true
);

public record SentinelRunResponse(
    int PassedCount,
    int FailedCount,
    int TotalCount,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Gates = null
);

public record ZoomInspectRequest(
    string ZoomToken,
    int Offset = 0,
    int Limit = 100
);

public record ZoomInspectResponse(
    string ZoomToken,
    string EntityType,
    int TotalLines,
    IReadOnlyList<string> Lines,
    bool HasMore = false
);

public record UsdmGraphInspectRequest(
    string? ProtocolId = null,
    string? EntityType = null,
    int Depth = 2,
    bool Summary = true
);

public record UsdmGraphInspectResponse(
    string ProtocolId,
    int NodeCount,
    int RelationshipCount,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Entities,
    int Depth,
    double DurationSeconds
);

public record ClinicalEventStreamRequest(
    string? EventType = null,
    int Limit = 20,
    bool Replay = false
);

public record ClinicalEventStreamResponse(
    int TotalEvents,
    int ReturnedEvents,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Events,
    int ReplayedCount,
    double DurationSeconds
);

public record SdtmPipelineValidateRequest(
    string? Domain = null,
    bool StrictCdisc = true,
    bool Summary = true
);

public record SdtmPipelineValidateResponse(
    IReadOnlyList<string> DomainsEvaluated,
    int ValidCount,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Violations,
    double DurationSeconds
);

public record SyntheticPatientGenerateRequest(
    string ProtocolId = "default",
    string Scenario = "standard",
    int Count = 1,
    bool DryRun = false
);

public record SyntheticPatientGenerateResponse(
    string ProtocolId,
    string Scenario,
    int GeneratedCount,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Subjects,
    bool IsSynthetic = true,
    double DurationSeconds = 0.0
);
