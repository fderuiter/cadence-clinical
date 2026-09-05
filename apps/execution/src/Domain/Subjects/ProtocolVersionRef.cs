namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Embedded reference to the protocol version a subject consented under (legacy
/// <c>protocol_version</c>/<c>version_tag</c>/<c>version_index</c> columns). EF Core Complex Type,
/// no own PK — mapped in `MIG-SUBJ-012`.
/// @req:PRD-SUB-001
/// </summary>
public sealed record ProtocolVersionRef(StudyCode StudyCode, string VersionTag, int VersionIndex);
