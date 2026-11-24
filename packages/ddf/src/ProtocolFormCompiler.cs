using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;
using Cadence.Templating;

namespace Cadence.Ddf;

/// <summary>
/// Compiles a kernel <see cref="ProtocolDesign"/>'s <see cref="CollectionSpec"/> definitions into immutable
/// OpenRosa XForms XML, serialized <see cref="DDFStudyIR"/> JSON, and CDISC ODM v1.3.2 XML snapshots.
/// </summary>
public sealed partial class ProtocolFormCompiler : IProtocolFormCompiler
{
    private const string OdmTemplateKey = "odm";
    private const string OpenRosaTemplateKey = "openrosa";

    private static readonly JsonSerializerOptions IrJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ITemplateRenderer _templateRenderer;
    private readonly IAccessibilityValidator _accessibilityValidator;

    /// <summary>Initializes a new instance of <see cref="ProtocolFormCompiler"/>.</summary>
    public ProtocolFormCompiler(
        ITemplateRenderer? templateRenderer = null,
        IAccessibilityValidator? accessibilityValidator = null)
    {
        _templateRenderer = templateRenderer ?? FluidTemplateRenderer.Instance;
        _accessibilityValidator = accessibilityValidator ?? XFormsAccessibilityValidator.Instance;
    }

    /// <inheritdoc />
    public CompiledProtocolForm? Compile(ProtocolDesign design, string studyIdentifier, string versionTag)
    {
        ArgumentNullException.ThrowIfNull(design);

        if (design.CollectionSpecs.Count == 0)
        {
            return null;
        }

        var resolvedStudyId = FirstNonBlank(studyIdentifier, "STUDY-001")!;
        var resolvedVersion = FirstNonBlank(versionTag, "1")!;
        var resolvedTitle = FirstNonBlank(design.Title, "Protocol Design")!;

        var seenItemIds = new HashSet<string>(StringComparer.Ordinal);
        var usedElementNames = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<DDFItemIR>();

        foreach (var spec in design.CollectionSpecs)
        {
            foreach (var item in spec.Items)
            {
                if (!seenItemIds.Add(item.Id))
                {
                    continue;
                }

                var oid = $"IT.{SanitizeOid(item.Id)}";
                var baseElement = DdfIdentifier.Sanitize(item.Id) ?? $"item_{items.Count + 1}";
                var elementName = AllocateUniqueElementName(baseElement, usedElementNames);
                var xformType = MapItemType(item.ExpectedKind);
                var label = FirstNonBlank(
                    item.Prompt,
                    !string.IsNullOrWhiteSpace(spec.Name) ? $"{spec.Name} - {item.Id}" : null,
                    item.Id)!;
                var hint = item.Unit is not null ? $"Unit: {item.Unit}" : null;

                items.Add(new DDFItemIR(
                    Id: oid,
                    Name: elementName,
                    Type: xformType,
                    Localizations:
                    [
                        new ItemLocalization(
                            Language: "en",
                            Label: label,
                            Description: FirstNonBlank(spec.Name),
                            Hint: hint),
                    ]));
            }
        }

        if (items.Count == 0)
        {
            return null;
        }

        var studyIr = new DDFStudyIR(
            StudyId: resolvedStudyId,
            Name: resolvedTitle,
            Items: items,
            Version: resolvedVersion,
            Metadata: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schema_version"] = design.SchemaVersion,
                ["form_count"] = design.CollectionSpecs.Count,
                ["item_count"] = items.Count,
            });

        var odmXml = _templateRenderer.Render(UsdmCompiler.Templates, OdmTemplateKey, studyIr);
        var openRosaXml = _templateRenderer.Render(UsdmCompiler.Templates, OpenRosaTemplateKey, studyIr);
        var formHash = ComputeSha256Hash(openRosaXml);

        var report = _accessibilityValidator.Validate(openRosaXml) with { FormHash = formHash };
        if (!AccessibilityStatus.Passes(report))
        {
            throw new AccessibilityValidationException(report);
        }

        var ddfIrJson = JsonSerializer.Serialize(studyIr, IrJsonOptions);
        return new CompiledProtocolForm(
            FormId: resolvedStudyId,
            FormHash: formHash,
            OpenRosaXml: openRosaXml,
            DdfIrJson: ddfIrJson,
            OdmXml: odmXml);
    }

    private static string MapItemType(ObservationValueKind? kind) => kind switch
    {
        ObservationValueKind.Quantity => "decimal",
        ObservationValueKind.Boolean => "boolean",
        ObservationValueKind.DateTime => "dateTime",
        _ => "string",
    };

    private static string AllocateUniqueElementName(string baseElement, HashSet<string> usedNames)
    {
        if (usedNames.Add(baseElement))
        {
            return baseElement;
        }

        int suffix = 2;
        while (true)
        {
            var candidate = $"{baseElement}_{suffix}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static string SanitizeOid(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "UNKNOWN";
        }

        var cleaned = InvalidOidCharsRegex().Replace(raw.Trim(), "_").Trim('_');
        return cleaned.Length == 0 ? "UNKNOWN" : cleaned;
    }

    private static string ComputeSha256Hash(string rawData) => ContentHash.OfCanonical(rawData);

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    [GeneratedRegex("[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex InvalidOidCharsRegex();
}
