using System;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Result of compiling a GxP visit letter PDF and executing the dispatch pipeline.
/// </summary>
public record VisitLetterCompilationResultDto(
    string LetterId,
    string VisitId,
    string LetterType,
    string PdfSha256Hash,
    string PdfObjectKey,
    bool IsSigned,
    string Status,
    string SignatureManifestation,
    string? EtmfDocumentId,
    DateTimeOffset? DispatchedAt,
    string PdfAccessUrl,
    string PdfBase64
);

/// <summary>
/// Result of GxP PDF compilation.
/// </summary>
public record GxpPdfResult(
    byte[] PdfBytes,
    string Sha256Hash,
    bool IsSigned,
    string SignatureManifestationJson,
    string RenderedHtml
);
