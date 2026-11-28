namespace Cadence.Compliance;

public class RequirementInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ParentPrdId { get; set; }
    public bool IsLegacyAlias { get; set; }

    /// <summary>
    /// The requirement's behaviour lives in the database (row-level security, triggers, constraints, transactions), so
    /// only a PostgreSQL test can prove it. Declared in the PRD with an <c>**Evidence:** PostgreSQL</c> line under
    /// the requirement's heading (#5324).
    /// </summary>
    public bool RequiresDatabase { get; set; }
}

public class TestMappingInfo
{
    public string File { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Line { get; set; }
}

public class TestResultInfo
{
    public string Status { get; set; } = TestStatus.NotExecuted;
    public string Message { get; set; } = string.Empty;
    public string Time { get; set; } = "0.0";
}

/// <summary>Outcome of a test as recorded in the TRX results.</summary>
public static class TestStatus
{
    public const string Passed = "PASSED";
    public const string Failed = "FAILED";
    public const string Skipped = "SKIPPED";

    /// <summary>
    /// The test exists in source but did not execute: it has no result in any TRX file, or its result is neither a
    /// pass, a failure nor a skip (for example inconclusive or pending).
    /// </summary>
    public const string NotExecuted = "NOT EXECUTED";

    /// <summary>Draft mode: no TRX results were supplied at all.</summary>
    public const string Unverified = "UNVERIFIED";
}

/// <summary>Verification state of one requirement.</summary>
public enum RequirementState
{
    /// <summary>Every mapped test passed. Each mapped test is mandatory.</summary>
    Qualified,

    /// <summary>At least one mapped test failed.</summary>
    Failed,

    /// <summary>
    /// No mapped test failed, but at least one did not pass: it was skipped, did not execute, or has no result
    /// (draft mode). Passing tests next to it do not make up for it.
    /// </summary>
    NotVerified,

    /// <summary>
    /// Every mapped test passed, but the requirement needs PostgreSQL evidence (<see cref="RequirementInfo.RequiresDatabase"/>)
    /// and none of its tests is a database test: in-memory or SQLite doubles can't exercise it.
    /// </summary>
    NeedsDatabaseEvidence,

    /// <summary>No test declares this requirement.</summary>
    Planned,
}
