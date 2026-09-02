using System;

namespace Cadence.Testing;

/// <summary>
/// Decorates test methods with 21 CFR Part 11 / GxP regulatory requirement traceability tags.
/// Parsed by the Cadence GxP sync pipeline to construct the Requirements Traceability Matrix (RTM).
/// @req:PRD-SYS-053
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GxPRequirementAttribute(string requirementId) : Attribute
{
    public string RequirementId { get; } = requirementId;
}

