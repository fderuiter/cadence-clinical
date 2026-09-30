using Cadence.Apps.Execution.Application.Abstractions;

namespace Cadence.UnitTests.Fixtures.Execution;

/// <summary>
/// Shared test double for <see cref="ICurrentAuditContextAccessor"/> providing configurable
/// <see cref="UserId"/> and <see cref="ChangeReason"/> properties with default fallback values.
/// </summary>
public sealed class FakeCurrentAuditContextAccessor : ICurrentAuditContextAccessor
{
    public FakeCurrentAuditContextAccessor(string userId = "test-user", string changeReason = "test-reason")
    {
        UserId = userId;
        ChangeReason = changeReason;
    }

    public string UserId { get; set; }

    public string ChangeReason { get; set; }
}
