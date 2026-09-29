using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Ddf;

/// <summary>
/// Port interface for evaluating XML/XForms layout and WCAG accessibility compliance.
/// </summary>
public interface IAccessibilityValidatorPort
{
    /// <summary>
    /// Evaluates layout accessibility on the given OpenRosa XML string.
    /// </summary>
    Task<AccessibilityReport> ValidateAsync(string openRosaXml, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default in-memory fake validator for standard protocol builds.
/// </summary>
public class DefaultAccessibilityValidator : IAccessibilityValidatorPort
{
    public Task<AccessibilityReport> ValidateAsync(string openRosaXml, CancellationToken cancellationToken = default)
    {
        var report = new AccessibilityReport(
            Status: "PASS",
            FormHash: string.Empty,
            Violations: [],
            Passes: [],
            Incomplete: [],
            Inapplicable: [],
            LayoutErrors: []
        );
        return Task.FromResult(report);
    }
}
