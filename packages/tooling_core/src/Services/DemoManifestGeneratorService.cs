using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates in-memory parity of the canonical synthetic demo study seed (<c>packages/demo-fixtures/demo-seed.json</c>)
/// against the local Keycloak realm (<c>docker/cadence-realm.json</c>), derives RFC 9562 version 8 UUIDs over SHA-256,
/// and blocks re-introduction of committed <c>packages/demo-fixtures/src/manifest.js</c> or <c>manifest.d.ts</c> files
/// (synthesized virtually by Vite at dev/build time). Obeys K-TIME-3 by deriving all timestamps from the fixed
/// <c>2026-01-05T09:00:00Z</c> epoch (never the wall clock).
/// </summary>
public static partial class DemoManifestGeneratorService
{
    /// <summary>The canonical demo epoch from <c>packages/demo-fixtures/demo-seed.json</c> (ADR-2543, ADR-2548).</summary>
    public static readonly DateTimeOffset CanonicalEpoch = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Relative path to the canonical JSON demo study seed.</summary>
    public const string DemoSeedRelativePath = "packages/demo-fixtures/demo-seed.json";

    /// <summary>Relative path to the kernel demo study host loader.</summary>
    public const string KernelSeedRelativePath = "apps/kernel-api/src/DevelopmentDemoData.cs";

    /// <summary>Relative path to the Execution microservice seeder.</summary>
    public const string ExecutionSeedRelativePath = "apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs";

    /// <summary>Relative path to the local Keycloak realm export.</summary>
    public const string RealmRelativePath = "docker/cadence-realm.json";

    /// <summary>Legacy generated ES module manifest path that must remain untracked on disk.</summary>
    public const string ManifestJsRelativePath = "packages/demo-fixtures/src/manifest.js";

    /// <summary>Legacy generated TypeScript declaration path that must remain untracked on disk.</summary>
    public const string ManifestDtsRelativePath = "packages/demo-fixtures/src/manifest.d.ts";

