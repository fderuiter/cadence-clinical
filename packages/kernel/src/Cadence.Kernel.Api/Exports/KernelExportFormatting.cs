using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Cadence.Kernel.Api;

internal static class KernelExportFormatting
{
    public const string ProducerName = "Cadence.Kernel.Api/CasebookExporter/1.0";
    public const string OdmNamespace = "http://www.cdisc.org/ns/odm/v1.3";
    public const string CadenceNamespace = "https://cadence.clinical/ns/kernel-export/v1";

    public static readonly JsonSerializerOptions CasebookJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static string EscapeCsvField(string value)
    {
        if (value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    public static string SanitizeOid(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "item";
        }

        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_').ToArray();
        return new string(chars);
    }

    public static string FormatInstant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string Html(string text) => WebUtility.HtmlEncode(text);

    public static string SanitizeFileName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        return chars.Length == 0 ? "export" : new string(chars);
    }

    public static string SanitizeIdentifierToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "records";
        }

        var chars = raw
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var cleaned = string.Join('_', new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(cleaned))
        {
            return "records";
        }

        return char.IsDigit(cleaned[0]) ? $"c_{cleaned}" : cleaned;
    }

    public static string SanitizeBundleToken(string raw)
    {
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        return chars.Length == 0 ? "item" : new string(chars);
    }

    public static void TryCopyCompiledMetaDataChildren(
        XmlWriter xml,
        string compiledOdmXml,
        HashSet<string> writtenItemOids,
        HashSet<string>? writtenFormOids = null,
        HashSet<string>? writtenItemGroupOids = null,
        HashSet<string>? writtenEventOids = null)
    {
        try
        {
            var doc = XDocument.Parse(compiledOdmXml);
            var ns = XNamespace.Get(OdmNamespace);
            var mdv = doc.Descendants(ns + "MetaDataVersion").FirstOrDefault();
            if (mdv is null)
            {
                return;
            }

            foreach (var element in mdv.Elements())
            {
                var oidAttr = element.Attribute("OID")?.Value;
                if (oidAttr is not null)
                {
                    switch (element.Name.LocalName)
                    {
                        case "ItemDef":
                            writtenItemOids.Add(oidAttr);
                            break;
                        case "FormDef":
                            writtenFormOids?.Add(oidAttr);
                            break;
                        case "ItemGroupDef":
                            writtenItemGroupOids?.Add(oidAttr);
                            break;
                        case "StudyEventDef":
                            writtenEventOids?.Add(oidAttr);
                            break;
                    }
                }

                element.WriteTo(xml);
            }
        }
        catch (XmlException)
        {
            // Ignore malformed precompiled XML and fall back to row-synthesized ItemDefs.
        }
    }

    public static string? TryCompileUsdmOdmViaReflection(string rawContent, string studyCode, string versionTag)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        try
        {
            var ddfAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Cadence.Ddf")
                ?? System.Reflection.Assembly.Load("Cadence.Ddf");
            var compilerType = ddfAssembly?.GetType("Cadence.Ddf.UsdmCompiler");
            if (compilerType is null)
            {
                return null;
            }

            var methods = compilerType.GetMethods().Where(m => m.Name == "CompileUsdm").ToList();
            var target = methods.FirstOrDefault(m => m.GetParameters().Length == 6);
            if (target is null)
            {
                return null;
            }

            var compilation = target.Invoke(null, [rawContent, studyCode, versionTag, null, null, null]);
            return compilation?.GetType().GetProperty("OdmXml")?.GetValue(compilation) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
