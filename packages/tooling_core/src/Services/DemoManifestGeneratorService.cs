using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates the declarative JSON demo seed contract (<c>packages/demo-fixtures/demo-seed.json</c> and
/// <c>packages/demo-fixtures/demo-seed.schema.json</c>) directly against the local Keycloak realm
/// (<c>docker/cadence-realm.json</c>) using Native-AOT-safe source-generated <see cref="System.Text.Json"/> — with zero
/// regex extractors over C# source files and zero committed <c>manifest.js</c> or <c>manifest.d.ts</c> artifacts.
/// Obeys K-TIME-3 by deriving all timestamps from the fixed <c>2026-01-05T09:00:00Z</c> epoch (never the wall clock).
/// </summary>
public static partial class DemoManifestGeneratorService
{
    /// <summary>The canonical demo epoch from <c>packages/demo-fixtures/demo-seed.json</c> (ADR-2543, ADR-2549).</summary>
    public static readonly DateTimeOffset CanonicalEpoch = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Relative path to the canonical JSON demo study seed.</summary>
    public const string DemoSeedRelativePath = "packages/demo-fixtures/demo-seed.json";

    /// <summary>Relative path to the JSON Schema contract for <c>demo-seed.json</c>.</summary>
    public const string DemoSeedSchemaRelativePath = "packages/demo-fixtures/demo-seed.schema.json";

    /// <summary>Relative path to the kernel demo study host loader.</summary>
    public const string KernelSeedRelativePath = "apps/kernel-api/src/DevelopmentDemoData.cs";

    /// <summary>Relative path to the Execution microservice seeder.</summary>
    public const string ExecutionSeedRelativePath = "apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs";

    /// <summary>Relative path to the local Keycloak realm export.</summary>
    public const string RealmRelativePath = "docker/cadence-realm.json";

    /// <summary>Legacy generated ES module path; must remain absent from the repository.</summary>
    public const string LegacyManifestJsRelativePath = "packages/demo-fixtures/src/manifest.js";

    /// <summary>Legacy generated TypeScript declaration path; must remain absent from the repository.</summary>
    public const string LegacyManifestDtsRelativePath = "packages/demo-fixtures/src/manifest.d.ts";

    private static readonly HashSet<string> ValidComplianceStages = new(StringComparer.Ordinal)
    {
        "SITE_ACTIVATION",
        "CONDUCT",
        "CLOSEOUT",
    };

    private static readonly HashSet<string> ValidParticipantStatuses = new(StringComparer.Ordinal)
    {
        "SCREENED",
        "ENROLLED",
        "ACTIVE",
        "COMPLETED",
        "WITHDRAWN",
    };

    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "$schema",
        "schemaVersion",
        "tenantCode",
        "epoch",
        "sponsor",
        "study",
        "sites",
        "participants",
        "roles",
        "users",
    };

    private static readonly HashSet<string> AllowedSponsorProperties = new(StringComparer.Ordinal)
    {
        "code",
        "name",
    };

    private static readonly HashSet<string> AllowedStudyProperties = new(StringComparer.Ordinal)
    {
        "code",
        "title",
        "phase",
    };

    private static readonly HashSet<string> AllowedSiteProperties = new(StringComparer.Ordinal)
    {
        "code",
        "name",
        "country",
        "institutionCode",
        "complianceStage",
    };

    private static readonly HashSet<string> AllowedParticipantProperties = new(StringComparer.Ordinal)
    {
        "subjectCode",
        "siteCode",
        "status",
    };

    private static readonly HashSet<string> AllowedRoleProperties = new(StringComparer.Ordinal)
    {
        "code",
        "name",
        "capabilities",
    };

    private static readonly HashSet<string> AllowedUserProperties = new(StringComparer.Ordinal)
    {
        "username",
        "subject",
        "email",
        "firstName",
        "lastName",
        "tenantId",
        "role",
        "site",
        "workflow",
        "workspacePersona",
        "realmRoles",
    };

    /// <summary>
    /// Validates the canonical JSON demo seed contract against <c>demo-seed.schema.json</c> and
    /// <c>docker/cadence-realm.json</c>. When <paramref name="validateOnly"/> is <c>false</c>, also removes any stale
    /// legacy <c>manifest.js</c> or <c>manifest.d.ts</c> artifacts. Returns <c>0</c> on success and <c>1</c> on drift.
    /// </summary>
    public static int GenerateDemoManifest(bool validateOnly = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();

        string legacyJsPath = Path.Combine(repoRoot, LegacyManifestJsRelativePath);
        string legacyDtsPath = Path.Combine(repoRoot, LegacyManifestDtsRelativePath);

        if (!validateOnly)
        {
            if (File.Exists(legacyJsPath))
            {
                File.Delete(legacyJsPath);
            }

            if (File.Exists(legacyDtsPath))
            {
                File.Delete(legacyDtsPath);
            }
        }

        if (!TryValidateSeedContract(repoRoot, out var snapshot, out var errors))
        {
            foreach (string error in errors)
            {
                Console.WriteLine($"[DEMO-MANIFEST ERROR] {error}");
            }

            return 1;
        }

        if (validateOnly)
        {
            bool hasLegacyArtifacts = false;
            if (File.Exists(legacyJsPath))
            {
                Console.WriteLine(
                    $"[DEMO-MANIFEST DRIFT] Legacy generated file '{LegacyManifestJsRelativePath}' must not exist; DEMO_MANIFEST is synthesized directly from '{DemoSeedRelativePath}'.");
                hasLegacyArtifacts = true;
            }

            if (File.Exists(legacyDtsPath))
            {
                Console.WriteLine(
                    $"[DEMO-MANIFEST DRIFT] Legacy generated file '{LegacyManifestDtsRelativePath}' must not exist; types live in 'packages/demo-fixtures/src/index.d.ts'.");
                hasLegacyArtifacts = true;
            }

            if (hasLegacyArtifacts)
            {
                return 1;
            }
        }

        Console.WriteLine(
            $"✔ Canonical demo seed contract verified ({snapshot!.Study.Code}, {snapshot.Sites.Count} sites, " +
            $"{snapshot.Participants.Count} participants, {snapshot.Personas.Count} personas).");
        return 0;
    }

    /// <summary>
    /// Derives an RFC 9562 version 8 UUID over SHA-256 using the exact <c>DevelopmentDemoData.DemoIds</c> scheme:
    /// <c>cadence-development-demo:{tenantScope}:{kind}:{name}</c>.
    /// </summary>
    public static Guid DeriveUuidV8(string tenantScope, string kind, string name)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-development-demo:{tenantScope}:{kind}:{name}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// Parses and validates <c>demo-seed.json</c> against <c>demo-seed.schema.json</c> and
    /// <c>docker/cadence-realm.json</c> and builds the deterministic <see cref="DemoManifestSnapshot"/> in memory.
    /// </summary>
    public static bool TryValidateSeedContract(
        string repoRoot,
        out DemoManifestSnapshot? snapshot,
        out IReadOnlyList<string> errors)
    {
        var issues = new List<string>();
        snapshot = null;

        string demoSeedPath = Path.Combine(repoRoot, DemoSeedRelativePath);
        string schemaPath = Path.Combine(repoRoot, DemoSeedSchemaRelativePath);
        string realmPath = Path.Combine(repoRoot, RealmRelativePath);

        if (!File.Exists(demoSeedPath))
        {
            issues.Add($"Required canonical demo seed file not found: '{DemoSeedRelativePath}'.");
        }

        if (!File.Exists(schemaPath))
        {
            issues.Add($"Required canonical demo seed JSON schema not found: '{DemoSeedSchemaRelativePath}'.");
        }

        if (!File.Exists(realmPath))
        {
            issues.Add($"Required Keycloak realm file not found: '{RealmRelativePath}'.");
        }

        if (issues.Count > 0)
        {
            errors = issues;
            return false;
        }

        string demoSeedJson = File.ReadAllText(demoSeedPath, Encoding.UTF8);
        string schemaJson = File.ReadAllText(schemaPath, Encoding.UTF8);
        string realmJson = File.ReadAllText(realmPath, Encoding.UTF8);

        ValidateSchemaDocument(schemaJson, issues);
        if (issues.Count > 0)
        {
            errors = issues;
            return false;
        }

        snapshot = ParseAndValidateSeeds(demoSeedJson, realmJson, issues);
        errors = issues;
        return snapshot is not null && issues.Count == 0;
    }

    private static void ValidateSchemaDocument(string schemaJson, List<string> issues)
    {
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                issues.Add($"'{DemoSeedSchemaRelativePath}' root must be a JSON object.");
                return;
            }

            string metaSchema = GetString(root, "$schema");
            if (!metaSchema.Contains("json-schema.org/draft/2020-12/schema", StringComparison.Ordinal))
            {
                issues.Add($"'{DemoSeedSchemaRelativePath}' must declare Draft 2020-12 '$schema'.");
            }

            if (!root.TryGetProperty("required", out var reqEl) || reqEl.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"'{DemoSeedSchemaRelativePath}' must declare a top-level 'required' array.");
                return;
            }

            var requiredProps = reqEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            foreach (string expected in new[]
            {
                "schemaVersion",
                "tenantCode",
                "epoch",
                "sponsor",
                "study",
                "sites",
                "participants",
                "roles",
                "users",
            })
            {
                if (!requiredProps.Contains(expected))
                {
                    issues.Add($"'{DemoSeedSchemaRelativePath}' is missing required top-level property '{expected}'.");
                }
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"Invalid JSON in '{DemoSeedSchemaRelativePath}': {ex.Message}");
        }
    }

    private static void ValidateJsonObjectShape(
        JsonElement element,
        HashSet<string> allowedProperties,
        string pathLabel,
        List<string> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add($"Expected JSON object at '{pathLabel}' in '{DemoSeedRelativePath}'.");
            return;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(prop.Name))
            {
                issues.Add($"Disallowed additional property '{prop.Name}' at '{pathLabel}' in '{DemoSeedRelativePath}'.");
            }
        }
    }

    private static DemoManifestSnapshot? ParseAndValidateSeeds(
        string demoSeedJson,
        string realmJson,
        List<string> issues)
    {
        DemoSeedDocument? seed;
        try
        {
            using (var rawDoc = JsonDocument.Parse(demoSeedJson))
            {
                var root = rawDoc.RootElement;
                ValidateJsonObjectShape(root, AllowedRootProperties, "$", issues);
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("sponsor", out var sponsorEl))
                    {
                        ValidateJsonObjectShape(sponsorEl, AllowedSponsorProperties, "$.sponsor", issues);
                    }

                    if (root.TryGetProperty("study", out var studyEl))
                    {
                        ValidateJsonObjectShape(studyEl, AllowedStudyProperties, "$.study", issues);
                    }

                    if (root.TryGetProperty("sites", out var sitesEl) && sitesEl.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var siteEl in sitesEl.EnumerateArray())
                        {
                            ValidateJsonObjectShape(siteEl, AllowedSiteProperties, $"$.sites[{idx}]", issues);
                            idx++;
                        }
                    }

                    if (root.TryGetProperty("participants", out var partEl) && partEl.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var pEl in partEl.EnumerateArray())
                        {
                            ValidateJsonObjectShape(pEl, AllowedParticipantProperties, $"$.participants[{idx}]", issues);
                            idx++;
                        }
                    }

                    if (root.TryGetProperty("roles", out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var rEl in rolesEl.EnumerateArray())
                        {
                            ValidateJsonObjectShape(rEl, AllowedRoleProperties, $"$.roles[{idx}]", issues);
                            idx++;
                        }
                    }

                    if (root.TryGetProperty("users", out var usersEl) && usersEl.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var uEl in usersEl.EnumerateArray())
                        {
                            ValidateJsonObjectShape(uEl, AllowedUserProperties, $"$.users[{idx}]", issues);
                            idx++;
                        }
                    }
                }
            }

            seed = JsonSerializer.Deserialize(demoSeedJson, DemoSeedJsonContext.Default.DemoSeedDocument);
        }
        catch (JsonException ex)
        {
            issues.Add($"Invalid JSON in '{DemoSeedRelativePath}': {ex.Message}");
            return null;
        }

        if (seed is null)
        {
            issues.Add($"'{DemoSeedRelativePath}' deserialized to null.");
            return null;
        }

        if (seed.SchemaVersion != 1)
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare schemaVersion = 1 (found {seed.SchemaVersion}).");
        }

        if (string.IsNullOrWhiteSpace(seed.TenantCode))
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare a non-empty 'tenantCode'.");
        }

        if (string.IsNullOrWhiteSpace(seed.Epoch) ||
            !DateTimeOffset.TryParse(seed.Epoch, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedEpoch))
        {
            issues.Add($"Invalid or missing 'epoch' ISO-8601 timestamp in '{DemoSeedRelativePath}': '{seed.Epoch}'.");
            return null;
        }

        if (parsedEpoch != CanonicalEpoch)
        {
            issues.Add($"Demo epoch '{FormatUtcIso(parsedEpoch)}' in '{DemoSeedRelativePath}' must equal '{FormatUtcIso(CanonicalEpoch)}' (ADR-2549).");
        }

        if (seed.Sponsor is null || string.IsNullOrWhiteSpace(seed.Sponsor.Code) || string.IsNullOrWhiteSpace(seed.Sponsor.Name))
        {
            issues.Add($"'{DemoSeedRelativePath}' is missing sponsor.code or sponsor.name.");
            return null;
        }

        if (seed.Study is null || string.IsNullOrWhiteSpace(seed.Study.Code) || string.IsNullOrWhiteSpace(seed.Study.Title) || string.IsNullOrWhiteSpace(seed.Study.Phase))
        {
            issues.Add($"'{DemoSeedRelativePath}' is missing study.code, study.title, or study.phase.");
            return null;
        }

        if (seed.Sites is not { Length: > 0 })
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare at least one site in 'sites'.");
            return null;
        }

        if (seed.Participants is not { Length: > 0 })
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare at least one participant in 'participants'.");
            return null;
        }

        if (seed.Roles is not { Length: > 0 })
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare at least one role in 'roles'.");
            return null;
        }

        if (seed.Users is not { Length: > 0 })
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare at least one user grant in 'users'.");
            return null;
        }

        Dictionary<string, RealmUserSeed> realmUsers;
        try
        {
            realmUsers = ParseRealmUsers(realmJson, issues);
        }
        catch (JsonException ex)
        {
            issues.Add($"Invalid JSON in '{RealmRelativePath}': {ex.Message}");
            return null;
        }

        if (issues.Count > 0)
        {
            return null;
        }

        string tenantCode = seed.TenantCode;
        string tenantUuid = DeriveUuidV8(tenantCode, "tenant", tenantCode).ToString("D");

        var sponsor = new DemoSponsorEntry(
            Code: seed.Sponsor.Code,
            Name: seed.Sponsor.Name,
            OrganizationUuid: DeriveUuidV8(tenantUuid, "organization", "sponsor").ToString("D"));

        string studyCode = seed.Study.Code;
        string studyUuid = DeriveUuidV8(tenantUuid, "study", studyCode).ToString("D");
        var study = new DemoStudyEntry(
            Code: studyCode,
            Title: seed.Study.Title,
            Phase: seed.Study.Phase,
            SponsorCode: seed.Sponsor.Code,
            Uuid: studyUuid,
            Ref: $"study:{studyUuid}");

        var sites = new List<DemoSiteEntry>(seed.Sites.Length);
        var siteSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawSite in seed.Sites)
        {
            if (string.IsNullOrWhiteSpace(rawSite.Code) ||
                string.IsNullOrWhiteSpace(rawSite.Name) ||
                string.IsNullOrWhiteSpace(rawSite.Country) ||
                string.IsNullOrWhiteSpace(rawSite.InstitutionCode) ||
                string.IsNullOrWhiteSpace(rawSite.ComplianceStage))
            {
                issues.Add($"Site entry in '{DemoSeedRelativePath}' is missing code, name, country, institutionCode, or complianceStage.");
                continue;
            }

            if (!ValidComplianceStages.Contains(rawSite.ComplianceStage))
            {
                issues.Add($"Site '{rawSite.Code}' in '{DemoSeedRelativePath}' has invalid complianceStage '{rawSite.ComplianceStage}'.");
                continue;
            }

            if (!siteSet.Add(rawSite.Code))
            {
                issues.Add($"Duplicate site code '{rawSite.Code}' in '{DemoSeedRelativePath}'.");
                continue;
            }

            string instUuid = DeriveUuidV8(tenantUuid, "organization", rawSite.Code).ToString("D");
            string siteUuid = DeriveUuidV8(tenantUuid, "site", rawSite.Code).ToString("D");
            string studySiteUuid = DeriveUuidV8(tenantUuid, "study-site", rawSite.Code).ToString("D");
            sites.Add(new DemoSiteEntry(
                Code: rawSite.Code,
                Name: rawSite.Name,
                Country: rawSite.Country,
                InstitutionCode: rawSite.InstitutionCode,
                ComplianceStage: rawSite.ComplianceStage,
                InstitutionUuid: instUuid,
                SiteUuid: siteUuid,
                StudySiteUuid: studySiteUuid,
                StudySiteRef: $"study_site:{studySiteUuid}"));
        }

        var participants = new List<DemoParticipantEntry>(seed.Participants.Length);
        var subjectSet = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < seed.Participants.Length; i++)
        {
            var rawParticipant = seed.Participants[i];
            if (string.IsNullOrWhiteSpace(rawParticipant.SubjectCode) ||
                string.IsNullOrWhiteSpace(rawParticipant.SiteCode) ||
                string.IsNullOrWhiteSpace(rawParticipant.Status))
            {
                issues.Add($"Participant entry #{i} in '{DemoSeedRelativePath}' is missing subjectCode, siteCode, or status.");
                continue;
            }

            if (!subjectSet.Add(rawParticipant.SubjectCode))
            {
                issues.Add($"Duplicate participant subjectCode '{rawParticipant.SubjectCode}' in '{DemoSeedRelativePath}'.");
                continue;
            }

            if (!siteSet.Contains(rawParticipant.SiteCode))
            {
                issues.Add($"Participant '{rawParticipant.SubjectCode}' in '{DemoSeedRelativePath}' references unknown site '{rawParticipant.SiteCode}'.");
                continue;
            }

            if (!ValidParticipantStatuses.Contains(rawParticipant.Status))
            {
                issues.Add($"Participant '{rawParticipant.SubjectCode}' in '{DemoSeedRelativePath}' has invalid status '{rawParticipant.Status}'.");
                continue;
            }

            string participantUuid = DeriveUuidV8(tenantUuid, "participant", rawParticipant.SubjectCode).ToString("D");
            string enrollmentUuid = DeriveUuidV8(tenantUuid, "enrollment", rawParticipant.SubjectCode).ToString("D");
            string screenedAt = FormatUtcIso(parsedEpoch.AddDays(7 + i));
            participants.Add(new DemoParticipantEntry(
                SubjectCode: rawParticipant.SubjectCode,
                SiteCode: rawParticipant.SiteCode,
                EnrollmentIndex: i,
                Status: rawParticipant.Status,
                ScreenedAtIso: screenedAt,
                ParticipantUuid: participantUuid,
                EnrollmentUuid: enrollmentUuid,
                EnrollmentRef: $"enrollment:{enrollmentUuid}"));
        }

        var roles = new List<DemoRoleEntry>(seed.Roles.Length);
        var roleSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawRole in seed.Roles)
        {
            if (string.IsNullOrWhiteSpace(rawRole.Code) ||
                string.IsNullOrWhiteSpace(rawRole.Name) ||
                rawRole.Capabilities is not { Length: > 0 })
            {
                issues.Add($"Role entry in '{DemoSeedRelativePath}' is missing code, name, or non-empty capabilities.");
                continue;
            }

            if (!roleSet.Add(rawRole.Code))
            {
                issues.Add($"Duplicate role code '{rawRole.Code}' in '{DemoSeedRelativePath}'.");
                continue;
            }

            roles.Add(new DemoRoleEntry(
                Code: rawRole.Code,
                Name: rawRole.Name,
                RoleUuid: DeriveUuidV8(tenantUuid, "role", rawRole.Code).ToString("D"),
                Capabilities: rawRole.Capabilities));
        }

        var personas = new List<DemoPersonaEntry>(seed.Users.Length);
        var seenRealmUsers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawUser in seed.Users)
        {
            string username = rawUser.Username;
            string subjectUuid = rawUser.Subject;
            string roleCode = rawUser.Role;
            string? assignedSiteCode = rawUser.Site;

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(subjectUuid) ||
                string.IsNullOrWhiteSpace(rawUser.Email) ||
                string.IsNullOrWhiteSpace(rawUser.FirstName) ||
                string.IsNullOrWhiteSpace(rawUser.LastName) ||
                string.IsNullOrWhiteSpace(rawUser.TenantId) ||
                string.IsNullOrWhiteSpace(roleCode) ||
                string.IsNullOrWhiteSpace(rawUser.Workflow) ||
                string.IsNullOrWhiteSpace(rawUser.WorkspacePersona) ||
                rawUser.RealmRoles is not { Length: > 0 })
            {
                issues.Add($"User grant entry '{username}' in '{DemoSeedRelativePath}' is missing required persona or realm binding fields.");
                continue;
            }

            if (!seenRealmUsers.Add(username))
            {
                issues.Add($"Duplicate user grant '{username}' in '{DemoSeedRelativePath}'.");
                continue;
            }

            if (!roleSet.Contains(roleCode))
            {
                issues.Add($"Kernel demo user '{username}' in '{DemoSeedRelativePath}' references undefined role '{roleCode}'.");
                continue;
            }

            if (assignedSiteCode is not null && !siteSet.Contains(assignedSiteCode))
            {
                issues.Add($"Kernel demo user '{username}' in '{DemoSeedRelativePath}' references undefined site '{assignedSiteCode}'.");
                continue;
            }

            if (!realmUsers.TryGetValue(username, out var realmUser))
            {
                issues.Add($"Kernel demo user '{username}' in '{DemoSeedRelativePath}' is missing from '{RealmRelativePath}'.");
                continue;
            }

            if (IsSubjectParticipantRealmUser(realmUser) ||
                rawUser.RealmRoles.Contains("Subject", StringComparer.Ordinal))
            {
                issues.Add(
                    $"Participant realm user '{username}' in '{RealmRelativePath}' holds 'Subject' role and must not have a staff kernel grant in '{DemoSeedRelativePath}' (K027).");
                continue;
            }

            if (!string.Equals(realmUser.KeycloakSub, subjectUuid, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"Subject UUID mismatch for '{username}': '{DemoSeedRelativePath}' has '{subjectUuid}' " +
                    $"but '{RealmRelativePath}' has '{realmUser.KeycloakSub}'.");
                continue;
            }

            if (!string.Equals(realmUser.Email, rawUser.Email, StringComparison.Ordinal) ||
                !string.Equals(realmUser.FirstName, rawUser.FirstName, StringComparison.Ordinal) ||
                !string.Equals(realmUser.LastName, rawUser.LastName, StringComparison.Ordinal) ||
                !string.Equals(realmUser.TenantCode, rawUser.TenantId, StringComparison.Ordinal) ||
                !string.Equals(tenantCode, rawUser.TenantId, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Identity attribute mismatch for '{username}' between '{DemoSeedRelativePath}' and '{RealmRelativePath}'.");
                continue;
            }

            if (!string.Equals(assignedSiteCode, realmUser.AssignedSiteCode, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Site attribute mismatch for '{username}': '{DemoSeedRelativePath}' grants '{assignedSiteCode ?? "<null>"}' " +
                    $"but '{RealmRelativePath}' has '{realmUser.AssignedSiteCode ?? "<null>"}'.");
                continue;
            }

            if (!rawUser.RealmRoles.SequenceEqual(realmUser.RealmRoles, StringComparer.Ordinal))
            {
                issues.Add(
                    $"Realm roles mismatch for '{username}': '{DemoSeedRelativePath}' has [{string.Join(", ", rawUser.RealmRoles)}] " +
                    $"but '{RealmRelativePath}' has [{string.Join(", ", realmUser.RealmRoles)}].");
                continue;
            }

            var (expectedWorkflow, expectedWorkspace) = ResolveWorkflowAndWorkspace(username, roleCode, realmUser.RealmRoles);
            if (!string.Equals(rawUser.Workflow, expectedWorkflow, StringComparison.Ordinal) ||
                !string.Equals(rawUser.WorkspacePersona, expectedWorkspace, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Persona binding mismatch for '{username}' in '{DemoSeedRelativePath}': expected workflow='{expectedWorkflow}', workspacePersona='{expectedWorkspace}'.");
                continue;
            }

            personas.Add(new DemoPersonaEntry(
                Username: username,
                KeycloakSub: subjectUuid,
                Email: rawUser.Email,
                FirstName: rawUser.FirstName,
                LastName: rawUser.LastName,
                TenantCode: rawUser.TenantId,
                KernelRoleCode: roleCode,
                Workflow: rawUser.Workflow,
                WorkspacePersona: rawUser.WorkspacePersona,
                AssignedSiteCode: assignedSiteCode,
                GrantUuid: DeriveUuidV8(tenantUuid, "grant", username).ToString("D"),
                RealmRoles: rawUser.RealmRoles));
        }

        foreach (string realmUsername in realmUsers.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (seenRealmUsers.Contains(realmUsername))
            {
                continue;
            }

            var realmUser = realmUsers[realmUsername];
            if (IsSubjectParticipantRealmUser(realmUser))
            {
                if (realmUser.AssignedSiteCode is not null && !siteSet.Contains(realmUser.AssignedSiteCode))
                {
                    issues.Add(
                        $"Participant realm user '{realmUsername}' in '{RealmRelativePath}' references unknown site '{realmUser.AssignedSiteCode}'.");
                }

                foreach (string assignedStudy in realmUser.StudyIds)
                {
                    if (!string.Equals(assignedStudy, "*", StringComparison.Ordinal) &&
                        !string.Equals(assignedStudy, studyCode, StringComparison.Ordinal))
                    {
                        issues.Add(
                            $"Participant realm user '{realmUsername}' in '{RealmRelativePath}' references study '{assignedStudy}', expected '{studyCode}'.");
                    }
                }

                continue;
            }

            issues.Add($"Realm user '{realmUsername}' in '{RealmRelativePath}' has no role grant in '{DemoSeedRelativePath}'.");
        }

        return issues.Count > 0
            ? null
            : new DemoManifestSnapshot(
                TenantCode: tenantCode,
                TenantUuid: tenantUuid,
                EpochIso: FormatUtcIso(parsedEpoch),
                EpochMs: parsedEpoch.ToUnixTimeMilliseconds(),
                Sponsor: sponsor,
                Study: study,
                Sites: sites,
                Participants: participants,
                Roles: roles,
                Personas: personas);
    }

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
        realmUser.RealmRoles.Contains("Subject", StringComparer.Ordinal);

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

    private static string FindRepoRoot()
    {
        string current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
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
        int SchemaVersion,
        string TenantCode,
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

    internal sealed record DemoParticipantSeed(
        string SubjectCode,
        string SiteCode,
        string Status);

    internal sealed record DemoRoleSeed(string Code, string Name, string[] Capabilities);

    internal sealed record DemoUserGrantSeed(
        string Username,
        string Subject,
        string Email,
        string FirstName,
        string LastName,
        string TenantId,
        string Role,
        string? Site,
        string Workflow,
        string WorkspacePersona,
        string[] RealmRoles);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DemoSeedDocument))]
    internal sealed partial class DemoSeedJsonContext : JsonSerializerContext;
}

