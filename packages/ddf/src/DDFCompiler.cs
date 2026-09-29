using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Ddf.Stages;

namespace Cadence.Ddf;

[SuppressMessage("SonarAnalyzer.CSharp", "S101:Types should name in PascalCase", Justification = "DDF acronym naming convention")]
public class DDFCompiler
{
    private readonly USDMNormalizerStage _normalizer;
    private readonly DDFSemanticIRBuilderStage _irBuilder;
    private readonly XmlLayoutRendererStage _renderer;
    private readonly LayoutAccessibilityStage _accessibilityStage;

    public DDFCompiler(
        USDMNormalizerStage? normalizer = null,
        DDFSemanticIRBuilderStage? irBuilder = null,
        XmlLayoutRendererStage? renderer = null,
        LayoutAccessibilityStage? accessibilityStage = null,
        IAccessibilityValidatorPort? accessibilityValidator = null)
    {
        _normalizer = normalizer ?? new USDMNormalizerStage();
        _irBuilder = irBuilder ?? new DDFSemanticIRBuilderStage();
        _renderer = renderer ?? new XmlLayoutRendererStage();

        if (accessibilityStage != null)
        {
            if (accessibilityStage.Validator == null)
            {
                throw new ArgumentException(
                    "Accessibility validator port is uninitialized or missing. " +
                    "DDFCompiler requires an explicit AccessibilityValidatorPort instance.");
            }
            _accessibilityStage = accessibilityStage;
        }
        else if (accessibilityValidator != null)
        {
            _accessibilityStage = new LayoutAccessibilityStage(accessibilityValidator);
        }
        else
        {
            _accessibilityStage = new LayoutAccessibilityStage(new DefaultAccessibilityValidator());
        }
    }

    public USDMNormalizerStage Normalizer => _normalizer;
    public DDFSemanticIRBuilderStage IrBuilder => _irBuilder;
    public XmlLayoutRendererStage Renderer => _renderer;
    public LayoutAccessibilityStage AccessibilityStage => _accessibilityStage;

    public async Task<DDFCompilationResult> CompileAsync(
        string studyId,
        JsonNode payload,
        CompileOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CompileOptions();

        // Stage 1: Normalization
        var (normalizedPayload, compilerWarnings) = _normalizer.Normalize(studyId, payload.AsObject());

        // Stage 2 & 3: IR Builder & XPath compilation
        var studyIr = _irBuilder.BuildIr(studyId, normalizedPayload);

        // Stage 4: XML Layout Renderer
        var (odmXml, openRosaXml, formHash) = _renderer.Render(studyIr);

        // Stage 5: Accessibility Validation
        AccessibilityReport? accessibilityReport = null;
        if (options.ValidateAccessibility)
        {
            accessibilityReport = await _accessibilityStage.ValidateAsync(openRosaXml, formHash, cancellationToken);
        }

        var warningsList = compilerWarnings.Count > 0
            ? compilerWarnings.Select(w => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["field"] = w.Field,
                ["generated_value"] = w.GeneratedValue,
                ["warning_reason"] = w.WarningReason
            }).ToList()
            : null;

        return new DDFCompilationResult(
            StudyId: studyId,
            Name: studyIr.Name,
            OdmXml: odmXml,
            OpenRosaXml: openRosaXml,
            FormHash: formHash,
            Warnings: warningsList,
            AccessibilityReport: accessibilityReport,
            Status: "COMPLETED"
        );
    }

    public DDFCompilationResult Compile(
        string studyId,
        JsonNode payload,
        CompileOptions? options = null)
    {
        return CompileAsync(studyId, payload, options).GetAwaiter().GetResult();
    }
}
