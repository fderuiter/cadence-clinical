using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Cadence.Deid;

namespace Cadence.Compliance;

/// <summary>
/// Native service for identifying and redacting PHI/PII non-destructively.
/// Delegating to <see cref="DeidDetector"/> for PHI scrubbing.
/// Requirements: PRD-SYS-001
/// </summary>
public class PhiRedactorService
{
    private readonly DeidDetector _detector;

    public PhiRedactorService(DeidDetector? detector = null)
    {
        _detector = detector ?? new DeidDetector();
    }

    /// <summary>
    /// Redacts configured terms and auto-detected PHI from raw content bytes.
    /// </summary>
    public byte[] RedactContent(byte[] content, IEnumerable<string>? phiTerms = null)
    {
        if (content == null || content.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var text = Encoding.UTF8.GetString(content);
        var redactedText = RedactText(text, phiTerms);
        return Encoding.UTF8.GetBytes(redactedText);
    }

    /// <summary>
    /// Redacts configured terms and auto-detected PHI from string content.
    /// </summary>
    public string RedactText(string text, IEnumerable<string>? phiTerms = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var customTermsList = phiTerms?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var detections = _detector.Detect(text, ComplianceProfile.HIPAA, customTermsList);

        return DeidDetector.RedactText(text, detections, res =>
            res.Category == DetectorCategory.Custom ? "[REDACTED]" : $"[{res.Category.ToUpperInvariant()}]");
    }
}
