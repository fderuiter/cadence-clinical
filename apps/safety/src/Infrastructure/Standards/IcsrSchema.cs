using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace Cadence.Apps.Safety.Infrastructure.Standards;

/// <summary>
/// Validates an ICSR message against the ICH E2B(R3) schema: the HL7 v3 <c>MCCI_IN200100UV01</c> batch schema and the
/// schemas it includes, as published in the ICH E2B(R3) implementation package and vendored unchanged under
/// <c>apps/safety/Schemas/ich-e2b-r3</c> (embedded in this assembly). The schemas are read from the assembly only; no
/// include is ever fetched over the network. The schema checks structure, data types and HL7 vocabularies; the ICH
/// business rules (mandatory elements, code lists, precision) are checked before rendering by
/// <see cref="Domain.IcsrConformance"/>.
/// </summary>
public static class IcsrSchema
{
    /// <summary>The ICH E2B(R3) batch interaction the schema validates.</summary>
    public const string RootSchema = "multicacheschemas/MCCI_IN200100UV01.xsd";

    private const string ResourcePrefix = "IchE2bR3/";

    // A base URI that never resolves to a host (RFC 6761 .invalid), so relative includes map to embedded resources.
    private static readonly Uri BaseUri = new("https://ich-e2b-r3.invalid/"); // deid-ignore: a reserved .invalid host, not PHI

    // Compiled once and only read afterwards; a compiled XmlSchemaSet can be shared by concurrent validating readers.
    private static readonly Lazy<XmlSchemaSet> Schemas = new(Load);

    /// <summary>The schema errors of <paramref name="xml"/>, each with its line; empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
            {
                errors.Add($"line {e.Exception?.LineNumber}: {e.Message}");
            }
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
                // Reading the whole document raises a validation event for each schema error.
            }
        }
        catch (XmlException ex)
        {
            errors.Add($"line {ex.LineNumber}: {ex.Message}");
        }

        return errors;
    }

    private static XmlSchemaSet Load()
    {
        var set = new XmlSchemaSet { XmlResolver = new EmbeddedSchemaResolver() };
        var rootUri = new Uri(BaseUri, RootSchema);
        using (var stream = EmbeddedSchemaResolver.Open(rootUri))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }, rootUri.AbsoluteUri))
        {
            set.Add(null, reader);
        }

        set.Compile();
        return set;
    }

    /// <summary>Resolves schema includes to this assembly's embedded schema files and nothing else.</summary>
    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) => Open(absoluteUri);

        public static Stream Open(Uri absoluteUri)
        {
            if (!string.Equals(absoluteUri.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new XmlSchemaException($"The ICH E2B(R3) schema refers to {absoluteUri}, which is not part of the vendored schema.");
            }

            string path = absoluteUri.AbsolutePath.TrimStart('/');
            string resource = ResourcePrefix + path;
            Assembly assembly = typeof(IcsrSchema).Assembly;

            // %(RecursiveDir) in the logical name uses the build machine's directory separator.
            return assembly.GetManifestResourceStream(resource)
                ?? assembly.GetManifestResourceStream(ResourcePrefix + path.Replace('/', '\\'))
                ?? throw new XmlSchemaException($"The vendored ICH E2B(R3) schema file {resource} is not embedded.");
        }
    }
}
