using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Ddf;

/// <summary>
/// Checks a rendered OpenRosa XForm for accessibility and layout problems before the compiler hands it out. The
/// compiler runs one on every form unless <see cref="CompileOptions.ValidateAccessibility"/> is turned off, and
/// refuses the form when the report does not pass.
/// </summary>
public interface IAccessibilityValidator
{
    /// <summary>Evaluates the form and reports every violation and layout error it finds.</summary>
    AccessibilityReport Validate(string openRosaXml);
}

/// <summary>The outcome values of <see cref="AccessibilityReport.Status"/>.</summary>
public static class AccessibilityStatus
{
    /// <summary>No violation and no layout error.</summary>
    public const string Pass = "PASS";

    /// <summary>At least one violation or layout error.</summary>
    public const string Fail = "FAIL";

    /// <summary>
    /// Whether a report lets the form through: its status is <see cref="Pass"/> and it lists no violation and no
    /// layout error. A report that says PASS while listing a problem does not pass.
    /// </summary>
    public static bool Passes(AccessibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return string.Equals(report.Status, Pass, StringComparison.Ordinal)
            && (report.Violations is null || report.Violations.Count == 0)
            && (report.LayoutErrors is null || report.LayoutErrors.Count == 0);
    }
}

/// <summary>
/// Thrown when a compiled form fails its accessibility check. The compiler never returns a form that failed.
/// </summary>
public sealed class AccessibilityValidationException : Exception
{
    /// <summary>Creates the exception from the failing report.</summary>
    public AccessibilityValidationException(AccessibilityReport report)
        : base(Describe(report))
    {
        Report = report;
    }

    /// <summary>The report that failed.</summary>
    public AccessibilityReport Report { get; }

    private static string Describe(AccessibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var messages = new List<string>(report.LayoutErrors ?? []);
        foreach (var violation in report.Violations ?? [])
        {
            string targets = violation.TryGetValue("nodes", out object? nodes) && nodes is IEnumerable<IReadOnlyDictionary<string, object?>> list
                ? string.Join(", ", list.Select(n => n.TryGetValue("target", out object? t) ? t?.ToString() : null).Where(t => !string.IsNullOrEmpty(t)))
                : string.Empty;
            messages.Add($"Accessibility violation {violation.GetValueOrDefault("id")}: {violation.GetValueOrDefault("description")} ({targets})");
        }

        if (messages.Count == 0)
        {
            messages.Add($"The accessibility report has status {report.Status}.");
        }

        return "Layout/accessibility validation failed: " + string.Join("; ", messages);
    }
}
