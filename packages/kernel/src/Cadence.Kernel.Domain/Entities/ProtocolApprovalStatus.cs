namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Formal approval states for clinical protocol versions under 21 CFR Part 11 and ICH GCP E6(R2).
/// Conforms to ADR-2245 and PRD-SYS-053.
/// </summary>
public enum ProtocolApprovalStatus
{
    Draft = 1,
    InReview = 2,
    Approved = 3,
    Superseded = 4
}
