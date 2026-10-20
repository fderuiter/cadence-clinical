using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Cadence.Ddf;

/// <summary>
/// The compiler's built-in accessibility check: a structural WCAG 2.1 review of an OpenRosa XForm. It needs no
/// browser, so it runs on every compile. Rule ids follow axe-core where one exists.
/// </summary>
/// <remarks>
/// Violations (WCAG):
/// <list type="bullet">
/// <item><c>document-title</c> (2.4.2): the form has a non-empty <c>h:title</c>.</item>
/// <item><c>label</c> (1.3.1, 3.3.2, 4.1.2): every control has a label with text or an itext reference.</item>
/// <item><c>label-language</c> (3.1.2): when labels carry <c>xml:lang</c>, every control is labelled in every
/// language the form uses.</item>
/// <item><c>select-option-name</c> (4.1.2): every choice of a select has a label.</item>
/// </list>
/// Layout errors: the form is not well-formed XML or has no body, a control has no <c>ref</c>, a control's
/// <c>ref</c> has no <c>bind</c>, or two controls bind the same node.
/// </remarks>
public sealed class XFormsAccessibilityValidator : IAccessibilityValidator
{
    private const int MaxSnippetLength = 200;

    private static readonly XNamespace XForms = "http://www.w3.org/2002/xforms";
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    private static readonly HashSet<string> ControlNames = new(StringComparer.Ordinal)
    {
        "input", "select", "select1", "textarea", "upload", "range", "trigger", "secret", "rank",
    };

    private static readonly (string Id, string Description)[] Rules =
    [
        ("document-title", "The form has a non-empty title (WCAG 2.4.2)."),
        ("label", "Every form control has a label (WCAG 1.3.1, 3.3.2, 4.1.2)."),
        ("label-language", "Every form control is labelled in every language the form uses (WCAG 3.1.2)."),
        ("select-option-name", "Every choice of a select has a label (WCAG 4.1.2)."),
    ];

    /// <summary>The shared instance; the validator holds no state.</summary>
    public static XFormsAccessibilityValidator Instance { get; } = new();

    /// <inheritdoc />
    public AccessibilityReport Validate(string openRosaXml)
    {
        ArgumentNullException.ThrowIfNull(openRosaXml);

        XDocument document;
        try
        {
            document = Parse(openRosaXml);
        }
        catch (XmlException ex)
        {
            return Failed([$"The form is not well-formed XML: {ex.Message}"]);
        }

        XElement? body = document.Root?.Element(Xhtml + "body");
        if (document.Root?.Name != Xhtml + "html" || body is null)
        {
            return Failed(["The form has no h:html root with an h:body."]);
        }

        var controls = body.Descendants().Where(e => e.Name.Namespace == XForms && ControlNames.Contains(e.Name.LocalName)).ToList();
        var violations = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
        var applicable = new HashSet<string>(StringComparer.Ordinal) { "document-title" };

        string? title = document.Root.Element(Xhtml + "head")?.Element(Xhtml + "title")?.Value;
        if (string.IsNullOrWhiteSpace(title))
        {
            Add(violations, "document-title", Node(document.Root.Element(Xhtml + "head") ?? document.Root, "h:title"));
        }

        CheckLabels(controls, violations, applicable);
        CheckChoices(controls, violations, applicable);
        var layoutErrors = CheckBindings(document.Root, controls);

        var violationList = Rules
            .Where(r => violations.ContainsKey(r.Id))
            .Select(r => Rule(r.Id, r.Description, violations[r.Id]))
            .ToList();
        var passes = Rules
            .Where(r => applicable.Contains(r.Id) && !violations.ContainsKey(r.Id))
            .Select(r => Rule(r.Id, r.Description, []))
            .ToList();
        var inapplicable = Rules
            .Where(r => !applicable.Contains(r.Id))
            .Select(r => Rule(r.Id, r.Description, []))
            .ToList();

        return new AccessibilityReport(
            Status: violationList.Count == 0 && layoutErrors.Count == 0 ? AccessibilityStatus.Pass : AccessibilityStatus.Fail,
            Violations: violationList,
            Passes: passes,
            Incomplete: [],
            Inapplicable: inapplicable,
            LayoutErrors: layoutErrors);
    }

