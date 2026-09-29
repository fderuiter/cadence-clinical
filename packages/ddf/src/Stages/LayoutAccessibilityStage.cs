using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Ddf.Stages;

public class LayoutAccessibilityStage
{
    private readonly IAccessibilityValidatorPort _validator;

    public LayoutAccessibilityStage(IAccessibilityValidatorPort validator)
    {
        _validator = validator ?? throw new ArgumentException(
            "Accessibility validator port is uninitialized or missing. " +
            "LayoutAccessibilityStage requires an explicit AccessibilityValidatorPort instance.");
    }

    public IAccessibilityValidatorPort Validator => _validator;

    public async Task<AccessibilityReport> ValidateAsync(
        string openRosaXml,
        string formHash,
        CancellationToken cancellationToken = default)
    {
        var report = await _validator.ValidateAsync(openRosaXml, cancellationToken);

        bool hasViolations = report.Violations != null && report.Violations.Count > 0;
        bool hasErrors = report.LayoutErrors != null && report.LayoutErrors.Count > 0;

        if (hasViolations || hasErrors)
        {
            var errMsgs = new List<string>();
            if (report.LayoutErrors != null)
            {
                foreach (var err in report.LayoutErrors)
                {
                    errMsgs.Add(err);
                }
            }

            if (report.Violations != null)
            {
                foreach (var v in report.Violations)
                {
                    string id = v.TryGetValue("id", out var idObj) ? idObj?.ToString() ?? "" : "";
                    string desc = v.TryGetValue("description", out var dObj) ? dObj?.ToString() ?? "" : "";
                    errMsgs.Add($"Accessibility Violation: {id} - {desc}");
                }
            }

            string errorText = string.Join("; ", errMsgs);
            throw new InvalidOperationException($"Layout/Accessibility Validation Failed: {errorText}");
        }

        if (string.IsNullOrEmpty(report.FormHash))
        {
            return report with { FormHash = formHash };
        }

        return report;
    }
}
