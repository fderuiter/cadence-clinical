using System;

namespace Cadence.Apps.Execution.Domain.Forms;

/// <summary>
/// Registry of published form schemas for studies and protocol versions.
/// Serves as the execution engine's copy of kernel form version snapshots.
/// </summary>
public sealed class PublishedFormVersionRegistry
{
    public Guid Id { get; private set; }
    public string TenantId { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string ProtocolVersionId { get; private set; } = string.Empty;
    public string ProtocolVersion { get; private set; } = string.Empty;
    public string FormId { get; private set; } = string.Empty;
    public string FormHash { get; private set; } = string.Empty;
    public string OpenRosaXml { get; private set; } = string.Empty;
    public string DdfIr { get; private set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; private set; }

    private PublishedFormVersionRegistry() { } // EF Core

    public PublishedFormVersionRegistry(
        Guid id,
        string tenantId,
        string studyId,
        string protocolVersionId,
        string protocolVersion,
        string formId,
        string formHash,
        string openRosaXml,
        string ddfIr,
        DateTimeOffset publishedAt)
    {
        Id = id;
        TenantId = tenantId;
        StudyId = studyId;
        ProtocolVersionId = protocolVersionId;
        ProtocolVersion = protocolVersion;
        FormId = formId;
        FormHash = formHash;
        OpenRosaXml = openRosaXml;
        DdfIr = ddfIr;
        PublishedAt = publishedAt;
    }

    public void Update(
        string formHash,
        string openRosaXml,
        string ddfIr,
        DateTimeOffset publishedAt)
    {
        FormHash = formHash;
        OpenRosaXml = openRosaXml;
        DdfIr = ddfIr;
        PublishedAt = publishedAt;
    }
}
