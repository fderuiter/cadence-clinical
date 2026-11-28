using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelCasebookHtmlExportWriter
{
    public static async Task WriteCasebookHtmlAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16384, leaveOpen: true);
        var sb = new StringBuilder(4096);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine($"  <title>Participant Casebook - {Html(casebook.SubjectCode)}</title>");
        sb.AppendLine("  <style>body{font-family:system-ui,sans-serif;margin:2rem;color:#111}table{width:100%;border-collapse:collapse;margin-bottom:1.5rem}th,td{border:1px solid #cbd5e1;padding:0.5rem;text-align:left;font-size:0.875rem}th{background:#f8fafc}.badge{display:inline-block;padding:0.15rem 0.45rem;border-radius:4px;background:#e2e8f0;font-size:0.75rem}</style>");
        sb.AppendLine("</head>");
        sb.AppendLine($"<body data-casebook-sha256=\"{Html(casebook.CasebookDigestSha256)}\">");
        sb.AppendLine($"  <h1>Participant Casebook: {Html(casebook.SubjectCode)}</h1>");
        sb.AppendLine($"  <p><strong>Study:</strong> {Html(casebook.StudyCode)} ({Html(casebook.StudyTitle)}) | <strong>Site:</strong> {Html(casebook.SiteCode)} | <strong>As Of:</strong> {Html(FormatInstant(casebook.KnownAt))} | <strong>Generated:</strong> {Html(FormatInstant(casebook.GeneratedAt))}</p>");
        sb.AppendLine($"  <p><strong>Producer:</strong> {Html(casebook.Producer)} | <strong>Canonical SHA-256:</strong> <code>{Html(casebook.CasebookDigestSha256)}</code></p>");

        if (casebook.Masked)
        {
            sb.AppendLine($"  <p class=\"badge\">masked ({Html(casebook.Mask ?? "NotPermitted")})</p>");
        }

        sb.AppendLine("  <h2>Clinical Observations &amp; Revisions</h2>");
        sb.AppendLine("  <table><thead><tr><th>Item</th><th>Concept</th><th>Current Value</th><th>Revision</th><th>Lock</th><th>Mask</th><th>History</th></tr></thead><tbody>");
        foreach (var obs in casebook.Observations)
        {
            var history = string.Join("; ", obs.Revisions.Select(r =>
                $"r{r.Revision.ToString(CultureInfo.InvariantCulture)}: {r.DisplayValue}{(r.Unit is not null ? $" {r.Unit}" : string.Empty)} ({r.ReasonForChange ?? "n/a"}){(r.Superseded ? " [superseded]" : string.Empty)}"));
            var valWithUnit = obs.Unit is not null && !obs.Masked ? $"{obs.DisplayValue} {obs.Unit}" : obs.DisplayValue;
            sb.AppendLine($"    <tr><td>{Html(obs.ItemId ?? "-")}</td><td>{Html(obs.ConceptLabel)}</td><td>{Html(valWithUnit)}</td><td>{Html(obs.CurrentRevision?.ToString(CultureInfo.InvariantCulture) ?? "-")}</td><td>{Html(obs.EffectiveLock ?? "Unlocked")}</td><td>{Html(obs.Mask ?? "-")}</td><td>{Html(history)}</td></tr>");
        }

        sb.AppendLine("  </tbody></table>");

        sb.AppendLine("  <h2>Queries &amp; Source Data Verifications</h2>");
        sb.AppendLine("  <table><thead><tr><th>Kind</th><th>Target</th><th>Detail</th><th>Actor</th><th>Recorded At</th><th>State</th></tr></thead><tbody>");
        foreach (var q in casebook.Queries)
        {
            var targetLabel = q.Revision is { } qRev
                ? $"{q.TargetRef} (r{qRev.ToString(CultureInfo.InvariantCulture)})"
                : q.TargetRef;
            var detail = !string.IsNullOrWhiteSpace(q.ReasonForChange) && q.ReasonForChange != "masked"
                ? $"{q.Text ?? string.Empty} ({q.ReasonForChange})"
                : q.Text ?? string.Empty;
            sb.AppendLine($"    <tr><td>{Html(q.Kind)}</td><td>{Html(targetLabel)}</td><td>{Html(detail)}</td><td>{Html(q.AssertedBy ?? "-")}</td><td>{Html(FormatInstant(q.AssertedAt))}</td><td>{(q.Closed ? "Closed" : "Open")}</td></tr>");
        }

        foreach (var v in casebook.Verifications)
        {
            sb.AppendLine($"    <tr><td>Verified</td><td>{Html(v.ObservationRef)} (r{Html(v.Revision?.ToString(CultureInfo.InvariantCulture) ?? "1")})</td><td>{Html(v.ReasonForChange ?? string.Empty)}</td><td>{Html(v.VerifiedBy ?? "-")}</td><td>{Html(FormatInstant(v.VerifiedAt))}</td><td>{(v.Retracted ? "Retracted" : "Active")}</td></tr>");
        }

        sb.AppendLine("  </tbody></table>");

        sb.AppendLine("  <h2>Electronic Signatures (21 CFR Part 11 Attestations)</h2>");
        sb.AppendLine("  <table><thead><tr><th>Attestation</th><th>Target</th><th>Meaning</th><th>Signer</th><th>Method</th><th>Signed At</th><th>Content Hash</th></tr></thead><tbody>");
        foreach (var a in casebook.Attestations)
        {
            sb.AppendLine($"    <tr><td>{Html(a.Ref)}</td><td>{Html(a.TargetRef)}</td><td>{Html(a.Meaning)}</td><td>{Html(a.SignerId)}</td><td>{Html(a.Method)}</td><td>{Html(FormatInstant(a.SignedAt))}</td><td><code>{Html(a.TargetContentHash)}</code></td></tr>");
        }

        sb.AppendLine("  </tbody></table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        await writer.WriteAsync(sb.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
