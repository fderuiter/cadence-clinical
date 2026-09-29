using System;
using System.Collections.Generic;

using Bogus;

namespace Cadence.Testing.Factories;

public record SubjectRecord(
    string Id,
    string StudyId,
    string SiteId,
    string Status,
    string ProtocolVersion,
    DateTime CreatedAt
);

public record ProtocolDefinitionRecord(
    string Id,
    string ProtocolTitle,
    string ProtocolVersion,
    bool IsActive,
    IReadOnlyList<string> Arms,
    DateTime CreatedAt
);

public record ClinicalObservationRecord(
    string Id,
    string SubjectId,
    string ItemGroupOid,
    string ItemOid,
    string Value,
    string Status,
    long VersionIndex,
    DateTime CreatedAt
);

public record QueryDiscrepancyRecord(
    string Id,
    string SubjectId,
    string FieldName,
    string QueryText,
    string Status,
    string CreatedBy,
    DateTime CreatedAt
);

public record ConsentRecord(
    string Id,
    string SubjectPseudonym,
    string StudyId,
    string SiteId,
    string ProtocolVersion,
    IReadOnlyDictionary<string, object?> SignatureManifest,
    DateTime CreatedAt
);

public record DocumentMetadataRecord(
    string Id,
    string StudyId,
    string? SiteId,
    string Zone,
    string Filename,
    long VersionIndex,
    DateTime CreatedAt
);

public record AuditLogRecord(
    string Id,
    string UserId,
    string Action,
    string ResourceType,
    string ResourceId,
    string ReasonForChange,
    long VersionIndex,
    DateTime Timestamp
);

public static class SubjectFactory
{
    private static readonly Faker<SubjectRecord> FakerInstance = new Faker<SubjectRecord>()
        .CustomInstantiator(f => new SubjectRecord(
            Id: $"SUBJ-{f.Random.Hexadecimal(6, "").ToUpperInvariant()}",
            StudyId: "study_oncology_phase3",
            SiteId: "SITE-101",
            Status: "ENROLLED",
            ProtocolVersion: "1.0",
            CreatedAt: DateTime.UtcNow
        ));

    public static SubjectRecord Create(
        string? id = null,
        string? studyId = null,
        string? siteId = null,
        string? status = null,
        string? protocolVersion = null,
        DateTime? createdAt = null)
    {
        var baseRecord = FakerInstance.Generate();
        return baseRecord with
        {
            Id = id ?? baseRecord.Id,
            StudyId = studyId ?? baseRecord.StudyId,
            SiteId = siteId ?? baseRecord.SiteId,
            Status = status ?? baseRecord.Status,
            ProtocolVersion = protocolVersion ?? baseRecord.ProtocolVersion,
            CreatedAt = createdAt ?? baseRecord.CreatedAt
        };
    }
}

public static class ProtocolDefinitionFactory
{
    private static readonly Faker<ProtocolDefinitionRecord> FakerInstance = new Faker<ProtocolDefinitionRecord>()
        .CustomInstantiator(f => new ProtocolDefinitionRecord(
            Id: $"PROT-{f.Random.Hexadecimal(6, "").ToUpperInvariant()}",
            ProtocolTitle: "Phase 3 Oncology Randomized Double-Blind Trial",
            ProtocolVersion: "1.0",
            IsActive: true,
            Arms: new[] { "Arm-A-Investigational", "Arm-B-Control" },
            CreatedAt: DateTime.UtcNow
        ));

    public static ProtocolDefinitionRecord Create(
        string? id = null,
        string? protocolTitle = null,
        string? protocolVersion = null,
        bool? isActive = null,
        IReadOnlyList<string>? arms = null,
        DateTime? createdAt = null)
    {
        var baseRecord = FakerInstance.Generate();
        return baseRecord with
        {
            Id = id ?? baseRecord.Id,
            ProtocolTitle = protocolTitle ?? baseRecord.ProtocolTitle,
            ProtocolVersion = protocolVersion ?? baseRecord.ProtocolVersion,
            IsActive = isActive ?? baseRecord.IsActive,
            Arms = arms ?? baseRecord.Arms,
            CreatedAt = createdAt ?? baseRecord.CreatedAt
        };
    }
}

public static class ClinicalObservationFactory
{
    private static readonly Faker<ClinicalObservationRecord> FakerInstance = new Faker<ClinicalObservationRecord>()
        .CustomInstantiator(f => new ClinicalObservationRecord(
            Id: $"OBS-{f.Random.Hexadecimal(8, "").ToLowerInvariant()}",
            SubjectId: "SUBJ-001",
            ItemGroupOid: "VS",
            ItemOid: "SYSBP",
            Value: "120",
            Status: "VALID",
            VersionIndex: 1,
            CreatedAt: DateTime.UtcNow
        ));

    public static ClinicalObservationRecord Create(
        string? id = null,
        string? subjectId = null,
        string? itemGroupOid = null,
        string? itemOid = null,
        string? value = null,
        string? status = null,
        long? versionIndex = null,
        DateTime? createdAt = null)
    {
        var baseRecord = FakerInstance.Generate();
        return baseRecord with
        {
            Id = id ?? baseRecord.Id,
            SubjectId = subjectId ?? baseRecord.SubjectId,
            ItemGroupOid = itemGroupOid ?? baseRecord.ItemGroupOid,
            ItemOid = itemOid ?? baseRecord.ItemOid,
            Value = value ?? baseRecord.Value,
            Status = status ?? baseRecord.Status,
            VersionIndex = versionIndex ?? baseRecord.VersionIndex,
            CreatedAt = createdAt ?? baseRecord.CreatedAt
        };
    }
}

