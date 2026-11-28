using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Kernel.Api.StudyImports;

internal static class KernelProtocolImportInputReader
{
    private static readonly FormOptions MultipartLimits = new()
    {
        MultipartBodyLengthLimit = IProtocolDesignImporter.MaxPayloadBytes + 262_144,
        ValueLengthLimit = IProtocolDesignImporter.MaxPayloadBytes,
    };

    internal static async Task<ParsedImportInput> ReadImportInputAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.HasFormContentType)
        {
            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(MultipartLimits, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return ParsedImportInput.Fail(PayloadTooLargeProblem());
            }
            catch (IOException ex)
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid multipart upload", ex.Message));
            }

            byte[]? rawBytes = null;
            string? fileName = null;
            if (form.Files.Count > 0)
            {
                var file = form.Files[0];
                fileName = file.FileName;
                if (file.Length > IProtocolDesignImporter.MaxPayloadBytes)
                {
                    return ParsedImportInput.Fail(PayloadTooLargeProblem());
                }

                await using var stream = file.OpenReadStream();
                var (bytes, exceeded) = await ReadBoundedBytesAsync(stream, IProtocolDesignImporter.MaxPayloadBytes, cancellationToken).ConfigureAwait(false);
                if (exceeded)
                {
                    return ParsedImportInput.Fail(PayloadTooLargeProblem());
                }

                rawBytes = bytes;
            }

            if (!TryParseImportFormat(form["format"].ToString(), out var format))
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest(
                    "Invalid format",
                    "Supported format values are 'auto', 'odm_xml', and 'usdm_json'."));
            }

            var jsonOptionsForForm = context.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions;
            var templateIds = ParseTemplateIds(form["template_ids"]);
            var templateAssignments = ParseTemplateAssignments(form["template_assignments"], jsonOptionsForForm, out var assignmentError);
            if (assignmentError is not null)
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid template_assignments", assignmentError));
            }

            var replaceForms = bool.TryParse(form["replace_forms"].ToString(), out var parsedReplace) && parsedReplace;
            if (!TryParseAmendmentClassification(form["amendment_classification"].ToString(), out var formClassification))
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest(
                    "Invalid amendment classification",
                    "Supported amendment_classification values are 'Substantial', 'NonSubstantial', and 'UrgentSafetyMeasure'."));
            }

            if (!TryParseTransitionPolicy(form["transition_policy"].ToString(), out var formPolicy))
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest(
                    "Invalid transition policy",
                    "Supported transition_policy values are 'NewParticipantsOnly', 'RequiresReconsent', 'Automatic', and 'ByCohort'."));
            }

            return new ParsedImportInput(
                rawBytes,
                fileName,
                format,
                templateIds,
                replaceForms,
                templateAssignments,
                formClassification,
                formPolicy,
                null);
        }

        var (bodyBytes, bodyExceeded) = await ReadBoundedBytesAsync(
            context.Request.Body,
            IProtocolDesignImporter.MaxPayloadBytes,
            cancellationToken).ConfigureAwait(false);
        if (bodyExceeded)
        {
            return ParsedImportInput.Fail(PayloadTooLargeProblem());
        }

        if (bodyBytes.Length == 0)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Empty import request",
                "Upload an ODM v1.3.2 XML or USDM v4 JSON file, or specify bundled template_ids."));
        }

        var jsonOptions = context.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions;
        KernelImportProtocolJsonRequest? jsonRequest;
        try
        {
            jsonRequest = JsonSerializer.Deserialize<KernelImportProtocolJsonRequest>(bodyBytes, jsonOptions);
        }
        catch (JsonException ex)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid JSON request", ex.Message));
        }

        if (jsonRequest is null)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid JSON request", "Request body could not be parsed."));
        }

        if (!TryParseImportFormat(jsonRequest.Format, out var jsonFormat))
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Invalid format",
                "Supported format values are 'auto', 'odm_xml', and 'usdm_json'."));
        }

        if (!TryParseAmendmentClassification(jsonRequest.AmendmentClassification, out var jsonClassification))
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Invalid amendment classification",
                "Supported amendment_classification values are 'Substantial', 'NonSubstantial', and 'UrgentSafetyMeasure'."));
        }

        if (!TryParseTransitionPolicy(jsonRequest.TransitionPolicy, out var jsonPolicy))
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Invalid transition policy",
                "Supported transition_policy values are 'NewParticipantsOnly', 'RequiresReconsent', 'Automatic', and 'ByCohort'."));
        }

        byte[]? contentBytes = null;
        if (!string.IsNullOrEmpty(jsonRequest.Content))
        {
            contentBytes = Encoding.UTF8.GetBytes(jsonRequest.Content);
            if (contentBytes.Length > IProtocolDesignImporter.MaxPayloadBytes)
            {
                return ParsedImportInput.Fail(PayloadTooLargeProblem());
            }
        }

        var mappedAssignments = jsonRequest.TemplateAssignments?
            .Where(a => !string.IsNullOrWhiteSpace(a.TemplateId))
            .Select(a => new TemplateActivityAssignment(a.TemplateId.Trim(), a.ActivityIds ?? []))
            .ToList();

        return new ParsedImportInput(
            contentBytes,
            jsonRequest.FileName,
            jsonFormat,
            jsonRequest.TemplateIds,
            jsonRequest.ReplaceForms,
            mappedAssignments,
            jsonClassification,
            jsonPolicy,
            null);
    }

    internal static ProblemHttpResult PayloadTooLargeProblem(string? detail = null) =>
        TypedResults.Problem(
            title: "Payload too large",
            detail: detail ?? $"Uploaded payload exceeds the 10 MB ({IProtocolDesignImporter.MaxPayloadBytes:N0} bytes) synchronous import limit.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    internal static ProblemHttpResult ValidationFailedProblem(IReadOnlyList<string> problems)
    {
        var detail = problems.Count > 0
            ? string.Join(" ", problems)
            : "The uploaded protocol definition failed structural validation.";
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = ProtocolErrors.ContentInvalid.Code,
            ["errors"] = problems.ToArray(),
        };
        return TypedResults.Problem(
            title: "Protocol import validation failed",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: extensions);
    }

    internal static string ComputeImportRequestFingerprint(StudyId studyId, ParsedImportInput input)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var normalizedTemplates = input.TemplateIds is { Count: > 0 }
            ? string.Join(",", input.TemplateIds.Select(t => t.Trim()))
            : string.Empty;
        var normalizedAssignments = input.TemplateAssignments is { Count: > 0 }
            ? string.Join(
                ";",
                input.TemplateAssignments
                    .OrderBy(a => a.TemplateId, StringComparer.OrdinalIgnoreCase)
                    .Select(a => $"{a.TemplateId.Trim()}={string.Join(",", a.ActivityIds.Select(id => id.Trim()))}"))
            : string.Empty;
        var metadata = string.Join(
            "\n",
            studyId.ToString(),
            input.Format.ToString(),
            input.FileName ?? string.Empty,
            normalizedTemplates,
            normalizedAssignments,
            input.ReplaceForms ? "replace:1" : "replace:0",
            input.AmendmentClassification?.ToString() ?? string.Empty,
            input.TransitionPolicy?.ToString() ?? string.Empty);
        sha.AppendData(Encoding.UTF8.GetBytes(metadata));
        sha.AppendData("\npayload:"u8);
        if (input.RawBytes is { Length: > 0 } raw)
        {
            sha.AppendData(raw);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static async Task<(byte[] Bytes, bool ExceededLimit)> ReadBoundedBytesAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return ([], true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return (buffer.ToArray(), false);
    }

    private static bool TryParseImportFormat(string? raw, out ProtocolImportFormat format)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            format = ProtocolImportFormat.Auto;
            return true;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "odm":
            case "odm_xml":
            case "odmxml":
            case "xml":
                format = ProtocolImportFormat.OdmXml;
                return true;

            case "usdm":
            case "usdm_json":
            case "usdmjson":
            case "json":
                format = ProtocolImportFormat.UsdmJson;
                return true;

            default:
                format = ProtocolImportFormat.Auto;
                return false;
        }
    }

    private static List<string>? ParseTemplateIds(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var entry in values)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var trimmed = entry.Trim();
            if (trimmed.StartsWith('['))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                    if (parsed is not null)
                    {
                        list.AddRange(parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // Fall through to comma-splitting.
                }
            }

            foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                list.Add(part);
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static List<TemplateActivityAssignment>? ParseTemplateAssignments(
        Microsoft.Extensions.Primitives.StringValues values,
        JsonSerializerOptions? jsonOptions,
        out string? error)
    {
        error = null;
        if (values.Count == 0)
        {
            return null;
        }

        var list = new List<TemplateActivityAssignment>();
        foreach (var entry in values)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<KernelTemplateActivityAssignmentRequest>>(entry.Trim(), jsonOptions);
                if (parsed is not null)
                {
                    list.AddRange(parsed
                        .Where(a => !string.IsNullOrWhiteSpace(a.TemplateId))
                        .Select(a => new TemplateActivityAssignment(a.TemplateId.Trim(), a.ActivityIds ?? [])));
                }
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static bool TryParseAmendmentClassification(string? raw, out AmendmentClassification? classification)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            classification = null;
            return true;
        }

        var normalized = raw.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<AmendmentClassification>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            classification = parsed;
            return true;
        }

        classification = null;
        return false;
    }

    private static bool TryParseTransitionPolicy(string? raw, out TransitionPolicy? policy)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            policy = null;
            return true;
        }

        var normalized = raw.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<TransitionPolicy>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            policy = parsed;
            return true;
        }

        policy = null;
        return false;
    }
}
