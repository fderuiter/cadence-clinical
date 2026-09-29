using System;

namespace Cadence.Kernel.Domain.Bitemporal;

/// <summary>
/// Defines an entity possessing dual-axis clinical valid time and transaction recorded time intervals under 21 CFR Part 11.
/// Conforms to ADR-2246 and PRD-SYS-053.
/// </summary>
public interface IBitemporalEntity
{
    TemporalPeriod Period { get; }
}
