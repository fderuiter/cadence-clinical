using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Modules.Intelligence.Inference;

/// <summary>Where a supplier stands in the sponsor's assessment (EU GMP Annex 11 §3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SupplierStatus>))]
public enum SupplierStatus
{
    /// <summary>Not yet assessed. Usable only in Development.</summary>
    Draft,

    /// <summary>Assessed and approved by the sponsor's quality unit.</summary>
    Approved,

    /// <summary>Approval withdrawn; refused everywhere.</summary>
    Suspended,
}

/// <summary>
/// The supplier file for one inference provider (ADR-2636 §4.5). Shipped as <see cref="SupplierStatus.Draft"/> with the
/// assessment questions open; the sponsor's quality unit fills them and approves, through a pull request or a
/// deployment overlay. Cadence never states a supplier's certifications on the sponsor's behalf.
/// </summary>
public sealed record SupplierRecord
{
    /// <summary>The record id providers name.</summary>
    public required string Id { get; init; }

    /// <summary>Increases with every change to the record.</summary>
    public required int Version { get; init; }

    /// <summary>The contracting supplier.</summary>
    public required string Supplier { get; init; }

    /// <summary>Who operates the service.</summary>
    public required string OperatedBy { get; init; }

    /// <summary>The service supplied.</summary>
    public required string Service { get; init; }

    /// <summary>The host pattern the service is reached on (HTTPS only).</summary>
    public required string Endpoint { get; init; }

    /// <summary>Free-text notes on the service.</summary>
    public string? Notes { get; init; }

    /// <summary>The assessment status.</summary>
    public required SupplierStatus Status { get; init; }

    /// <summary>The data-processing terms in force (DPA, BAA), as the assessor records them.</summary>
    public string? DataProcessingTerms { get; init; }

    /// <summary>Whether customer data trains the supplier's models; null until assessed.</summary>
    public bool? TrainsOnCustomerData { get; init; }

    /// <summary>Retention of prompts and outputs, as assessed.</summary>
    public string? Retention { get; init; }

    /// <summary>The regions the contract allows.</summary>
    public IReadOnlyList<string> RegionsOffered { get; init; } = [];

    /// <summary>Certifications the supplier states, as the assessor verified them.</summary>
    public IReadOnlyList<string> StatedCertifications { get; init; } = [];

    /// <summary>When the assessment was made.</summary>
    public DateOnly? AssessedOn { get; init; }

    /// <summary>Who made it.</summary>
    public string? AssessedBy { get; init; }

    /// <summary>When the next periodic review is due.</summary>
    public DateOnly? NextReviewOn { get; init; }

    /// <summary>What the assessment must answer.</summary>
    public IReadOnlyList<string> AssessmentQuestions { get; init; } = [];

    /// <summary>True when the periodic review is past due on <paramref name="today"/>; a finding, not a refusal.</summary>
    public bool IsReviewOverdue(DateOnly today) => Status == SupplierStatus.Approved && NextReviewOn is { } due && due < today;
}

/// <summary>
/// The supplier records: the ones shipped with the module, with a deployment's overlay applied
/// (<c>Intelligence:Inference:Suppliers:&lt;id&gt;</c>).
/// </summary>
public interface ISupplierCatalog
{
    /// <summary>The record with <paramref name="id"/>, or null.</summary>
    SupplierRecord? Find(string id);

    /// <summary>Every record.</summary>
    IReadOnlyCollection<SupplierRecord> All { get; }
}

/// <summary>A deployment's assessment of one supplier, overriding the shipped record's assessment fields.</summary>
public sealed class SupplierOverlayOptions
{
    /// <summary>The status the quality unit set.</summary>
    public SupplierStatus? Status { get; set; }

    /// <summary>Who assessed. Required with an <see cref="SupplierStatus.Approved"/> status.</summary>
    public string? AssessedBy { get; set; }

    /// <summary>When. Required with an <see cref="SupplierStatus.Approved"/> status.</summary>
    public DateOnly? AssessedOn { get; set; }

    /// <summary>The next periodic review. Required with an <see cref="SupplierStatus.Approved"/> status.</summary>
    public DateOnly? NextReviewOn { get; set; }

    /// <summary>The data-processing terms in force.</summary>
    public string? DataProcessingTerms { get; set; }

    /// <summary>Whether customer data trains the supplier's models.</summary>
    public bool? TrainsOnCustomerData { get; set; }

    /// <summary>Retention of prompts and outputs.</summary>
    public string? Retention { get; set; }
}

/// <summary>Loads the records embedded in the assembly and applies the overlay; an approval without its assessment fails.</summary>
internal sealed class SupplierCatalog : ISupplierCatalog
{
    private const string ResourcePrefix = "Cadence.Modules.Intelligence.Inference.Suppliers.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly FrozenDictionary<string, SupplierRecord> _records;

    public SupplierCatalog(IReadOnlyDictionary<string, SupplierOverlayOptions> overlays)
    {
        var shipped = LoadShipped();
        if (overlays.Keys.FirstOrDefault(id => !shipped.ContainsKey(id)) is { } unknown)
        {
            throw new InvalidOperationException($"{InferenceSettings.Suppliers}:{unknown} overlays a supplier record that does not exist.");
        }

        _records = shipped.Values
            .Select(r => overlays.TryGetValue(r.Id, out var overlay) ? Apply(r, overlay) : r)
            .ToFrozenDictionary(r => r.Id, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<SupplierRecord> All => _records.Values;

    public SupplierRecord? Find(string id) => _records.GetValueOrDefault(id);

    /// <summary>The records shipped as embedded <c>Inference/Suppliers/*.json</c> resources.</summary>
    internal static Dictionary<string, SupplierRecord> LoadShipped()
    {
        var assembly = typeof(SupplierCatalog).Assembly;
        var records = new Dictionary<string, SupplierRecord>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException(name);
            var record = JsonSerializer.Deserialize<SupplierRecord>(stream, Json) ?? throw new InvalidDataException($"{name} is empty.");
            records.Add(record.Id, record);
        }

        return records;
    }

    private static SupplierRecord Apply(SupplierRecord record, SupplierOverlayOptions overlay)
    {
        var status = overlay.Status ?? record.Status;
        var applied = record with
        {
            Status = status,
            AssessedBy = overlay.AssessedBy ?? record.AssessedBy,
            AssessedOn = overlay.AssessedOn ?? record.AssessedOn,
            NextReviewOn = overlay.NextReviewOn ?? record.NextReviewOn,
            DataProcessingTerms = overlay.DataProcessingTerms ?? record.DataProcessingTerms,
            TrainsOnCustomerData = overlay.TrainsOnCustomerData ?? record.TrainsOnCustomerData,
            Retention = overlay.Retention ?? record.Retention,
        };
        if (status == SupplierStatus.Approved
            && (string.IsNullOrWhiteSpace(applied.AssessedBy) || applied.AssessedOn is null || applied.NextReviewOn is null))
        {
            throw new InvalidOperationException(
                $"{InferenceSettings.Suppliers}:{record.Id}: an approval needs AssessedBy, AssessedOn and NextReviewOn.");
        }

        return applied;
    }
}
