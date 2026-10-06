using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cadence.Apps.Designer.Persistence;

/// <summary>
/// Database entity storing CRDT binary state vectors alongside JSON schema documents
/// to preserve REST API compatibility and full collaboration history.
/// </summary>
[Table("ecrf_form_snapshots", Schema = "designer")]
public class EcrfFormSnapshot
{
    [Key]
    [MaxLength(128)]
    public string FormId { get; set; } = string.Empty;

    [MaxLength(128)]
    public string StudyId { get; set; } = string.Empty;

    public string UsdmJson { get; set; } = string.Empty;

    public byte[] CrdtStateVector { get; set; } = Array.Empty<byte>();

    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string CompilationStatus { get; set; } = "Pending";

    public string? CompilationResultJson { get; set; }
}