public static class QueryDiscrepancyFactory
{
    private static readonly Faker<QueryDiscrepancyRecord> FakerInstance = new Faker<QueryDiscrepancyRecord>()
        .CustomInstantiator(f => new QueryDiscrepancyRecord(
            Id: $"QRY-{f.Random.Hexadecimal(6, "").ToUpperInvariant()}",
            SubjectId: "SUBJ-001",
            FieldName: "SYSBP",
            QueryText: "Systolic blood pressure out of expected physiological range.",
            Status: "OPEN",
            CreatedBy: "cra.monitor@example.com",
            CreatedAt: DateTime.UtcNow
        ));

    public static QueryDiscrepancyRecord Create(
        string? id = null,
        string? subjectId = null,
        string? fieldName = null,
        string? queryText = null,
        string? status = null,
        string? createdBy = null,
        DateTime? createdAt = null)
    {
        var baseRecord = FakerInstance.Generate();
        return baseRecord with
        {
            Id = id ?? baseRecord.Id,
            SubjectId = subjectId ?? baseRecord.SubjectId,
            FieldName = fieldName ?? baseRecord.FieldName,
            QueryText = queryText ?? baseRecord.QueryText,
            Status = status ?? baseRecord.Status,
            CreatedBy = createdBy ?? baseRecord.CreatedBy,
            CreatedAt = createdAt ?? baseRecord.CreatedAt
        };
    }
}

public static class ConsentRecordFactory
{
    private static readonly Faker<ConsentRecord> FakerInstance = new Faker<ConsentRecord>()
        .CustomInstantiator(f => new ConsentRecord(
            Id: $"CONSENT-{f.Random.Hexadecimal(6, "").ToUpperInvariant()}",
            SubjectPseudonym: "SUBJ-001",
            StudyId: "study_oncology_phase3",
            SiteId: "SITE-101",
            ProtocolVersion: "1.0",
            SignatureManifest: new Dictionary<string, object?>
            {
                ["algorithm"] = "ES256",
                ["signature_type"] = "ELECTRONIC"
            },
            CreatedAt: DateTime.UtcNow
        ));

    public static ConsentRecord Create(
        string? id = null,
        string? subjectPseudonym = null,
        string? studyId = null,
        string? siteId = null,
        string? protocolVersion = null,
        IReadOnlyDictionary<string, object?>? signatureManifest = null,
        DateTime? createdAt = null)
    {
        var baseRecord = FakerInstance.Generate();
        return baseRecord with
        {
            Id = id ?? baseRecord.Id,
            SubjectPseudonym = subjectPseudonym ?? baseRecord.SubjectPseudonym,
            StudyId = studyId ?? baseRecord.StudyId,
            SiteId = siteId ?? baseRecord.SiteId,
            ProtocolVersion = protocolVersion ?? baseRecord.ProtocolVersion,
            SignatureManifest = signatureManifest ?? baseRecord.SignatureManifest,
            CreatedAt = createdAt ?? baseRecord.CreatedAt
        };
    }
}

public static class DocumentMetadataFactory
{
    private static readonly Faker<DocumentMetadataRecord> FakerInstance = new Faker<DocumentMetadataRecord>()
        .CustomInstantiator(f => new DocumentMetadataRecord(
            Id: $"DOC-{f.Random.Hexadecimal(6, "").ToUpperInvariant()}",
            StudyId: "study_oncology_phase3",
            SiteId: "SITE-101",
            Zone: "01_TRIAL_MANAGEMENT",
            Filename: "Protocol_Signature_Page_v1.pdf",
            VersionIndex: 1,
            CreatedAt: DateTime.UtcNow
        ));

    public static DocumentMetadataRecord Create(
        string? id = null,
        string? studyId = null,
        string? siteId = null,
        string? zone = null,
        string? filename = null,
        long? versionIndex = null,
        DateTime? createdAt = null)
    {
        var baseRecord = FakerInstance.Generate();
        return baseRecord with
        {
            Id = id ?? baseRecord.Id,
            StudyId = studyId ?? baseRecord.StudyId,
            SiteId = siteId, // allows explicit null
            Zone = zone ?? baseRecord.Zone,
            Filename = filename ?? baseRecord.Filename,
            VersionIndex = versionIndex ?? baseRecord.VersionIndex,
            CreatedAt = createdAt ?? baseRecord.CreatedAt
        };
    }
}

public static class AuditLogFactory
{
    private static readonly Faker<AuditLogRecord> FakerInstance = new Faker<AuditLogRecord>()
        .CustomInstantiator(f => new AuditLogRecord(
            Id: $"AUDIT-{f.Random.Hexadecimal(8, "").ToLowerInvariant()}",
            UserId: "site.crc@example.com",
            Action: "UPDATE_OBSERVATION",
            ResourceType: "ClinicalObservation",
            ResourceId: "OBS-001",
            ReasonForChange: "Correcting transcription error from source paper worksheet",
            VersionIndex: 2,
            Timestamp: DateTime.UtcNow
        ));

    public static AuditLogRecord Create(
        string? id = null,
        string? userId = null,
        string? action = null,
        string? resourceType = null,
        string? resourceId = null,
        string? reasonForChange = null,
        long? versionIndex = null,
        DateTime? timestamp = null)
    {
        var baseRecord = FakerInstance.Generate();
        return baseRecord with
        {
            Id = id ?? baseRecord.Id,
            UserId = userId ?? baseRecord.UserId,
            Action = action ?? baseRecord.Action,
            ResourceType = resourceType ?? baseRecord.ResourceType,
            ResourceId = resourceId ?? baseRecord.ResourceId,
            ReasonForChange = reasonForChange ?? baseRecord.ReasonForChange,
            VersionIndex = versionIndex ?? baseRecord.VersionIndex,
            Timestamp = timestamp ?? baseRecord.Timestamp
        };
    }
}
