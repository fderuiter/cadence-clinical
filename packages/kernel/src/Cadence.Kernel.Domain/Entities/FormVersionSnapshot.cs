using System;

using Cadence.Core.Domain;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// An immutable form version snapshot created upon protocol version approval (Requirement 1, 21 CFR Part 11).
/// Persists compilation results from <c>UsdmCompiler</c> including DDF IR, OpenRosa XML, and SHA-256 <c>FormHash</c>.
/// </summary>
public class FormVersionSnapshot : AggregateRoot<FormVersionSnapshotId>
{
    protected FormVersionSnapshot() { }

    public FormVersionSnapshot(
        FormVersionSnapshotId id,
        TenantId tenantId,
        StudyId studyId,
        ProtocolVersionId protocolVersionId,
        string protocolVersion,
        string formId,
        string formHash,
        string openRosaXml,
        string ddfIr,
        string? odmXml = null,
        DateTimeOffset recordedAt = default)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        ProtocolVersionId = protocolVersionId;
        ProtocolVersion = protocolVersion;
        FormId = formId;
        FormHash = formHash;
        OpenRosaXml = openRosaXml;
        DdfIr = ddfIr;
        OdmXml = odmXml;
        RecordedAt = recordedAt;
    }

    public TenantId TenantId { get; private set; }

    public StudyId StudyId { get; private set; }

    public ProtocolVersionId ProtocolVersionId { get; private set; }

    public string ProtocolVersion { get; private set; } = string.Empty;

    public string FormId { get; private set; } = string.Empty;

    public string FormHash { get; private set; } = string.Empty;

    public string OpenRosaXml { get; private set; } = string.Empty;

    public string DdfIr { get; private set; } = string.Empty;

    public string? OdmXml { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
