using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.ToolingCore.Services;

public static partial class DemoManifestGeneratorService
{
    private static Dictionary<string, RealmUserSeed> ParseRealmUsers(string realmJson, List<string> issues)
    {
        var result = new Dictionary<string, RealmUserSeed>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(realmJson);
        if (!doc.RootElement.TryGetProperty("users", out var usersEl) || usersEl.ValueKind != JsonValueKind.Array)
        {
            issues.Add($"'{RealmRelativePath}' does not declare a top-level 'users' array.");
            return result;
        }

        foreach (var userEl in usersEl.EnumerateArray())
        {
            string username = GetString(userEl, "username");
            string sub = GetString(userEl, "id");
            string email = GetString(userEl, "email");
            string firstName = GetString(userEl, "firstName");
            string lastName = GetString(userEl, "lastName");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sub))
            {
                issues.Add($"A user entry in '{RealmRelativePath}' is missing 'username' or 'id'.");
                continue;
            }

            var realmRoles = new List<string>();
            if (userEl.TryGetProperty("realmRoles", out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var roleEl in rolesEl.EnumerateArray())
                {
                    if (roleEl.ValueKind == JsonValueKind.String && roleEl.GetString() is { Length: > 0 } roleName)
                    {
                        realmRoles.Add(roleName);
                    }
                }
            }

            string tenantCode = "cadence-local";
            string? siteCode = null;
            IReadOnlyList<string> studyIds = [];
            if (userEl.TryGetProperty("attributes", out var attrsEl) && attrsEl.ValueKind == JsonValueKind.Object)
            {
                tenantCode = GetFirstAttribute(attrsEl, "tenant_id") ?? tenantCode;
                siteCode = GetFirstAttribute(attrsEl, "site_id");
                studyIds = GetAttributeList(attrsEl, "study_ids");
            }

            result[username] = new RealmUserSeed(
                Username: username,
                KeycloakSub: sub,
                Email: email,
                FirstName: firstName,
                LastName: lastName,
                TenantCode: tenantCode,
                AssignedSiteCode: siteCode,
                StudyIds: studyIds,
                RealmRoles: realmRoles);
        }

        return result;
    }

    private static (string Workflow, string WorkspacePersona) ResolveWorkflowAndWorkspace(
        string username,
        string kernelRoleCode,
        IReadOnlyList<string> realmRoles) =>
        kernelRoleCode switch
        {
            "DEMO_CRC" => ("coordinator", "site_crc"),
            "DEMO_INVESTIGATOR" => ("investigator", "site_investigator"),
            "DEMO_MONITOR" => ("monitor", "cra_monitor"),
            "DEMO_DATA_MANAGER" => ("data_manager", "data_manager"),
            "DEMO_DESIGNER" when realmRoles.Contains("Sponsor Admin", StringComparer.Ordinal) => ("designer_lead", "super_admin"),
            "DEMO_DESIGNER" => ("designer", "sponsor_designer"),
            "DEMO_SAFETY" => ("safety", "safety_officer"),
            "DEMO_AUDITOR" => ("auditor", "auditor"),
            "DEMO_STATISTICIAN" when string.Equals(username, "stat.approver", StringComparison.Ordinal) => ("statistician_approver", "unblinded_statistician"),
            "DEMO_STATISTICIAN" => ("statistician", "unblinded_statistician"),
            "DEMO_PHARMACIST" => ("pharmacist", "pharmacist"),
            _ => (kernelRoleCode.ToLowerInvariant(), "super_admin"),
        };

    private static string FormatUtcIso(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsSubjectParticipantRealmUser(RealmUserSeed realmUser) =>
        realmUser.RealmRoles.Count > 0 &&
        realmUser.RealmRoles.All(role => string.Equals(role, "Subject", StringComparison.Ordinal));

    private static IReadOnlyList<string> GetAttributeList(JsonElement attrs, string key)
    {
        if (!attrs.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string? GetFirstAttribute(JsonElement attrs, string key)
    {
        if (!attrs.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    private sealed record RealmUserSeed(
        string Username,
        string KeycloakSub,
        string Email,
        string FirstName,
        string LastName,
        string TenantCode,
        string? AssignedSiteCode,
        IReadOnlyList<string> StudyIds,
        IReadOnlyList<string> RealmRoles);

    internal sealed record DemoSeedDocument(
        string Epoch,
        DemoSponsorSeed Sponsor,
        DemoStudySeed Study,
        DemoSiteSeed[] Sites,
        DemoParticipantSeed[] Participants,
        DemoRoleSeed[] Roles,
        DemoUserGrantSeed[] Users);

    internal sealed record DemoSponsorSeed(string Code, string Name);

    internal sealed record DemoStudySeed(string Code, string Title, string Phase);

    internal sealed record DemoSiteSeed(
        string Code,
        string Name,
        string Country,
        string InstitutionCode,
        string ComplianceStage);

    internal sealed record DemoParticipantSeed(string SubjectCode, string SiteCode);

    internal sealed record DemoRoleSeed(string Code, string Name, string[] Capabilities);

    internal sealed record DemoUserGrantSeed(string Username, string Subject, string Role, string? Site);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DemoSeedDocument))]
    internal sealed partial class DemoSeedJsonContext : JsonSerializerContext;
}