/// <summary>Canonical snapshot of the synthetic demo study validated from <c>packages/demo-fixtures/demo-seed.json</c>.</summary>
public sealed record DemoManifestSnapshot(
    string TenantCode,
    string TenantUuid,
    string EpochIso,
    long EpochMs,
    DemoSponsorEntry Sponsor,
    DemoStudyEntry Study,
    IReadOnlyList<DemoSiteEntry> Sites,
    IReadOnlyList<DemoParticipantEntry> Participants,
    IReadOnlyList<DemoRoleEntry> Roles,
    IReadOnlyList<DemoPersonaEntry> Personas);

/// <summary>Synthetic sponsor entry in the demo manifest.</summary>
public sealed record DemoSponsorEntry(
    string Code,
    string Name,
    string OrganizationUuid);

/// <summary>Synthetic study entry in the demo manifest.</summary>
public sealed record DemoStudyEntry(
    string Code,
    string Title,
    string Phase,
    string SponsorCode,
    string Uuid,
    string Ref);

/// <summary>Synthetic site entry in the demo manifest.</summary>
public sealed record DemoSiteEntry(
    string Code,
    string Name,
    string Country,
    string InstitutionCode,
    string ComplianceStage,
    string InstitutionUuid,
    string SiteUuid,
    string StudySiteUuid,
    string StudySiteRef);

/// <summary>Synthetic participant entry in the demo manifest.</summary>
public sealed record DemoParticipantEntry(
    string SubjectCode,
    string SiteCode,
    int EnrollmentIndex,
    string Status,
    string ScreenedAtIso,
    string ParticipantUuid,
    string EnrollmentUuid,
    string EnrollmentRef);

/// <summary>Synthetic role entry in the demo manifest.</summary>
public sealed record DemoRoleEntry(
    string Code,
    string Name,
    string RoleUuid,
    IReadOnlyList<string> Capabilities);

/// <summary>Synthetic realm persona entry in the demo manifest.</summary>
public sealed record DemoPersonaEntry(
    string Username,
    string KeycloakSub,
    string Email,
    string FirstName,
    string LastName,
    string TenantCode,
    string KernelRoleCode,
    string Workflow,
    string WorkspacePersona,
    string? AssignedSiteCode,
    string GrantUuid,
    IReadOnlyList<string> RealmRoles);
