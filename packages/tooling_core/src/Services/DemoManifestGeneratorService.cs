using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Deid;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Exports and validates the canonical synthetic demo study manifest (<c>packages/demo-fixtures/src/manifest.js</c> and
/// <c>manifest.d.ts</c>) from the backend development seed (<c>apps/kernel-api/src/DevelopmentDemoData.cs</c>), the
/// Execution seeder (<c>apps/execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs</c>), and the local Keycloak
/// realm (<c>docker/cadence-realm.json</c>). Obeys K-TIME-3 by deriving all timestamps from the fixed
/// <c>2026-01-05T09:00:00Z</c> epoch (never the wall clock).
/// </summary>
public static partial class DemoManifestGeneratorService
{
    /// <summary>The canonical demo epoch from <c>DevelopmentDemoData.Epoch</c> (ADR-2543).</summary>
    public static readonly DateTimeOffset CanonicalEpoch = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Relative path to the kernel demo study seed.</summary>
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
                    Console.WriteLine($"[DEMO-MANIFEST DRIFT] '{ManifestJsRelativePath}' is out of sync with the backend demo seed. Run 'pnpm cadence generate-demo-manifest'.");
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
                    Console.WriteLine($"[DEMO-MANIFEST DRIFT] '{ManifestDtsRelativePath}' is out of sync with the backend demo seed. Run 'pnpm cadence generate-demo-manifest'.");
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

        string kernelSeedPath = Path.Combine(repoRoot, KernelSeedRelativePath);
        string executionSeedPath = Path.Combine(repoRoot, ExecutionSeedRelativePath);
        string realmPath = Path.Combine(repoRoot, RealmRelativePath);

        if (!File.Exists(kernelSeedPath))
        {
            issues.Add($"Required kernel seed file not found: '{KernelSeedRelativePath}'.");
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

        string kernelSource = File.ReadAllText(kernelSeedPath, Encoding.UTF8);
        string executionSource = File.ReadAllText(executionSeedPath, Encoding.UTF8);
        string realmJson = File.ReadAllText(realmPath, Encoding.UTF8);

        snapshot = ParseAndValidateSeeds(kernelSource, executionSource, realmJson, issues);
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
        string kernelSource,
        string executionSource,
        string realmJson,
        List<string> issues)
    {
        var studyCodeMatch = StudyCodeRegex().Match(kernelSource);
        if (!studyCodeMatch.Success)
        {
            issues.Add($"Could not parse StudyCode from '{KernelSeedRelativePath}'.");
            return null;
        }

        string studyCode = studyCodeMatch.Groups["code"].Value;

        var epochMatch = EpochRegex().Match(kernelSource);
        if (!epochMatch.Success)
        {
            issues.Add($"Could not parse fixed DateTimeOffset Epoch from '{KernelSeedRelativePath}'.");
            return null;
        }

        var parsedEpoch = new DateTimeOffset(
            int.Parse(epochMatch.Groups["y"].Value, CultureInfo.InvariantCulture),
            int.Parse(epochMatch.Groups["mo"].Value, CultureInfo.InvariantCulture),
            int.Parse(epochMatch.Groups["d"].Value, CultureInfo.InvariantCulture),
            int.Parse(epochMatch.Groups["h"].Value, CultureInfo.InvariantCulture),
            int.Parse(epochMatch.Groups["mi"].Value, CultureInfo.InvariantCulture),
            int.Parse(epochMatch.Groups["s"].Value, CultureInfo.InvariantCulture),
            TimeSpan.Zero);

        if (parsedEpoch != CanonicalEpoch)
        {
            issues.Add(
                $"Epoch in '{KernelSeedRelativePath}' ({FormatUtcIso(parsedEpoch)}) drifted from canonical 2026-01-05T09:00:00Z epoch."); // deid-ignore: error message naming canonical demo epoch
        }

        var sponsorMatch = SponsorRegex().Match(kernelSource);
        if (!sponsorMatch.Success)
        {
            issues.Add($"Could not parse RegisterOrganization sponsor registration from '{KernelSeedRelativePath}'.");
            return null;
        }

        string sponsorCode = sponsorMatch.Groups["code"].Value;
        string sponsorName = sponsorMatch.Groups["name"].Value;

        var studyRegMatch = RegisterStudyRegex().Match(kernelSource);
        if (!studyRegMatch.Success)
        {
            issues.Add($"Could not parse RegisterStudy registration from '{KernelSeedRelativePath}'.");
            return null;
        }

        string studyTitle = studyRegMatch.Groups["title"].Value;
        string studyPhase = studyRegMatch.Groups["phase"].Value;

        var rawSites = ParseSites(kernelSource, issues);
        var rawParticipants = ParseParticipants(kernelSource, issues);
        var rawRoles = ParseRoles(kernelSource, issues);
        var rawUsers = ParseKernelUsers(kernelSource, issues);
        var executionStages = ParseExecutionSeed(executionSource, studyCode, rawSites, rawParticipants, issues);
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

        var sites = new List<DemoSiteEntry>(rawSites.Count);
        var siteSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, name, country) in rawSites)
        {
            siteSet.Add(code);
            string instUuid = DeriveUuidV8(tenantUuid, "organization", code).ToString("D");
            string siteUuid = DeriveUuidV8(tenantUuid, "site", code).ToString("D");
            string studySiteUuid = DeriveUuidV8(tenantUuid, "study-site", code).ToString("D");
            string complianceStage = executionStages.GetValueOrDefault(code, "CONDUCT");
            sites.Add(new DemoSiteEntry(
                Code: code,
                Name: name,
                Country: country,
                InstitutionCode: $"INST-{code}",
                ComplianceStage: complianceStage,
                InstitutionUuid: instUuid,
                SiteUuid: siteUuid,
                StudySiteUuid: studySiteUuid,
                StudySiteRef: $"study_site:{studySiteUuid}"));
        }

        var participants = new List<DemoParticipantEntry>(rawParticipants.Count);
        for (int i = 0; i < rawParticipants.Count; i++)
        {
            var (subjectCode, siteCode) = rawParticipants[i];
            if (!siteSet.Contains(siteCode))
            {
                issues.Add($"Participant '{subjectCode}' in '{KernelSeedRelativePath}' references unknown site '{siteCode}'.");
                continue;
            }

            string participantUuid = DeriveUuidV8(tenantUuid, "participant", subjectCode).ToString("D");
            string enrollmentUuid = DeriveUuidV8(tenantUuid, "enrollment", subjectCode).ToString("D");
            string screenedAt = FormatUtcIso(parsedEpoch.AddDays(7 + i));
            participants.Add(new DemoParticipantEntry(
                SubjectCode: subjectCode,
                SiteCode: siteCode,
                EnrollmentIndex: i,
                Status: "SCREENED",
                ScreenedAtIso: screenedAt,
                ParticipantUuid: participantUuid,
                EnrollmentUuid: enrollmentUuid,
                EnrollmentRef: $"enrollment:{enrollmentUuid}"));
        }

        var roles = rawRoles
            .Select(r => new DemoRoleEntry(
                Code: r.Code,
                Name: r.Name,
                RoleUuid: DeriveUuidV8(tenantUuid, "role", r.Code).ToString("D"),
                Capabilities: r.Capabilities))
            .ToList();
        var roleSet = roles.Select(r => r.Code).ToHashSet(StringComparer.Ordinal);

        var personas = new List<DemoPersonaEntry>(rawUsers.Count);
        var seenRealmUsers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (username, subjectUuid, roleCode, assignedSiteCode) in rawUsers)
        {
            if (!roleSet.Contains(roleCode))
            {
                issues.Add($"Kernel demo user '{username}' references undefined role '{roleCode}'.");
                continue;
            }

            if (assignedSiteCode is not null && !siteSet.Contains(assignedSiteCode))
            {
                issues.Add($"Kernel demo user '{username}' references undefined site '{assignedSiteCode}'.");
                continue;
            }

            if (!realmUsers.TryGetValue(username, out var realmUser))
            {
                issues.Add($"Kernel demo user '{username}' in '{KernelSeedRelativePath}' is missing from '{RealmRelativePath}'.");
                continue;
            }

            seenRealmUsers.Add(username);
            if (!string.Equals(realmUser.KeycloakSub, subjectUuid, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"Subject UUID mismatch for '{username}': '{KernelSeedRelativePath}' has '{subjectUuid}' " +
                    $"but '{RealmRelativePath}' has '{realmUser.KeycloakSub}'.");
                continue;
            }

            if (assignedSiteCode is not null &&
                realmUser.AssignedSiteCode is not null &&
                !string.Equals(assignedSiteCode, realmUser.AssignedSiteCode, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Site attribute mismatch for '{username}': '{KernelSeedRelativePath}' grants '{assignedSiteCode}' " +
                    $"but '{RealmRelativePath}' has '{realmUser.AssignedSiteCode}'.");
                continue;
            }

            var (workflow, workspacePersona) = ResolveWorkflowAndWorkspace(roleCode, realmUser.RealmRoles);
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
            if (!seenRealmUsers.Contains(realmUsername))
            {
                issues.Add($"Realm user '{realmUsername}' in '{RealmRelativePath}' has no role grant in '{KernelSeedRelativePath}'.");
            }
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

    private static List<(string Code, string Name, string Country)> ParseSites(string kernelSource, List<string> issues)
    {
        string? block = ExtractArrayBlock(kernelSource, "Sites");
        if (block is null)
        {
            issues.Add($"Could not locate Sites array in '{KernelSeedRelativePath}'.");
            return [];
        }

        var sites = ThreeStringTupleRegex()
            .Matches(block)
            .Select(match => (match.Groups["a"].Value, match.Groups["b"].Value, match.Groups["c"].Value))
            .ToList();

        if (sites.Count == 0)
        {
            issues.Add($"Parsed zero sites from '{KernelSeedRelativePath}'.");
        }

        return sites;
    }

    private static List<(string SubjectCode, string SiteCode)> ParseParticipants(string kernelSource, List<string> issues)
    {
        string? block = ExtractArrayBlock(kernelSource, "Participants");
        if (block is null)
        {
            issues.Add($"Could not locate Participants array in '{KernelSeedRelativePath}'.");
            return [];
        }

        var participants = TwoStringTupleRegex()
            .Matches(block)
            .Select(match => (match.Groups["a"].Value, match.Groups["b"].Value))
            .ToList();

        if (participants.Count == 0)
        {
            issues.Add($"Parsed zero participants from '{KernelSeedRelativePath}'.");
        }

        return participants;
    }

    private static List<(string Code, string Name, IReadOnlyList<string> Capabilities)> ParseRoles(string kernelSource, List<string> issues)
    {
        string? block = ExtractArrayBlock(kernelSource, "Roles");
        if (block is null)
        {
            issues.Add($"Could not locate Roles array in '{KernelSeedRelativePath}'.");
            return [];
        }

        var roles = RoleTupleRegex()
            .Matches(block)
            .Select(match =>
            {
                IReadOnlyList<string> caps = QuotedStringRegex()
                    .Matches(match.Groups["caps"].Value)
                    .Select(m => m.Groups["v"].Value)
                    .ToList();
                return (match.Groups["code"].Value, match.Groups["name"].Value, caps);
            })
            .ToList();

        if (roles.Count == 0)
        {
            issues.Add($"Parsed zero roles from '{KernelSeedRelativePath}'.");
        }

        return roles;
    }

    private static List<(string Username, string SubjectUuid, string RoleCode, string? SiteCode)> ParseKernelUsers(string kernelSource, List<string> issues)
    {
        string? block = ExtractArrayBlock(kernelSource, "Users");
        if (block is null)
        {
            issues.Add($"Could not locate Users array in '{KernelSeedRelativePath}'.");
            return [];
        }

        var users = UserTupleRegex()
            .Matches(block)
            .Select(match =>
            {
                string? site = match.Groups["site"].Success ? match.Groups["site"].Value : null;
                return (
                    match.Groups["user"].Value,
                    match.Groups["sub"].Value,
                    match.Groups["role"].Value,
                    site);
            })
            .ToList();

        if (users.Count == 0)
        {
            issues.Add($"Parsed zero users from '{KernelSeedRelativePath}'.");
        }

        return users;
    }

    private static Dictionary<string, string> ParseExecutionSeed(
        string executionSource,
        string expectedStudyCode,
        IReadOnlyList<(string Code, string Name, string Country)> kernelSites,
        IReadOnlyList<(string SubjectCode, string SiteCode)> kernelParticipants,
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
                    $"Execution seeder SiteComplianceCache uses study '{study}', expected '{expectedStudyCode}' from '{KernelSeedRelativePath}'.");
            }

            stageBySite[site] = stage;
        }

        foreach (var (siteCode, _, _) in kernelSites)
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
                $"Execution seeder ClinicalSubject uses study '{execStudy}', expected '{expectedStudyCode}' from '{KernelSeedRelativePath}'.");
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
            if (userEl.TryGetProperty("attributes", out var attrsEl) && attrsEl.ValueKind == JsonValueKind.Object)
            {
                tenantCode = GetFirstAttribute(attrsEl, "tenant_id") ?? tenantCode;
                siteCode = GetFirstAttribute(attrsEl, "site_id");
            }

            result[username] = new RealmUserSeed(
                Username: username,
                KeycloakSub: sub,
                Email: email,
                FirstName: firstName,
                LastName: lastName,
                TenantCode: tenantCode,
                AssignedSiteCode: siteCode,
                RealmRoles: realmRoles);
        }

        return result;
    }

    private static (string Workflow, string WorkspacePersona) ResolveWorkflowAndWorkspace(
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
            _ => (kernelRoleCode.ToLowerInvariant(), "super_admin"),
        };

    private static string RenderManifestJs(DemoManifestSnapshot snapshot)
    {
        var sb = new StringBuilder(8192);
        sb.Append("// Generated by `pnpm cadence generate-demo-manifest` from:\n");
        sb.Append("// - apps/kernel-api/src/DevelopmentDemoData.cs\n");
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

    private static string? ExtractArrayBlock(string source, string fieldName)
    {
        var header = Regex.Match(
            source,
            $@"\b{Regex.Escape(fieldName)}\s*=\s*\[",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!header.Success)
        {
            return null;
        }

        int depth = 1;
        int start = header.Index + header.Length;
        bool inString = false;
        for (int i = start; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '"' && (i == 0 || source[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..i];
                }
            }
        }

        return null;
    }

    private static string GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? string.Empty
            : string.Empty;

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

    [GeneratedRegex(@"StudyCode\s*=\s*""(?<code>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex StudyCodeRegex();

    [GeneratedRegex(
        @"DateTimeOffset\s+Epoch\s*=\s*new\(\s*(?<y>\d+)\s*,\s*(?<mo>\d+)\s*,\s*(?<d>\d+)\s*,\s*(?<h>\d+)\s*,\s*(?<mi>\d+)\s*,\s*(?<s>\d+)\s*,\s*TimeSpan\.Zero\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex EpochRegex();

    [GeneratedRegex(
        @"new\s+RegisterOrganization\([^,]+,\s*ids\.Organization\(""sponsor""\)\s*,\s*""(?<code>[^""]+)""\s*,\s*""(?<name>[^""]+)""\s*,\s*OrganizationKind\.Sponsor",
        RegexOptions.CultureInvariant)]
    private static partial Regex SponsorRegex();

    [GeneratedRegex(
        @"new\s+RegisterStudy\([^,]+,\s*ids\.Study\(StudyCode\)\s*,\s*sponsor\s*,\s*StudyCode\s*,\s*""(?<title>[^""]+)""\s*,\s*""(?<phase>[^""]+)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex RegisterStudyRegex();

    [GeneratedRegex(
        @"\(\s*""(?<a>[^""]+)""\s*,\s*""(?<b>[^""]+)""\s*,\s*""(?<c>[^""]+)""\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ThreeStringTupleRegex();

    [GeneratedRegex(
        @"\(\s*""(?<a>[^""]+)""\s*,\s*""(?<b>[^""]+)""\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TwoStringTupleRegex();

    [GeneratedRegex(
        @"\(\s*""(?<code>[^""]+)""\s*,\s*""(?<name>[^""]+)""\s*,\s*\[(?<caps>[^\]]*)\]\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex RoleTupleRegex();

    [GeneratedRegex(@"""(?<v>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedStringRegex();

    [GeneratedRegex(
        @"\(\s*""(?<user>[^""]+)""\s*,\s*""(?<sub>[^""]+)""\s*,\s*""(?<role>[^""]+)""\s*,\s*(?:""(?<site>[^""]+)""|null)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex UserTupleRegex();

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
        IReadOnlyList<string> RealmRoles);
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
