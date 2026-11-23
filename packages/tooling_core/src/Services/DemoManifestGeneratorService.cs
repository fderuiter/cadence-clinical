using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Cadence.Deid;
using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Exports and validates the canonical synthetic demo study manifest (<c>packages/demo-fixtures/src/manifest.js</c> and
/// <c>manifest.d.ts</c>) from the canonical JSON demo seed (<c>packages/demo-fixtures/demo-seed.json</c>), the
/// Execution seeder (<c>apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs</c>), and the local Keycloak
/// realm (<c>docker/cadence-realm.json</c>). Obeys K-TIME-3 by deriving all timestamps from the fixed
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

    /// <summary>Relative path to the generated ES module manifest.</summary>
    public const string ManifestJsRelativePath = "packages/demo-fixtures/src/manifest.js";

    /// <summary>Relative path to the generated TypeScript declarations.</summary>
    public const string ManifestDtsRelativePath = "packages/demo-fixtures/src/manifest.d.ts";

    /// <summary>
    /// Generates or validates <c>packages/demo-fixtures/src/manifest.js</c> and <c>manifest.d.ts</c>.
    /// Returns <c>0</c> on success and <c>1</c> on missing inputs, cross-seeder drift, or committed manifest drift.
    /// </summary>
    public static int GenerateDemoManifest(bool validateOnly = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();

        if (!TryBuildExpectedArtifacts(repoRoot, out var snapshot, out string expectedJs, out string expectedDts, out var errors))
        {
            foreach (string error in errors)
            {
                Console.WriteLine($"[DEMO-MANIFEST ERROR] {error}");
            }

            return 1;
        }

        string jsPath = Path.Combine(repoRoot, ManifestJsRelativePath);
        string dtsPath = Path.Combine(repoRoot, ManifestDtsRelativePath);

        if (validateOnly)
        {
            bool matches = true;
            if (!File.Exists(jsPath))
            {
                Console.WriteLine($"[DEMO-MANIFEST DRIFT] Missing '{ManifestJsRelativePath}'. Run 'pnpm cadence generate-demo-manifest'.");
                matches = false;
            }
            else
            {
                string actualJs = File.ReadAllText(jsPath, Encoding.UTF8).ReplaceLineEndings("\n");
                if (!string.Equals(actualJs, expectedJs, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[DEMO-MANIFEST DRIFT] '{ManifestJsRelativePath}' is out of sync with the canonical demo seed. Run 'pnpm cadence generate-demo-manifest'.");
                    matches = false;
                }
            }

            if (!File.Exists(dtsPath))
            {
                Console.WriteLine($"[DEMO-MANIFEST DRIFT] Missing '{ManifestDtsRelativePath}'. Run 'pnpm cadence generate-demo-manifest'.");
                matches = false;
            }
            else
            {
                string actualDts = File.ReadAllText(dtsPath, Encoding.UTF8).ReplaceLineEndings("\n");
                if (!string.Equals(actualDts, expectedDts, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[DEMO-MANIFEST DRIFT] '{ManifestDtsRelativePath}' is out of sync with the canonical demo seed. Run 'pnpm cadence generate-demo-manifest'.");
                    matches = false;
                }
            }

            if (!matches)
            {
                return 1;
            }

            Console.WriteLine(
                $"✔ Canonical demo manifest verified ({snapshot!.Study.Code}, {snapshot.Sites.Count} sites, " +
                $"{snapshot.Participants.Count} participants, {snapshot.Personas.Count} personas).");
            return 0;
        }

        string? outDir = Path.GetDirectoryName(jsPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        WriteUtf8NoBom(jsPath, expectedJs);
        WriteUtf8NoBom(dtsPath, expectedDts);

        Console.WriteLine(
            $"✔ Generated '{ManifestJsRelativePath}' and '{ManifestDtsRelativePath}' " +
            $"for study {snapshot!.Study.Code} ({snapshot.Sites.Count} sites, {snapshot.Participants.Count} participants, {snapshot.Personas.Count} personas).");
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

    /// <summary>Builds the canonical manifest snapshot and renders the expected JS and d.ts artifacts in memory.</summary>
    public static bool TryBuildExpectedArtifacts(
        string repoRoot,
        out DemoManifestSnapshot? snapshot,
        out string expectedJs,
        out string expectedDts,
        out IReadOnlyList<string> errors)
    {
        var issues = new List<string>();
        snapshot = null;
        expectedJs = string.Empty;
        expectedDts = string.Empty;

        string demoSeedPath = Path.Combine(repoRoot, DemoSeedRelativePath);
        string executionSeedPath = Path.Combine(repoRoot, ExecutionSeedRelativePath);
        string realmPath = Path.Combine(repoRoot, RealmRelativePath);

        if (!File.Exists(demoSeedPath))
        {
            issues.Add($"Required canonical demo seed file not found: '{DemoSeedRelativePath}'.");
        }

        if (!File.Exists(executionSeedPath))
        {
            issues.Add($"Required execution seeder file not found: '{ExecutionSeedRelativePath}'.");
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
        string executionSource = File.ReadAllText(executionSeedPath, Encoding.UTF8);
        string realmJson = File.ReadAllText(realmPath, Encoding.UTF8);

        snapshot = ParseAndValidateSeeds(demoSeedJson, executionSource, realmJson, issues);
        if (snapshot is null || issues.Count > 0)
        {
            errors = issues;
            return false;
        }

        expectedJs = RenderManifestJs(snapshot);
        expectedDts = RenderManifestDts();
        errors = issues;
        return true;
    }

    private static DemoManifestSnapshot? ParseAndValidateSeeds(
        string demoSeedJson,
        string executionSource,
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

        var executionStages = ParseExecutionSeed(executionSource, studyCode, seed.Sites, seed.Participants, issues);
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
                string.IsNullOrWhiteSpace(rawSite.InstitutionCode))
            {
                issues.Add($"Site entry in '{DemoSeedRelativePath}' is missing code, name, country, or institutionCode.");
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
            string complianceStage = executionStages.GetValueOrDefault(rawSite.Code, "CONDUCT");
            sites.Add(new DemoSiteEntry(
                Code: rawSite.Code,
                Name: rawSite.Name,
                Country: rawSite.Country,
                InstitutionCode: rawSite.InstitutionCode,
                ComplianceStage: complianceStage,
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

    private static Dictionary<string, string> ParseExecutionSeed(
        string executionSource,
        string expectedStudyCode,
        IReadOnlyList<DemoSiteSeed> kernelSites,
        IReadOnlyList<DemoParticipantSeed> kernelParticipants,
        List<string> issues)
    {
        var stageBySite = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var groups in ExecutionSiteComplianceRegex().Matches(executionSource).Select(match => match.Groups))
        {
            string study = groups["study"].Value;
            string site = groups["site"].Value;
            string stage = groups["stage"].Value;
            if (!string.Equals(study, expectedStudyCode, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Execution seeder SiteComplianceCache uses study '{study}', expected '{expectedStudyCode}' from '{DemoSeedRelativePath}'.");
            }

            stageBySite[site] = stage;
        }

        foreach (string siteCode in kernelSites.Select(siteSeed => siteSeed.Code))
        {
            if (stageBySite.Count > 0 && !stageBySite.ContainsKey(siteCode) && siteCode is "SITE-101" or "SITE-102")
            {
                issues.Add($"Execution seeder '{ExecutionSeedRelativePath}' is missing SiteComplianceCache for '{siteCode}'.");
            }
        }

        var execSubjectMatch = ExecutionInitialSubjectRegex().Match(executionSource);
        if (!execSubjectMatch.Success)
        {
            issues.Add($"Could not parse initial ClinicalSubject.Create seed in '{ExecutionSeedRelativePath}'.");
            return stageBySite;
        }

        string execSubject = execSubjectMatch.Groups["subject"].Value;
        string execStudy = execSubjectMatch.Groups["study"].Value;
        string execSite = execSubjectMatch.Groups["site"].Value;

        if (!string.Equals(execStudy, expectedStudyCode, StringComparison.Ordinal))
        {
            issues.Add(
                $"Execution seeder ClinicalSubject uses study '{execStudy}', expected '{expectedStudyCode}' from '{DemoSeedRelativePath}'.");
        }

        if (kernelParticipants.Count > 0)
        {
            var first = kernelParticipants[0];
            if (!string.Equals(execSubject, first.SubjectCode, StringComparison.Ordinal) ||
                !string.Equals(execSite, first.SiteCode, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Execution seeder initial subject ({execSubject} at {execSite}) drifted from first kernel participant ({first.SubjectCode} at {first.SiteCode}).");
            }
        }

        return stageBySite;
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

    private static string RenderManifestJs(DemoManifestSnapshot snapshot)
    {
        var sb = new StringBuilder(8192);
        sb.Append("// Generated by `pnpm cadence generate-demo-manifest` from:\n");
        sb.Append("// - packages/demo-fixtures/demo-seed.json\n");
        sb.Append("// - apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs\n");
        sb.Append("// - docker/cadence-realm.json\n");
        sb.Append("// Do not edit by hand; `pnpm cadence check demo-manifest` blocks drift.\n\n");

        sb.Append("export const DEMO_MANIFEST = Object.freeze({\n");
        AppendProp(sb, 1, "tenantCode", Quote(snapshot.TenantCode));
        AppendProp(sb, 1, "tenantUuid", Quote(snapshot.TenantUuid));
        AppendProp(sb, 1, "epochIso", Quote(snapshot.EpochIso));
        AppendProp(sb, 1, "epochMs", snapshot.EpochMs.ToString(CultureInfo.InvariantCulture));

        sb.Append("  sponsor: Object.freeze({\n");
        AppendProp(sb, 2, "code", Quote(snapshot.Sponsor.Code));
        AppendProp(sb, 2, "name", Quote(snapshot.Sponsor.Name));
        AppendProp(sb, 2, "organizationUuid", Quote(snapshot.Sponsor.OrganizationUuid));
        sb.Append("  }),\n");

        sb.Append("  study: Object.freeze({\n");
        AppendProp(sb, 2, "code", Quote(snapshot.Study.Code));
        AppendProp(sb, 2, "title", Quote(snapshot.Study.Title));
        AppendProp(sb, 2, "phase", Quote(snapshot.Study.Phase));
        AppendProp(sb, 2, "sponsorCode", Quote(snapshot.Study.SponsorCode));
        AppendProp(sb, 2, "uuid", Quote(snapshot.Study.Uuid));
        AppendProp(sb, 2, "ref", Quote(snapshot.Study.Ref));
        sb.Append("  }),\n");

        sb.Append("  sites: Object.freeze([\n");
        foreach (var site in snapshot.Sites)
        {
            sb.Append("    Object.freeze({\n");
            AppendProp(sb, 3, "code", Quote(site.Code));
            AppendProp(sb, 3, "name", Quote(site.Name));
            AppendProp(sb, 3, "country", Quote(site.Country));
            AppendProp(sb, 3, "institutionCode", Quote(site.InstitutionCode));
            AppendProp(sb, 3, "complianceStage", Quote(site.ComplianceStage));
            AppendProp(sb, 3, "institutionUuid", Quote(site.InstitutionUuid));
            AppendProp(sb, 3, "siteUuid", Quote(site.SiteUuid));
            AppendProp(sb, 3, "studySiteUuid", Quote(site.StudySiteUuid));
            AppendProp(sb, 3, "studySiteRef", Quote(site.StudySiteRef));
            sb.Append("    }),\n");
        }

        sb.Append("  ]),\n");

        sb.Append("  participants: Object.freeze([\n");
        foreach (var participant in snapshot.Participants)
        {
            sb.Append("    Object.freeze({\n");
            AppendProp(sb, 3, "subjectCode", Quote(participant.SubjectCode));
            AppendProp(sb, 3, "siteCode", Quote(participant.SiteCode));
            AppendProp(sb, 3, "enrollmentIndex", participant.EnrollmentIndex.ToString(CultureInfo.InvariantCulture));
            AppendProp(sb, 3, "status", Quote(participant.Status));
            AppendProp(sb, 3, "screenedAt", Quote(participant.ScreenedAtIso));
            AppendProp(sb, 3, "participantUuid", Quote(participant.ParticipantUuid));
            AppendProp(sb, 3, "enrollmentUuid", Quote(participant.EnrollmentUuid));
            AppendProp(sb, 3, "enrollmentRef", Quote(participant.EnrollmentRef));
            sb.Append("    }),\n");
        }

        sb.Append("  ]),\n");

        sb.Append("  roles: Object.freeze([\n");
        foreach (var role in snapshot.Roles)
        {
            sb.Append("    Object.freeze({\n");
            AppendProp(sb, 3, "code", Quote(role.Code));
            AppendProp(sb, 3, "name", Quote(role.Name));
            AppendProp(sb, 3, "roleUuid", Quote(role.RoleUuid));
            AppendFrozenStringArray(sb, 3, "capabilities", role.Capabilities);
            sb.Append("    }),\n");
        }

        sb.Append("  ]),\n");

        sb.Append("  personas: Object.freeze([\n");
        foreach (var persona in snapshot.Personas)
        {
            sb.Append("    Object.freeze({\n");
            AppendProp(sb, 3, "username", Quote(persona.Username));
            AppendProp(sb, 3, "id", Quote(persona.KeycloakSub));
            AppendProp(sb, 3, "email", Quote(persona.Email));
            AppendProp(sb, 3, "firstName", Quote(persona.FirstName));
            AppendProp(sb, 3, "lastName", Quote(persona.LastName));
            AppendProp(sb, 3, "tenantId", Quote(persona.TenantCode));
            AppendProp(sb, 3, "kernelRole", Quote(persona.KernelRoleCode));
            AppendProp(sb, 3, "workflow", Quote(persona.Workflow));
            AppendProp(sb, 3, "workspacePersona", Quote(persona.WorkspacePersona));
            AppendProp(sb, 3, "siteCode", persona.AssignedSiteCode is null ? "null" : Quote(persona.AssignedSiteCode));
            AppendProp(sb, 3, "grantUuid", Quote(persona.GrantUuid));
            AppendFrozenStringArray(sb, 3, "realmRoles", persona.RealmRoles);
            sb.Append("    }),\n");
        }

        sb.Append("  ]),\n");
        sb.Append("});\n");

        return AnnotateDeidFalsePositives(sb.ToString());
    }

    private static void AppendFrozenStringArray(StringBuilder sb, int indentLevel, string key, IReadOnlyList<string> values)
    {
        string prefix = $"{new string(' ', indentLevel * 2)}{key}: Object.freeze([";
        string singleLine = $"{prefix}{string.Join(", ", values.Select(Quote))}]),";
        if (singleLine.Length <= 80)
        {
            sb.Append(singleLine).Append('\n');
            return;
        }

        sb.Append(prefix).Append('\n');
        string itemIndent = new string(' ', (indentLevel + 1) * 2);
        foreach (string item in values)
        {
            sb.Append(itemIndent).Append(Quote(item)).Append(",\n");
        }

        sb.Append(' ', indentLevel * 2).Append("]),\n");
    }

    private static string AnnotateDeidFalsePositives(string content)
    {
        var detector = new DeidDetector();
        string[] lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (DeidComplianceService.ScanText(ManifestJsRelativePath, line, detector).Count > 0)
            {
                lines[i] = $"{line} // deid-ignore: synthetic demo manifest fixture";
            }
        }

        return string.Join('\n', lines);
    }

    private static string RenderManifestDts() =>
        """
        // Generated by `pnpm cadence generate-demo-manifest`. Do not edit by hand.

        export interface DemoSponsorFixture {
          readonly code: string;
          readonly name: string;
          readonly organizationUuid: string;
        }

        export interface DemoStudyFixture {
          readonly code: string;
          readonly title: string;
          readonly phase: string;
          readonly sponsorCode: string;
          readonly uuid: string;
          readonly ref: string;
        }

        export interface DemoSiteFixture {
          readonly code: string;
          readonly name: string;
          readonly country: string;
          readonly institutionCode: string;
          readonly complianceStage: string;
          readonly institutionUuid: string;
          readonly siteUuid: string;
          readonly studySiteUuid: string;
          readonly studySiteRef: string;
        }

        export interface DemoParticipantFixture {
          readonly subjectCode: string;
          readonly siteCode: string;
          readonly enrollmentIndex: number;
          readonly status: string;
          readonly screenedAt: string;
          readonly participantUuid: string;
          readonly enrollmentUuid: string;
          readonly enrollmentRef: string;
        }

        export interface DemoRoleFixture {
          readonly code: string;
          readonly name: string;
          readonly roleUuid: string;
          readonly capabilities: readonly string[];
        }

        export interface DemoPersonaFixture {
          readonly username: string;
          readonly id: string;
          readonly email: string;
          readonly firstName: string;
          readonly lastName: string;
          readonly tenantId: string;
          readonly kernelRole: string;
          readonly workflow: string;
          readonly workspacePersona: string;
          readonly siteCode: string | null;
          readonly grantUuid: string;
          readonly realmRoles: readonly string[];
        }

        export interface DemoManifestFixture {
          readonly tenantCode: string;
          readonly tenantUuid: string;
          readonly epochIso: string;
          readonly epochMs: number;
          readonly sponsor: DemoSponsorFixture;
          readonly study: DemoStudyFixture;
          readonly sites: readonly DemoSiteFixture[];
          readonly participants: readonly DemoParticipantFixture[];
          readonly roles: readonly DemoRoleFixture[];
          readonly personas: readonly DemoPersonaFixture[];
        }

        export declare const DEMO_MANIFEST: DemoManifestFixture;
        """.ReplaceLineEndings("\n") + "\n";

    private static void AppendProp(StringBuilder sb, int indentLevel, string key, string rawValue)
    {
        sb.Append(' ', indentLevel * 2).Append(key).Append(": ").Append(rawValue).Append(",\n");
    }

    private static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append(@"\n");
                    break;
                case '\r':
                    sb.Append(@"\r");
                    break;
                case '\t':
                    sb.Append(@"\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

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

    private static void WriteUtf8NoBom(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static string FindRepoRoot() => RepositoryRoot.Find();

    [GeneratedRegex(
        @"SiteComplianceCache\.Create\(\s*new\s+StudyCode\(""(?<study>[^""]+)""\)\s*,\s*new\s+SiteCode\(""(?<site>[^""]+)""\)\s*,\s*""(?<stage>[^""]+)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExecutionSiteComplianceRegex();

    [GeneratedRegex(
        @"ClinicalSubject\.Create\(\s*subjectCode:\s*new\s+SubjectCode\(""(?<subject>[^""]+)""\)\s*,\s*studyCode:\s*new\s+StudyCode\(""(?<study>[^""]+)""\)\s*,\s*siteCode:\s*new\s+SiteCode\(""(?<site>[^""]+)""\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExecutionInitialSubjectRegex();

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

    internal sealed record DemoSiteSeed(string Code, string Name, string Country, string InstitutionCode);

    internal sealed record DemoParticipantSeed(string SubjectCode, string SiteCode);

    internal sealed record DemoRoleSeed(string Code, string Name, string[] Capabilities);

    internal sealed record DemoUserGrantSeed(string Username, string Subject, string Role, string? Site);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DemoSeedDocument))]
    internal sealed partial class DemoSeedJsonContext : JsonSerializerContext;
}

/// <summary>Canonical snapshot of the synthetic demo study exported to <c>packages/demo-fixtures</c>.</summary>
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
