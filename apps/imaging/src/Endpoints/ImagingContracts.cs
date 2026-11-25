using System.Collections.Generic;

using Cadence.Apps.Imaging.Domain;

namespace Cadence.Apps.Imaging.Endpoints;

public record CertificateResponse(
    DeidentificationCertificate Certificate,
    bool IsSignatureValid);

public record ReaderAssessmentDto(
    string ReaderId,
    string ReaderName,
    double SumLongestDiametersMm,
    string TargetResponse,
    string OverallResponse,
    bool NewLesionsPresent,
    List<string>? Notes = null);

public record SubmitAdjudicationSessionRequest(
    string StudyId,
    string SiteId,
    string SubjectId,
    string TimepointName,
    ReaderAssessmentDto Reader1Assessment,
    ReaderAssessmentDto Reader2Assessment,
    string AssessmentCriteria = "RECIST_1_1");

public record ChairAdjudicateImagingRequest(
    string FinalConsensusResponse,
    double FinalConsensusSldMm,
    string BindingReader,
    string AdjudicationRationale,
    string SignerName,
    string SignatureMeaning);

public record AdjudicationSessionResponse(
    string SessionId,
    string StudyId,
    string SubjectId,
    string TimepointName,
    string WorkflowStatus,
    bool IsResponseDiscordant,
    double SldDiscordanceDelta,
    List<string> DiscordanceReasons,
    string? FinalConsensusResponse,
    double? FinalConsensusSldMm,
    string? BindingReader,
    string? AdjudicationRationale,
    string SessionDigest);
