using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Forms.Errors;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Observations.Errors;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Trust;
using Cadence.Security;

using Microsoft.Extensions.Configuration;

namespace Cadence.Apps.Execution.Application.Forms;

/// <summary>
/// Application service implementing form submission lifecycle, eCRF payload ingestion,
/// 21 CFR Part 11 electronic signature attestation, and audited unlock reversion.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-003
/// </summary>
public sealed class FormSubmissionApplicationService(
    IFormSubmissionRepository formSubmissionRepository,
    IObservationRepository observationRepository,
    IUcumNormalizationService ucumService,
    ICurrentAuditContextAccessor auditContextAccessor,
    ISubjectRepository? subjectRepository = null,
    IEditCheckEngine? editCheckEngine = null,
    IAutoQueryDispatchService? autoQueryDispatchService = null,
    IConfiguration? configuration = null) : IFormSubmissionApplicationService
{
    private static readonly Dictionary<string, (string Domain, string TestCode, string TestName, string? Unit)> FieldMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vssbp"] = ("VS", "VSSBP", "Systolic Blood Pressure", "mmHg"),
        ["vsdpb"] = ("VS", "VSDPB", "Diastolic Blood Pressure", "mmHg"),
        ["pulse"] = ("VS", "VSHR", "Pulse Rate", "bpm"),
        ["vshr"] = ("VS", "VSHR", "Pulse Rate", "bpm"),
        ["weight"] = ("VS", "WT", "Weight", "kg"),
        ["wt"] = ("VS", "WT", "Weight", "kg"),
        ["height"] = ("VS", "HT", "Height", "m"),
        ["ht"] = ("VS", "HT", "Height", "m"),
        ["temp"] = ("VS", "TEMP", "Body Temperature", "Cel"),
        ["vstemp"] = ("VS", "TEMP", "Body Temperature", "Cel"),
        ["wbc"] = ("LB", "WBC", "White Blood Cell Count", "10^9/L"),
        ["lb_wbc"] = ("LB", "WBC", "White Blood Cell Count", "10^9/L"),
        ["gluc"] = ("LB", "GLUC", "Glucose", "mg/dL"),
        ["lb_gluc"] = ("LB", "GLUC", "Glucose", "mg/dL"),
        ["alt"] = ("LB", "ALT", "Alanine Aminotransferase", "U/L"),
        ["lb_alt"] = ("LB", "ALT", "Alanine Aminotransferase", "U/L"),
    };

    public async Task<Result<FormSubmissionResponse>> CreateAsync(
        FormSubmissionCreateRequest request,
        CancellationToken cancellationToken)
    {
        SubjectCode subjectCode;
        try
        {
            subjectCode = new SubjectCode(request.SubjectId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_SUBJECT_CODE", ex.Message);
        }

        StudyCode studyCode;
        try
        {
            studyCode = new StudyCode(request.StudyId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_STUDY_CODE", ex.Message);
        }

        FormId formId;
        try
        {
            formId = new FormId(request.FormId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_FORM_ID", ex.Message);
        }

        SiteCode? siteCode = null;
        if (!string.IsNullOrWhiteSpace(request.SiteId))
        {
            try
            {
                siteCode = new SiteCode(request.SiteId);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_SITE_CODE", ex.Message);
            }
        }

        ClinicalSubject? subject = null;
        if (subjectRepository is not null)
        {
            subject = await subjectRepository.GetByIdAsync(request.SubjectId, cancellationToken).ConfigureAwait(false)
                ?? await subjectRepository.GetByCodeAsync(request.SubjectId, cancellationToken).ConfigureAwait(false);
        }

        string activeProtocolVersion = subject?.ActiveProtocolVersion ?? "1.0.0";

        string resolvedProtocolVersion;
        if (string.IsNullOrWhiteSpace(request.ProtocolVersion))
        {
            resolvedProtocolVersion = activeProtocolVersion;
        }
        else
        {
            string suppliedVersion = request.ProtocolVersion.Trim();
            if (!string.Equals(suppliedVersion, activeProtocolVersion.Trim(), StringComparison.Ordinal))
            {
                return FormErrors.ProtocolVersionMismatch(suppliedVersion, activeProtocolVersion);
            }

            resolvedProtocolVersion = suppliedVersion;
        }

        var initResult = FormSubmission.Initialize(
            studyId: studyCode,
            subjectId: subjectCode,
            formId: formId,
            siteId: siteCode,
            visitId: request.VisitId,
            protocolVersion: resolvedProtocolVersion,
            initialPayload: request.Payload);

        if (initResult.IsFailure)
        {
            return initResult.Error!;
        }

        var submission = initResult.Value!;
        submission.CreatedBy = auditContextAccessor.UserId;
        submission.ReasonForChange = auditContextAccessor.ChangeReason;

        await formSubmissionRepository.AddAsync(submission, cancellationToken).ConfigureAwait(false);

        // Extract and persist discrete observations from structured payload
        if (!string.IsNullOrWhiteSpace(request.Payload))
        {
            var persisted = await PersistObservationsAsync(submission, request.Payload, cancellationToken).ConfigureAwait(false);
            if (persisted.IsFailure)
            {
                return persisted.Error!;
            }

            if (editCheckEngine != null && autoQueryDispatchService != null && persisted.Value is not null)
            {
                var observations = persisted.Value;
                var violations = new List<EditCheckViolation>();

                foreach (var obs in observations)
                {
                    violations.AddRange(editCheckEngine.EvaluateInlineConstraints(obs));
                }

                var batchContext = new BatchEvaluationContext(
                    submission.StudyId.Value,
                    submission.SubjectId.Value,
                    observations,
                    [submission]);

                violations.AddRange(editCheckEngine.EvaluateBatchRules(batchContext));

                await autoQueryDispatchService.ProcessViolationsAndAutoResolutionAsync(
                    submission.StudyId,
                    submission.SubjectId,
                    submission.SiteId,
                    submission.VisitId,
                    submission.FormId.Value,
                    observations,
                    violations,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await formSubmissionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToResponse(submission);
    }

    public async Task<Result<FormSubmissionResponse>> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (!FormSubmissionId.TryParse(id, out var submissionId))
        {
            return FormErrors.NotFound(id);
        }

        var submission = await formSubmissionRepository.GetByIdAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return FormErrors.NotFound(id);
        }

        return MapToResponse(submission);
    }

    public async Task<Result<IReadOnlyList<FormSubmissionResponse>>> ListBySubjectAsync(
        string studyId,
        string subjectId,
        CancellationToken cancellationToken)
    {
        var studyCode = new StudyCode(studyId);
        var subjectCode = new SubjectCode(subjectId);

        var list = await formSubmissionRepository.ListBySubjectAsync(studyCode, subjectCode, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<FormSubmissionResponse>>.Success(list.Select(MapToResponse).ToList());
    }

    public async Task<Result<FormSubmissionResponse>> UpdatePayloadAsync(
        string id,
        string newPayload,
        CancellationToken cancellationToken)
    {
        if (!FormSubmissionId.TryParse(id, out var submissionId))
        {
            return FormErrors.NotFound(id);
        }

        var submission = await formSubmissionRepository.GetForUpdateAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return FormErrors.NotFound(id);
        }

        var updateResult = submission.UpdatePayload(newPayload, auditContextAccessor.ChangeReason);
        if (updateResult.IsFailure)
        {
            return updateResult.Error!;
        }

        if (!string.IsNullOrWhiteSpace(newPayload))
        {
            var persisted = await PersistObservationsAsync(submission, newPayload, cancellationToken).ConfigureAwait(false);
            if (persisted.IsFailure)
            {
                return persisted.Error!;
            }

            if (autoQueryDispatchService != null && persisted.Value is not null)
            {
                var observations = persisted.Value;
                var violations = new List<EditCheckViolation>();

                if (editCheckEngine != null)
                {
                    foreach (var obs in observations)
                    {
                        violations.AddRange(editCheckEngine.EvaluateInlineConstraints(obs));
                    }

                    var batchContext = new BatchEvaluationContext(
                        submission.StudyId.Value,
                        submission.SubjectId.Value,
                        observations,
                        [submission]);

                    violations.AddRange(editCheckEngine.EvaluateBatchRules(batchContext));
                }

                await autoQueryDispatchService.ProcessViolationsAndAutoResolutionAsync(
                    submission.StudyId,
                    submission.SubjectId,
                    submission.SiteId,
                    submission.VisitId,
                    submission.FormId.Value,
                    observations,
                    violations,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await observationRepository.DeactivateByFormSubmissionIdAsync(submission.Id, cancellationToken).ConfigureAwait(false);

            if (autoQueryDispatchService != null)
            {
                await autoQueryDispatchService.ProcessViolationsAndAutoResolutionAsync(
                    submission.StudyId,
                    submission.SubjectId,
                    submission.SiteId,
                    submission.VisitId,
                    submission.FormId.Value,
                    Array.Empty<ClinicalObservation>(),
                    Array.Empty<EditCheckViolation>(),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await formSubmissionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToResponse(submission);
    }

    public async Task<Result<FormSubmissionResponse>> CompleteAsync(string id, CancellationToken cancellationToken)
    {
        if (!FormSubmissionId.TryParse(id, out var submissionId))
        {
            return FormErrors.NotFound(id);
        }

        var submission = await formSubmissionRepository.GetForUpdateAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return FormErrors.NotFound(id);
        }

        var completeResult = submission.Complete();
        if (completeResult.IsFailure)
        {
            return completeResult.Error!;
        }

        submission.ReasonForChange = auditContextAccessor.ChangeReason;
        await formSubmissionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToResponse(submission);
    }

    public async Task<Result<FormSubmissionResponse>> ApproveAsync(
        string id,
        FormSubmissionApproveRequest request,
        AuthenticatedCaller? signer,
        CancellationToken cancellationToken)
    {
        if (!FormSubmissionId.TryParse(id, out var submissionId))
        {
            return FormErrors.NotFound(id);
        }

        var submission = await formSubmissionRepository.GetForUpdateAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return FormErrors.NotFound(id);
        }

        var manifest = request.SignatureManifest;
        if (manifest is null)
        {
            return FormErrors.SignatureRequired();
        }

        // Validate signing reason against allowed set
        string signingReason = !string.IsNullOrWhiteSpace(request.SigningReason)
            ? request.SigningReason.Trim()
            : manifest.SigningReason?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(signingReason) || !FormSubmission.ValidSigningReasons.Contains(signingReason))
        {
            return FormErrors.InvalidSigningReason(signingReason);
        }

        // The signer is the authenticated caller. The manifest may restate their id but cannot name
        // someone else, and its role field is ignored: the investigator role must come from the caller's
        // signed roles.
        if (signer is null)
        {
            return FormErrors.UnauthorizedSignerRole(string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(manifest.SignerId) && !string.Equals(manifest.SignerId, signer.UserId, StringComparison.Ordinal))
        {
            return FormErrors.SignerIdentityMismatch(manifest.SignerId);
        }

        string? signerRole = signer.FirstRoleMatching(IsAuthorizedInvestigatorRole);
        if (signerRole is null)
        {
            return FormErrors.UnauthorizedSignerRole(string.Join(",", signer.Roles));
        }

        // The signer's hash covers the form content, not the text the database returns: both sides hash the
        // RFC 8785 canonical form, so key order, whitespace and number spelling do not matter (#5023).
        string? computedHash = ComputePayloadHash(submission.Payload ?? "{}");

        if (computedHash is null ||
            string.IsNullOrWhiteSpace(manifest.PayloadHash) ||
            !string.Equals(manifest.PayloadHash.Trim(), computedHash, StringComparison.OrdinalIgnoreCase))
        {
            return FormErrors.InvalidPayloadHash();
        }

        string secret = configuration?["GATEWAY_SECRET"]
            ?? Environment.GetEnvironmentVariable("GATEWAY_SECRET")
            ?? string.Empty;

        if (!HmacSigner.VerifySignature(computedHash, secret, manifest.HmacSignature ?? string.Empty))
        {
            return FormErrors.InvalidHmacSignature();
        }

        var normalizedManifest = manifest with
        {
            SignerId = signer.UserId,
            SignerName = string.IsNullOrWhiteSpace(manifest.SignerName) ? signer.UserId : manifest.SignerName,
            SignerRole = signerRole,
            SigningReason = signingReason,
        };
        var approveResult = submission.Approve(normalizedManifest);
        if (approveResult.IsFailure)
        {
            return approveResult.Error!;
        }

        submission.ReasonForChange = signingReason;
        await formSubmissionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToResponse(submission);
    }

    private static bool IsAuthorizedInvestigatorRole(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "site_investigator" or "site investigator" or "site-investigator"
            or "investigator" or "principal_investigator" or "principal investigator" or "pi"
            or "sub_investigator" or "sub investigator" or "sub-investigator" or "admin";
    }

    public async Task<Result<FormSubmissionResponse>> UnlockAsync(
        string id,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!FormSubmissionId.TryParse(id, out var submissionId))
        {
            return FormErrors.NotFound(id);
        }

        var submission = await formSubmissionRepository.GetForUpdateAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return FormErrors.NotFound(id);
        }

        var unlockResult = submission.Unlock(auditContextAccessor.UserId, reason);
        if (unlockResult.IsFailure)
        {
            return unlockResult.Error!;
        }

        await formSubmissionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToResponse(submission);
    }

    /// <summary>
    /// The payload hash a signer puts in an approval's signature manifest: the lower-case hex SHA-256 of the RFC 8785
    /// (JCS) canonical form of the payload (<see cref="JsonCanonicalization"/>). A client computes it from the payload
    /// it submitted, for example <c>sha256(canonicalSerialize(JSON.parse(payload)))</c> with
    /// <c>packages/ui/signing.js</c>. Null when the payload is not JSON that JCS can canonicalize.
    /// </summary>
    public static string? ComputePayloadHash(string payload)
    {
        try
        {
            return JsonCanonicalization.Sha256Hex(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the discrete observations a payload carries. Each needs a valid time: an item's own
    /// <c>observation_date</c>, else the payload's top-level <c>observation_date</c>. A payload that yields
    /// observations without one is rejected rather than stamped with the time it was recorded (#4769).
    /// Payloads that are not JSON objects yield no observations.
    /// </summary>
    private Result<List<ClinicalObservation>> ExtractObservations(FormSubmission submission, string payloadJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return new List<ClinicalObservation>();
        }

        using (doc)
        {
            var root = doc.RootElement;
            var observations = new List<ClinicalObservation>();
            if (root.ValueKind != JsonValueKind.Object)
            {
                return observations;
            }

            var payloadDate = ReadObservationDate(root);
            if (payloadDate.IsFailure)
            {
                return payloadDate.Error!;
            }

            // Check if structured 'observations' array is present
            if (root.TryGetProperty("observations", out var obsArray) && obsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in obsArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;

                    var itemDate = ReadObservationDate(item);
                    if (itemDate.IsFailure)
                    {
                        return itemDate.Error!;
                    }

                    if ((itemDate.Value ?? payloadDate.Value) is not { } observationDate)
                    {
                        return ObservationErrors.ObservationDateRequired();
                    }

                    string domainStr = item.TryGetProperty("domain", out var d) ? d.GetString() ?? "VS" : "VS";
                    string testCodeStr = item.TryGetProperty("test_code", out var tc) ? tc.GetString() ?? "UNKNOWN" : "UNKNOWN";
                    string testNameStr = item.TryGetProperty("test_name", out var tn) ? tn.GetString() ?? testCodeStr : testCodeStr;
                    string? unit = item.TryGetProperty("unit", out var u) ? u.GetString() : null;

                    double? val = null;
                    if (item.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
                    {
                        val = v.GetDouble();
                    }

                    string? valStr = item.TryGetProperty("value_string", out var vs) ? vs.GetString() : val?.ToString(CultureInfo.InvariantCulture);

                    AddIfValid(observations, submission, new DomainCode(domainStr), new TestCode(testCodeStr), testNameStr, observationDate, val, valStr, unit);
                }
            }
            else
            {
                // Key-value mapping of standard vitals/labs
                foreach (var prop in root.EnumerateObject())
                {
                    string key = prop.Name.ToLowerInvariant().Replace(".", "_").Replace("-", "_");
                    if (FieldMapping.TryGetValue(key, out var mapping))
                    {
                        if (payloadDate.Value is not { } observationDate)
                        {
                            return ObservationErrors.ObservationDateRequired();
                        }

                        double? val = null;
                        if (prop.Value.ValueKind == JsonValueKind.Number)
                        {
                            val = prop.Value.GetDouble();
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String &&
                                 double.TryParse(prop.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                        {
                            val = parsed;
                        }

                        string? valStr = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : val?.ToString(CultureInfo.InvariantCulture);

                        AddIfValid(observations, submission, new DomainCode(mapping.Domain), new TestCode(mapping.TestCode), mapping.TestName, observationDate, val, valStr, mapping.Unit);
                    }
                }
            }

            return observations;
        }
    }

    private void AddIfValid(
        List<ClinicalObservation> observations,
        FormSubmission submission,
        DomainCode domain,
        TestCode testCode,
        string testName,
        DateTime observationDate,
        double? value,
        string? valueString,
        string? unit)
    {
        var (normVal, normUnit) = ucumService.GetNormalizedRepresentation(value, unit);
        var obsResult = ClinicalObservation.Create(
            studyId: submission.StudyId,
            subjectId: submission.SubjectId,
            domain: domain,
            testCode: testCode,
            testName: testName,
            observationDate: observationDate,
            value: value,
            valueString: valueString,
            unit: unit,
            siteId: submission.SiteId,
            visitId: submission.VisitId,
            protocolVersionTag: submission.ProtocolVersion,
            protocolVersionIndex: 1,
            normalizedValue: normVal,
            normalizedUnit: normUnit,
            formSubmissionId: submission.Id,
            isActive: true);

        if (obsResult.IsSuccess && obsResult.Value is not null)
        {
            observations.Add(obsResult.Value);
        }
    }

    /// <summary>Reads an optional ISO 8601 <c>observation_date</c> property as UTC.</summary>
    private static Result<DateTime?> ReadObservationDate(JsonElement element)
    {
        if (!element.TryGetProperty("observation_date", out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return (DateTime?)null;
        }

        string raw = property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.GetRawText();
        if (property.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return ObservationErrors.InvalidObservationDate(raw);
        }

        return (DateTime?)parsed.UtcDateTime;
    }

    private async Task<Result<List<ClinicalObservation>>> PersistObservationsAsync(
        FormSubmission submission,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var extracted = ExtractObservations(submission, payloadJson);
        if (extracted.IsFailure)
        {
            return extracted.Error!;
        }

        await observationRepository.DeactivateByFormSubmissionIdAsync(submission.Id, cancellationToken).ConfigureAwait(false);

        foreach (var observation in extracted.Value!)
        {
            await observationRepository.AddAsync(observation, cancellationToken).ConfigureAwait(false);
        }

        return extracted.Value!;
    }

    private static FormSubmissionResponse MapToResponse(FormSubmission f) =>
        new(
            Id: f.Id.ToString(),
            StudyId: f.StudyId.Value,
            SubjectId: f.SubjectId.Value,
            FormId: f.FormId.Value,
            Status: f.Status,
            SiteId: f.SiteId?.Value,
            VisitId: f.VisitId,
            ProtocolVersion: f.ProtocolVersion,
            Payload: f.Payload,
            SignatureManifest: f.SignatureManifest,
            IsActive: f.IsActive,
            IsReadonly: f.IsReadonly,
            ClonedFromId: f.ClonedFromId,
            Version: f.VersionIndex
        );
}
