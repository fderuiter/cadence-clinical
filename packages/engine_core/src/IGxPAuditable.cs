using System;

namespace Cadence.Core.Domain;

/// <summary>
/// Defines the regulatory 21 CFR Part 11 audit trail contract required for all persistent clinical domain entities.
/// @req:PRD-SYS-053
/// </summary>
public interface IGxPAuditable
{
    DateTime CreatedAt { get; }
    string CreatedBy { get; }
    string ReasonForChange { get; }
    long VersionIndex { get; }
}

