using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelCasebookPdfExportWriter
{
    public static async Task WriteCasebookPdfAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken)
    {
        var lines = BuildCasebookPdfLines(casebook);
        var pages = PaginatePdfLines(lines, maxLinesPerPage: 44);
        var pdfBytes = RenderPdf17Document(casebook, pages);
        await destination.WriteAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<PdfStyledLine> BuildCasebookPdfLines(KernelCasebookResponse casebook)
    {
        var lines = new List<PdfStyledLine>(128)
        {
            new(PdfFontKind.Heading, 15, $"Participant Casebook: {casebook.SubjectCode}"),
            new(PdfFontKind.Body, 10, $"Study: {casebook.StudyCode} ({casebook.StudyTitle}) | Site: {casebook.SiteCode} | Status: {casebook.EnrollmentStatus ?? "n/a"}"),
            new(PdfFontKind.Body, 9, $"Enrollment Ref: {casebook.EnrollmentRef} | Site Ref: {casebook.SiteRef} | Requested By: {casebook.RequestedBy}"),
            new(PdfFontKind.Body, 9, $"Valid At: {FormatInstant(casebook.ValidAt)} | Known At: {FormatInstant(casebook.KnownAt)} | Generated At: {FormatInstant(casebook.GeneratedAt)}"),
            new(PdfFontKind.Mono, 8, $"Producer: {casebook.Producer} | Canonical SHA-256: {casebook.CasebookDigestSha256}"),
        };

        if (casebook.Masked)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 10, $"K-AUTH-4 Visibility Notice: masked ({casebook.Mask ?? "NotPermitted"})"));
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "1. Forms & Collection Instances"));
        if (casebook.Forms.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No collection instances recorded at this bitemporal coordinate."));
        }
        else
        {
            foreach (var form in casebook.Forms)
            {
                var formDetail = form.Masked
                    ? $"[masked ({form.Mask ?? "NotPermitted"})]"
                    : $"{form.Title} ({form.CollectionSpecId}) | Visit: {form.PlannedActivityName ?? form.PlannedActivityId ?? "Unscheduled"} | Ref: {form.Ref}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, $"- {formDetail}", maxChars: 96);
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "2. Clinical Observations & Revision History"));
        if (casebook.Observations.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No clinical observations recorded at this bitemporal coordinate."));
        }
        else
        {
            foreach (var obs in casebook.Observations)
            {
                var valWithUnit = obs.Unit is not null && !obs.Masked ? $"{obs.DisplayValue} {obs.Unit}" : obs.DisplayValue;
                var maskSuffix = obs.Masked ? $" | Mask: masked ({obs.Mask ?? "Blinded"})" : string.Empty;
                var obsLine = $"- [{obs.ItemId ?? obs.ConceptCode}] {obs.ConceptLabel}: {valWithUnit} (r{obs.CurrentRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"}, Lock: {obs.EffectiveLock ?? "Unlocked"}{maskSuffix})";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, obsLine, maxChars: 96);

                foreach (var rev in obs.Revisions)
                {
                    var revVal = rev.Unit is not null && !obs.Masked ? $"{rev.DisplayValue} {rev.Unit}" : rev.DisplayValue;
                    var stateTag = rev.Superseded ? "superseded" : "current";
                    var revLine = $"    r{rev.Revision.ToString(CultureInfo.InvariantCulture)} [{stateTag}] {revVal} | by {rev.RecordedBy ?? "masked"} at {FormatInstant(rev.RecordedAt)} | reason: {rev.ReasonForChange ?? "n/a"}";
                    AppendWrappedLines(lines, PdfFontKind.Mono, 8, revLine, maxChars: 102);
                }
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "3. Queries & Source Data Verifications"));
        if (casebook.Queries.Count == 0 && casebook.Verifications.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No queries or source data verifications recorded."));
        }
        else
        {
            foreach (var q in casebook.Queries)
            {
                var qTarget = q.Revision is { } qRev
                    ? $"{q.TargetRef} (r{qRev.ToString(CultureInfo.InvariantCulture)})"
                    : q.TargetRef;
                var qDetail = !string.IsNullOrWhiteSpace(q.ReasonForChange) && q.ReasonForChange != "masked"
                    ? $"{q.Text ?? string.Empty} ({q.ReasonForChange})"
                    : q.Text ?? string.Empty;
                var qLine = $"- [{q.Kind}] Target: {qTarget} | State: {(q.Closed ? "Closed" : "Open")} | Actor: {q.AssertedBy ?? "-"} | At: {FormatInstant(q.AssertedAt)} | Detail: {qDetail}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, qLine, maxChars: 96);
            }

            foreach (var v in casebook.Verifications)
            {
                var vLine = $"- [Verified] Target: {v.ObservationRef} (r{v.Revision?.ToString(CultureInfo.InvariantCulture) ?? "1"}) | State: {(v.Retracted ? "Retracted" : "Active")} | Actor: {v.VerifiedBy ?? "-"} | At: {FormatInstant(v.VerifiedAt)}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, vLine, maxChars: 96);
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "4. Scope Locks"));
        if (casebook.Locks.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No scope locks covering this enrollment."));
        }
        else
        {
            foreach (var lk in casebook.Locks)
            {
                var lockState = lk.Released is true ? "Released" : "InEffect";
                var lkLine = lk.Masked
                    ? $"- [masked ({lk.Mask ?? "NotPermitted"})]"
                    : $"- [{lk.Kind ?? "Lock"}] Scope: {lk.ScopeRef} | State: {lockState} | By: {lk.LockedBy ?? "-"} | Reason: {lk.ReasonForChange ?? "-"}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, lkLine, maxChars: 96);
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "5. Electronic Signatures (21 CFR Part 11 Attestations)"));
        if (casebook.Attestations.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No electronic signatures recorded on visible records."));
        }
        else
        {
            foreach (var att in casebook.Attestations)
            {
                var attLine = $"- {att.Ref} | Target: {att.TargetRef} | Meaning: {att.Meaning} | Signer: {att.SignerId} | Method: {att.Method} | Signed: {FormatInstant(att.SignedAt)} | Hash: {att.TargetContentHash}";
                AppendWrappedLines(lines, PdfFontKind.Mono, 8, attLine, maxChars: 102);
            }
        }

        return lines;
    }

    private static void AppendWrappedLines(
        List<PdfStyledLine> target,
        PdfFontKind font,
        int fontSize,
        string text,
        int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            target.Add(new PdfStyledLine(font, fontSize, text));
            return;
        }

        var remaining = text.AsSpan();
        var continuationIndent = "    ";
        var firstChunk = true;
        while (remaining.Length > 0)
        {
            var limit = firstChunk ? maxChars : Math.Max(20, maxChars - continuationIndent.Length);
            if (remaining.Length <= limit)
            {
                var finalLine = firstChunk ? remaining.ToString() : continuationIndent + remaining.ToString();
                target.Add(new PdfStyledLine(font, fontSize, finalLine));
                break;
            }

            var slice = remaining[..limit];
            var splitAt = slice.LastIndexOf(' ');
            if (splitAt <= limit / 3)
            {
                splitAt = limit;
            }

            var segment = remaining[..splitAt].TrimEnd().ToString();
            target.Add(new PdfStyledLine(font, fontSize, firstChunk ? segment : continuationIndent + segment));
            remaining = remaining[splitAt..].TrimStart();
            firstChunk = false;
        }
    }

    private static List<IReadOnlyList<PdfStyledLine>> PaginatePdfLines(
        IReadOnlyList<PdfStyledLine> lines,
        int maxLinesPerPage)
    {
        var pages = new List<IReadOnlyList<PdfStyledLine>>();
        for (var idx = 0; idx < lines.Count; idx += maxLinesPerPage)
        {
            var count = Math.Min(maxLinesPerPage, lines.Count - idx);
            var chunk = new List<PdfStyledLine>(count);
            for (var j = 0; j < count; j++)
            {
                chunk.Add(lines[idx + j]);
            }

            pages.Add(chunk);
        }

        if (pages.Count == 0)
        {
            pages.Add([new PdfStyledLine(PdfFontKind.Body, 10, "Empty casebook.")]);
        }

        return pages;
    }

    private static byte[] RenderPdf17Document(
        KernelCasebookResponse casebook,
        IReadOnlyList<IReadOnlyList<PdfStyledLine>> pages)
    {
        var totalObjects = 6 + (pages.Count * 2);
        var offsets = new long[totalObjects + 1];

        using var output = new MemoryStream(8192);
        WriteAscii(output, "%PDF-1.7\n");
        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        offsets[1] = output.Position;
        WriteAscii(output, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kidsRefs = string.Join(' ', Enumerable.Range(0, pages.Count)
            .Select(p => $"{(7 + (p * 2)).ToString(CultureInfo.InvariantCulture)} 0 R"));
        offsets[2] = output.Position;
        WriteAscii(output, $"2 0 obj\n<< /Type /Pages /Kids [{kidsRefs}] /Count {pages.Count.ToString(CultureInfo.InvariantCulture)} >>\nendobj\n");

        offsets[3] = output.Position;
        WriteAscii(output, "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        offsets[4] = output.Position;
        WriteAscii(output, "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        offsets[5] = output.Position;
        WriteAscii(output, "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n");

        var pdfCreationDate = $"D:{casebook.GeneratedAt.ToUniversalTime():yyyyMMddHHmmss}Z";
        offsets[6] = output.Position;
        WriteAscii(
            output,
            $"6 0 obj\n<< /Title ({EscapePdfLiteral($"Participant Casebook - {casebook.SubjectCode}")}) /Producer ({EscapePdfLiteral(casebook.Producer)}) /Creator (Cadence Clinical Kernel) /Subject ({EscapePdfLiteral($"Study {casebook.StudyCode} | SHA-256 {casebook.CasebookDigestSha256}")}) /CreationDate ({EscapePdfLiteral(pdfCreationDate)}) >>\nendobj\n");

        for (var p = 0; p < pages.Count; p++)
        {
            var pageObjId = 7 + (p * 2);
            var streamObjId = 8 + (p * 2);
            var contentBytes = BuildPdfPageContentBytes(casebook, pages[p], p + 1, pages.Count);

            offsets[pageObjId] = output.Position;
            WriteAscii(
                output,
                $"{pageObjId.ToString(CultureInfo.InvariantCulture)} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {streamObjId.ToString(CultureInfo.InvariantCulture)} 0 R >>\nendobj\n");

            offsets[streamObjId] = output.Position;
            WriteAscii(output, $"{streamObjId.ToString(CultureInfo.InvariantCulture)} 0 obj\n<< /Length {contentBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n");
            output.Write(contentBytes);
            WriteAscii(output, "\nendstream\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {(totalObjects + 1).ToString(CultureInfo.InvariantCulture)}\n");
        WriteAscii(output, "0000000000 65535 f \n");
        for (var objId = 1; objId <= totalObjects; objId++)
        {
            WriteAscii(output, $"{offsets[objId].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        WriteAscii(
            output,
            $"trailer\n<< /Size {(totalObjects + 1).ToString(CultureInfo.InvariantCulture)} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

        return output.ToArray();
    }

    private static byte[] BuildPdfPageContentBytes(
        KernelCasebookResponse casebook,
        IReadOnlyList<PdfStyledLine> lines,
        int pageNumber,
        int totalPages)
    {
        var sb = new StringBuilder(2048);
        sb.Append("0.82 0.86 0.91 RG 0.75 w 48 730 m 564 730 l S\n");

        var cursorY = 744;
        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line.Text))
            {
                var fontAlias = line.Font switch
                {
                    PdfFontKind.Heading or PdfFontKind.Subheading => "/F2",
                    PdfFontKind.Mono => "/F3",
                    _ => "/F1",
                };

                sb.Append("BT ");
                sb.Append(fontAlias);
                sb.Append(' ');
                sb.Append(line.FontSize.ToString(CultureInfo.InvariantCulture));
                sb.Append(" Tf 48 ");
                sb.Append(cursorY.ToString(CultureInfo.InvariantCulture));
                sb.Append(" Td (");
                sb.Append(EscapePdfLiteral(line.Text));
                sb.Append(") Tj ET\n");
            }

            cursorY -= line.Font switch
            {
                PdfFontKind.Heading => 20,
                PdfFontKind.Subheading => 16,
                _ => 14,
            };
        }

        sb.Append("0.82 0.86 0.91 RG 0.75 w 48 44 m 564 44 l S\n");
        var footer = $"Page {pageNumber.ToString(CultureInfo.InvariantCulture)} of {totalPages.ToString(CultureInfo.InvariantCulture)} | Study: {casebook.StudyCode} | Subject: {casebook.SubjectCode} | SHA-256: {casebook.CasebookDigestSha256}";
        sb.Append("BT /F3 7 Tf 48 32 Td (");
        sb.Append(EscapePdfLiteral(footer));
        sb.Append(") Tj ET");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void WriteAscii(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes);
    }

    private static string EscapePdfLiteral(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length + 8);
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '(':
                    sb.Append(@"\(");
                    break;
                case ')':
                    sb.Append(@"\)");
                    break;
                case '\r':
                case '\n':
                case '\t':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(ch is >= ' ' and <= '~' ? ch : '?');
                    break;
            }
        }

        return sb.ToString();
    }
}