    /// <summary>
    /// Validates <c>packages/demo-fixtures/demo-seed.json</c> against <c>docker/cadence-realm.json</c> in memory and
    /// verifies that legacy generated manifest files are absent from disk.
    /// Returns <c>0</c> on success and <c>1</c> on missing inputs, seed/realm drift, or legacy committed manifest files.
    /// </summary>
    public static int GenerateDemoManifest(bool validateOnly = false, string? repoRoot = null)
    {
        _ = validateOnly;
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();

        bool ok = TryBuildSnapshot(repoRoot, out var snapshot, out var errors);
        foreach (string error in errors)
        {
            Console.WriteLine($"[DEMO-MANIFEST ERROR] {error}");
        }

        string jsPath = Path.Combine(repoRoot, ManifestJsRelativePath);
        if (File.Exists(jsPath))
        {
            Console.WriteLine(
                $"[DEMO-MANIFEST DRIFT] '{ManifestJsRelativePath}' must not exist on disk; DEMO_MANIFEST is synthesized virtually by Vite.");
            ok = false;
        }

        string dtsPath = Path.Combine(repoRoot, ManifestDtsRelativePath);
        if (File.Exists(dtsPath))
        {
            Console.WriteLine(
                $"[DEMO-MANIFEST DRIFT] '{ManifestDtsRelativePath}' must not exist on disk; fixture interfaces live in 'packages/demo-fixtures/src/index.d.ts'.");
            ok = false;
        }

        if (!ok || snapshot is null)
        {
            return 1;
        }

        Console.WriteLine(
            $"✔ Canonical demo seed & realm verified ({snapshot.Study.Code}, {snapshot.Sites.Count} sites, " +
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

    /// <summary>Builds and validates the canonical manifest snapshot in memory from <c>demo-seed.json</c> and <c>cadence-realm.json</c>.</summary>
    public static bool TryBuildSnapshot(
        string repoRoot,
        out DemoManifestSnapshot? snapshot,
        out IReadOnlyList<string> errors)
    {
        var issues = new List<string>();
        snapshot = null;

        string demoSeedPath = Path.Combine(repoRoot, DemoSeedRelativePath);
        string realmPath = Path.Combine(repoRoot, RealmRelativePath);

        if (!File.Exists(demoSeedPath))
        {
            issues.Add($"Required canonical demo seed file not found: '{DemoSeedRelativePath}'.");
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
        string realmJson = File.ReadAllText(realmPath, Encoding.UTF8);

        snapshot = ParseAndValidateSeeds(demoSeedJson, realmJson, issues);
        errors = issues;
        return snapshot is not null && issues.Count == 0;
    }

    private static DemoManifestSnapshot? ParseAndValidateSeeds(
        string demoSeedJson,
        string realmJson,
        List<string> issues)
    {
        DemoSeedDocument? seed;
        try
        {
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

        if (!DateTimeOffset.TryParseExact(
                seed.Epoch,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedEpoch))
        {
            issues.Add($"Could not parse ISO-8601 UTC 'epoch' from '{DemoSeedRelativePath}'.");
            return null;
        }

        if (parsedEpoch != CanonicalEpoch)
        {
            issues.Add(
                $"Epoch in '{DemoSeedRelativePath}' ({FormatUtcIso(parsedEpoch)}) drifted from canonical 2026-01-05T09:00:00Z epoch."); // deid-ignore: error message naming canonical demo epoch
        }

        if (seed.Sponsor is null ||
            string.IsNullOrWhiteSpace(seed.Sponsor.Code) ||
            string.IsNullOrWhiteSpace(seed.Sponsor.Name))
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare a non-empty 'sponsor' code and name.");
            return null;
        }

        if (seed.Study is null ||
            string.IsNullOrWhiteSpace(seed.Study.Code) ||
            string.IsNullOrWhiteSpace(seed.Study.Title) ||
            string.IsNullOrWhiteSpace(seed.Study.Phase))
        {
            issues.Add($"'{DemoSeedRelativePath}' must declare a non-empty 'study' code, title, and phase.");
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

        string studyCode = seed.Study.Code;
        string sponsorCode = seed.Sponsor.Code;
        string sponsorName = seed.Sponsor.Name;
        string studyTitle = seed.Study.Title;
        string studyPhase = seed.Study.Phase;

        var realmUsers = ParseRealmUsers(realmJson, issues);

        if (issues.Count > 0)
        {
            return null;
        }

        string tenantCode = realmUsers.Values.Select(u => u.TenantCode).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "cadence-local";
        string tenantUuid = DeriveUuidV8(tenantCode, "tenant", tenantCode).ToString("D");

        var sponsor = new DemoSponsorEntry(
            Code: sponsorCode,
            Name: sponsorName,
            OrganizationUuid: DeriveUuidV8(tenantUuid, "organization", "sponsor").ToString("D"));

        string studyUuid = DeriveUuidV8(tenantUuid, "study", studyCode).ToString("D");
        var study = new DemoStudyEntry(
            Code: studyCode,
            Title: studyTitle,
            Phase: studyPhase,
            SponsorCode: sponsorCode,
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
        for (int i = 0; i < seed.Participants.Length; i++)
        {
            var rawParticipant = seed.Participants[i];
            if (string.IsNullOrWhiteSpace(rawParticipant.SubjectCode) || string.IsNullOrWhiteSpace(rawParticipant.SiteCode))
            {
                issues.Add($"Participant entry #{i} in '{DemoSeedRelativePath}' is missing subjectCode or siteCode.");
                continue;
            }

            if (!siteSet.Contains(rawParticipant.SiteCode))
            {
                issues.Add($"Participant '{rawParticipant.SubjectCode}' in '{DemoSeedRelativePath}' references unknown site '{rawParticipant.SiteCode}'.");
                continue;
            }

            string participantUuid = DeriveUuidV8(tenantUuid, "participant", rawParticipant.SubjectCode).ToString("D");
            string enrollmentUuid = DeriveUuidV8(tenantUuid, "enrollment", rawParticipant.SubjectCode).ToString("D");
            string screenedAt = FormatUtcIso(parsedEpoch.AddDays(7 + i));
            participants.Add(new DemoParticipantEntry(
                SubjectCode: rawParticipant.SubjectCode,
                SiteCode: rawParticipant.SiteCode,
                EnrollmentIndex: i,
                Status: "SCREENED",
                ScreenedAtIso: screenedAt,
                ParticipantUuid: participantUuid,
                EnrollmentUuid: enrollmentUuid,
                EnrollmentRef: $"enrollment:{enrollmentUuid}"));
        }

        var roles = new List<DemoRoleEntry>(seed.Roles.Length);
        var roleSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawRole in seed.Roles)
        {
            if (string.IsNullOrWhiteSpace(rawRole.Code) || string.IsNullOrWhiteSpace(rawRole.Name) || rawRole.Capabilities is null)
            {
                issues.Add($"Role entry in '{DemoSeedRelativePath}' is missing code, name, or capabilities.");
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

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(subjectUuid) || string.IsNullOrWhiteSpace(roleCode))
            {
                issues.Add($"User grant entry in '{DemoSeedRelativePath}' is missing username, subject, or role.");
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

            seenRealmUsers.Add(username);
            if (IsSubjectParticipantRealmUser(realmUser))
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

            if (assignedSiteCode is not null &&
                realmUser.AssignedSiteCode is not null &&
                !string.Equals(assignedSiteCode, realmUser.AssignedSiteCode, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Site attribute mismatch for '{username}': '{DemoSeedRelativePath}' grants '{assignedSiteCode}' " +
                    $"but '{RealmRelativePath}' has '{realmUser.AssignedSiteCode}'.");
                continue;
            }

            var (workflow, workspacePersona) = ResolveWorkflowAndWorkspace(username, roleCode, realmUser.RealmRoles);
            personas.Add(new DemoPersonaEntry(
                Username: username,
                KeycloakSub: subjectUuid,
                Email: realmUser.Email,
                FirstName: realmUser.FirstName,
                LastName: realmUser.LastName,
                TenantCode: string.IsNullOrWhiteSpace(realmUser.TenantCode) ? tenantCode : realmUser.TenantCode,
                KernelRoleCode: roleCode,
                Workflow: workflow,
                WorkspacePersona: workspacePersona,
                AssignedSiteCode: assignedSiteCode,
                GrantUuid: DeriveUuidV8(tenantUuid, "grant", username).ToString("D"),
                RealmRoles: realmUser.RealmRoles));
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
}
