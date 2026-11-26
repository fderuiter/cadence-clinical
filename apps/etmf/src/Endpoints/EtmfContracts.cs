using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Etmf.Endpoints;

public record ClassifyRequest(
    string Filename,
    string? Content = null,
    string? MimeType = "application/pdf",
    string? ContentHint = null,
    string? TaxonomyVersion = "v3.2.0-complete"
);

public record StageDocumentQCRequest(
    string? Content,
    string Filename,
    string StudyId,
    string? SiteId = null,
    string? MimeType = "application/pdf",
    string? ArtifactHint = null,
    string? TaxonomyVersion = "v3.2.0-complete",
    string ReasonForChange = "Batch upload AI auto-classification staging for CRA QC",
    string? AssignedCra = null,
    string? ContentBase64 = null
);

public record CraqcReviewRequest(
    string Decision,
    string ReasonForChange,
    string? OverrideArtifactCode = null,
    string? DiscrepancyComment = null
);

public record SubmitDocumentForQcRequest(
    string? ReasonForChange = null,
    string? AssignedCra = null
);

public record UpdateDocumentTaxonomyRequest(
    int? Zone = null,
    string? Section = null,
    string? ArtifactCode = null,
    string? ArtifactType = null,
    string? ReasonForChange = null
);

public record IngestDocumentRequest(
    string Filename,
    string? Content,
    string? StudyId,
    string? SiteId,
    string? MimeType,
    int Zone,
    string? Section,
    string? ArtifactCode,
    string? ArtifactType,
    string? TaxonomyVersion,
    string? ReasonForChange,
    string? ContentBase64 = null
);

public record DocumentIntelligenceReport(
    string? DocumentId,
    string Filename,
    string MimeType,
    string Sha256Hash,
    string Modality,
    TaxonomyMatchCandidate PrimaryClassification,
    List<TaxonomyMatchCandidate> AlternativeCandidates,
    string ConfidenceTier,
    string QcRecommendation,
    Dictionary<string, object> AiGenerationManifest
);

public record TaxonomyMatchCandidate(
    int Zone,
    string Section,
    string ArtifactCode,
    string ArtifactType,
    double Confidence
);

public record CraqcStagingItem(
    string DocumentId,
    string StudyId,
    string? SiteId,
    string Filename,
    string Status,
    DocumentIntelligenceReport IntelligenceReport,
    DateTimeOffset StagedAt,
    string StagedBy,
    string? QcAssignedTo,
    List<string> DiscrepancyNotes
);

public record ExportDocumentsRequest(
    [property: JsonPropertyName("study_id")] string? StudyId = null,
    [property: JsonPropertyName("document_ids")] List<string>? DocumentIds = null,
    [property: JsonPropertyName("zone")] int? Zone = null,
    [property: JsonPropertyName("section")] string? Section = null,
    [property: JsonPropertyName("artifact_code")] string? ArtifactCode = null,
    [property: JsonPropertyName("is_full_binder")] bool IsFullBinder = false
);
