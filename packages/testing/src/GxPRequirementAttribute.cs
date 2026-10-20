using System;

namespace Cadence.Testing;

/// <summary>
/// Decorates test methods with 21 CFR Part 11 / GxP regulatory requirement traceability tags.
/// Parsed by the Cadence GxP sync pipeline to construct the Requirements Traceability Matrix (RTM).
/// It applies to test methods only: a tag on a class would claim every test in it, so the compiler refuses it here
/// and the <c>rtm</c> gate refuses an <c>@req:</c> line on a type (docs/TESTING.md).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class GxPRequirementAttribute(string requirementId) : Attribute
{
    public string RequirementId { get; } = requirementId;
}