    private static void CheckLabels(
        List<XElement> controls,
        Dictionary<string, List<IReadOnlyDictionary<string, object?>>> violations,
        HashSet<string> applicable)
    {
        if (controls.Count == 0)
        {
            return;
        }

        applicable.Add("label");
        var languages = controls
            .SelectMany(c => c.Elements(XForms + "label"))
            .Select(Language)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (languages.Count > 0)
        {
            applicable.Add("label-language");
        }

        foreach (var control in controls)
        {
            var labels = control.Elements(XForms + "label").Where(HasName).ToList();
            if (labels.Count == 0)
            {
                Add(violations, "label", Node(control, Target(control)));
                continue;
            }

            var missing = languages.Except(labels.Select(Language).OfType<string>(), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                Add(violations, "label-language", Node(control, $"{Target(control)} (no label in {string.Join(", ", missing)})"));
            }
        }
    }

    private static void CheckChoices(
        List<XElement> controls,
        Dictionary<string, List<IReadOnlyDictionary<string, object?>>> violations,
        HashSet<string> applicable)
    {
        foreach (var choice in controls.Where(c => c.Name.LocalName is "select" or "select1" or "rank").SelectMany(c => c.Elements(XForms + "item")))
        {
            applicable.Add("select-option-name");
            if (!choice.Elements(XForms + "label").Any(HasName))
            {
                string value = choice.Element(XForms + "value")?.Value ?? "?";
                Add(violations, "select-option-name", Node(choice, $"{Target(choice.Parent!)} choice {value}"));
            }
        }
    }

    private static List<string> CheckBindings(XElement root, List<XElement> controls)
    {
        var errors = new List<string>();
        var binds = root.Descendants(XForms + "bind")
            .Select(b => (string?)b.Attribute("nodeset") ?? (string?)b.Attribute("ref"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var control in controls.Where(c => c.Name.LocalName != "trigger"))
        {
            string? reference = (string?)control.Attribute("ref") ?? (string?)control.Attribute("nodeset");
            if (string.IsNullOrWhiteSpace(reference))
            {
                errors.Add($"A {control.Name.LocalName} control has no ref: {Snippet(control)}");
                continue;
            }

            if (!binds.Contains(reference))
            {
                errors.Add($"The control {reference} has no bind.");
            }

            if (!seen.Add(reference))
            {
                errors.Add($"More than one control binds {reference}.");
            }
        }

        return errors;
    }

    private static XDocument Parse(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        return XDocument.Load(reader);
    }

    private static bool HasName(XElement label) => !string.IsNullOrWhiteSpace(label.Value) || label.Attribute("ref") is not null;

    private static string? Language(XElement label) => (string?)label.Attribute(XNamespace.Xml + "lang");

    private static string Target(XElement control) =>
        (string?)control.Attribute("ref") ?? (string?)control.Attribute("nodeset") ?? control.Name.LocalName;

    private static void Add(Dictionary<string, List<IReadOnlyDictionary<string, object?>>> violations, string rule, IReadOnlyDictionary<string, object?> node)
    {
        if (!violations.TryGetValue(rule, out var nodes))
        {
            nodes = [];
            violations[rule] = nodes;
        }

        nodes.Add(node);
    }

    private static Dictionary<string, object?> Node(XElement element, string target) =>
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["target"] = target, ["html"] = Snippet(element) };

    private static Dictionary<string, object?> Rule(string id, string description, List<IReadOnlyDictionary<string, object?>> nodes) =>
        new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = id, ["description"] = description, ["nodes"] = nodes };

    private static string Snippet(XElement element)
    {
        string text = element.ToString(SaveOptions.DisableFormatting);
        return text.Length <= MaxSnippetLength ? text : text[..MaxSnippetLength] + "…";
    }

    private static AccessibilityReport Failed(List<string> layoutErrors) =>
        new(Status: AccessibilityStatus.Fail, Violations: [], Passes: [], Incomplete: [], Inapplicable: [], LayoutErrors: layoutErrors);
}
