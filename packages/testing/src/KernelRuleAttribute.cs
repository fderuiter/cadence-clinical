using System;

namespace Cadence.Testing;

/// <summary>
/// Declares which Clinical Kernel rule (<c>K-AREA-N</c>, defined in bold in
/// <c>docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md</c>) a test method proves. Tag only a rule the test's assertions
/// would catch being broken. A test may prove several rules, so the attribute can repeat.
/// <para>
/// <c>KernelRuleCoverageTests</c> in <c>Cadence.ArchitectureTests</c> reads these tags from the test sources and fails
/// when a rule in the specification has no tagged test (outside its shrink-only baseline) or when a test names a rule
/// the specification does not define. It is the only form that counts: a K-ID in a method name, doc comment or
/// class comment is documentation.
/// </para>
/// </summary>
/// <param name="ruleId">The rule identifier, for example <c>K-FACT-1</c>.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class KernelRuleAttribute(string ruleId) : Attribute
{
    /// <summary>The rule identifier, for example <c>K-FACT-1</c>.</summary>
    public string RuleId { get; } = ruleId;
}
