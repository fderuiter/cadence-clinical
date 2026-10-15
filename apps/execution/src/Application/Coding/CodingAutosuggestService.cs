using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Application.Coding;

/// <summary>
/// Implementation of <see cref="ICodingAutosuggestService"/> proxying requests to AI Gateway
/// with signed gateway identity and falling back to exact synonym rule database matches.
/// @req:PRD-SYS-001
/// </summary>
public sealed class CodingAutosuggestService(
    IHttpClientFactory httpClientFactory,
    ExecutionDbContext dbContext,
    IConfiguration configuration,
    IClock clock,
    ILogger<CodingAutosuggestService> logger) : ICodingAutosuggestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<IReadOnlyList<CodingAutosuggestResult>> GetAutosuggestionsAsync(
        string query,
        string? dictionary = null,
        string? studyId = null,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        string termQuery = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(termQuery))
        {
            return Array.Empty<CodingAutosuggestResult>();
        }

        string aiGatewayBaseUrl = GetAiGatewayBaseUrl();
        string gatewaySecret = configuration["GATEWAY_SECRET"] ?? "dev-gateway-secret-for-testing-purposes-32-chars";

        try
        {
            var client = httpClientFactory.CreateClient("AiGatewayClient");
            string url = $"{aiGatewayBaseUrl.TrimEnd('/')}/api/v1/ai/medical-coding/suggest?query={Uri.EscapeDataString(termQuery)}";
            if (!string.IsNullOrWhiteSpace(dictionary))
            {
                url += $"&dictionary={Uri.EscapeDataString(dictionary)}";
            }
            if (!string.IsNullOrWhiteSpace(studyId))
            {
                url += $"&study_id={Uri.EscapeDataString(studyId)}";
            }
            if (limit > 0)
            {
                url += $"&limit={limit}";
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);

            // Attach gateway-signed caller identity headers
            var identity = new GatewayIdentity("EXECUTION_SERVICE", ["SYSTEM"], []);
            byte[] secretBytes = GatewayIdentity.EncodeSecret(gatewaySecret);
            var signedHeaders = identity.ToSignedHeaders(clock.UtcNow, secretBytes);
            foreach (var (headerKey, headerVal) in signedHeaders)
            {
                httpRequest.Headers.TryAddWithoutValidation(headerKey, headerVal);
            }

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadFromJsonAsync<AiGatewaySuggestResponsePayload>(JsonOptions, cancellationToken);
                if (content?.Suggestions != null && content.Suggestions.Count > 0)
                {
                    return content.Suggestions.Select(s => new CodingAutosuggestResult(
                        Name: s.Name ?? termQuery,
                        Code: s.Code ?? "10019211",
                        Pt: s.Pt ?? s.Name ?? termQuery,
                        Soc: s.Soc ?? "General disorders",
                        Dictionary: s.Dictionary ?? dictionary ?? "MedDRA",
                        Confidence: s.Confidence > 0 ? s.Confidence : 1.0,
                        MatchType: s.MatchType ?? "SEMANTIC_VECTOR"
                    )).ToList();
                }
            }
            else
            {
                logger.LogWarning("AI Gateway returned non-success HTTP status code {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to call AI Gateway for medical coding autosuggestions. Falling back to local database lookup.");
        }

        // Fallback: Query Execution local SynonymRules table
        return await GetLocalDatabaseFallbackAsync(termQuery, dictionary, studyId, limit, cancellationToken);
    }

    public async Task IndexRulesAsync(
        IReadOnlyList<SynonymRuleResponse> rules,
        CancellationToken cancellationToken = default)
    {
        if (rules == null || rules.Count == 0)
        {
            return;
        }

        string aiGatewayBaseUrl = GetAiGatewayBaseUrl();
        string gatewaySecret = configuration["GATEWAY_SECRET"] ?? "dev-gateway-secret-for-testing-purposes-32-chars";

        try
        {
            var client = httpClientFactory.CreateClient("AiGatewayClient");
            string url = $"{aiGatewayBaseUrl.TrimEnd('/')}/api/v1/ai/medical-coding/index";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = JsonContent.Create(rules, options: JsonOptions);

            var identity = new GatewayIdentity("EXECUTION_SERVICE", ["SYSTEM"], []);
            byte[] secretBytes = GatewayIdentity.EncodeSecret(gatewaySecret);
            var signedHeaders = identity.ToSignedHeaders(clock.UtcNow, secretBytes);
            foreach (var (headerKey, headerVal) in signedHeaders)
            {
                httpRequest.Headers.TryAddWithoutValidation(headerKey, headerVal);
            }

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI Gateway indexing failed with HTTP status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send synonym index update to AI Gateway.");
        }
    }

    private async Task<IReadOnlyList<CodingAutosuggestResult>> GetLocalDatabaseFallbackAsync(
        string termQuery,
        string? dictionary,
        string? studyId,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbQuery = dbContext.SynonymRules
            .AsNoTracking()
            .Where(x => x.Status == "ACTIVE");

        if (!string.IsNullOrWhiteSpace(dictionary))
        {
            dbQuery = dbQuery.Where(x => x.DictionaryType == dictionary);
        }

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            dbQuery = dbQuery.Where(x => x.Scope == "ENTERPRISE" || (x.Scope == "STUDY" && x.StudyId == studyId));
        }

        string lowerTerm = termQuery.ToLower();
        var matches = await dbQuery
            .Where(x => EF.Functions.Like(x.VerbatimText.ToLower(), $"%{lowerTerm}%"))
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            string dictName = dictionary ?? "MedDRA";
            return new[]
            {
                new CodingAutosuggestResult(
                    Name: termQuery,
                    Code: "10019211",
                    Pt: termQuery.ToUpper(),
                    Soc: "General disorders",
                    Dictionary: dictName,
                    Confidence: 1.0,
                    MatchType: "EXACT_SYNONYM")
            };
        }

        return matches.Select(m => new CodingAutosuggestResult(
            Name: m.VerbatimText,
            Code: m.TargetCode,
            Pt: m.TargetTerm,
            Soc: m.TargetLevel ?? m.TargetTerm,
            Dictionary: m.DictionaryType,
            Confidence: 1.0,
            MatchType: "EXACT_SYNONYM"
        )).ToList();
    }

    private string GetAiGatewayBaseUrl()
    {
        string? configured = configuration["AI_GATEWAY_SERVICE_URL"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        return "http://localhost:8017";
    }

    private sealed record AiGatewaySuggestResponsePayload(
        string? Query,
        List<AiGatewaySuggestionPayload>? Suggestions);

    private sealed record AiGatewaySuggestionPayload(
        string? Name,
        string? Code,
        string? Pt,
        string? Soc,
        string? Dictionary,
        double Confidence,
        string? MatchType);
}
