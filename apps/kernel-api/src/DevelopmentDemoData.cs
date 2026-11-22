using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Host;

/// <summary>
/// The local stack's demo study in the development tenant (ADR-2543, #5452): a sponsor, the study
/// <see cref="StudyCode"/> with an approved and effective protocol version, two activated study sites with screened
/// participants matching what the Execution service seeds, and grants
/// that let the local realm's users read it, the coordinator only at their own site. Everything is written through
/// the command pipeline as the development bootstrap's service account, which first grants itself a role for the
/// writes. Ids are derived from the tenant and each record's name, and every value is fixed, so each command has the
/// same payload on every start: a later start replays the stored results instead of writing again, and a start
/// interrupted halfway finishes the rest.
/// </summary>
internal static class DevelopmentDemoData
{
    /// <summary>The demo study's code: the study the Execution service seeds (<c>ExecutionDbSeeder</c>).</summary>
    public const string StudyCode = "CADENCE-101";

    /// <summary>When the demo world's records take effect in valid time; fixed so every start sends the same payload.</summary>
    internal static readonly DateTimeOffset Epoch = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private const string Reason = "Seed the local development stack's demo study (ADR-2543)";
    private const string DesignerLeadSubject = "8808e058-a472-4f72-9970-a027eddf35bc";

    private static Actor DesignerLeadActor() => Actor.Person(DesignerLeadSubject);

    /// <summary>The demo sites: code, name and country; the coordinator and the monitor work at the first.</summary>
    internal static readonly (string Code, string Name, string Country)[] Sites =
    [
        ("SITE-101", "Riverside University Hospital", "US"),
        ("SITE-102", "Lakeshore Medical Center", "US"),
    ];

    /// <summary>The demo participants by subject code and site; the first matches the Execution service's seed.</summary>
    internal static readonly (string SubjectCode, string Site)[] Participants =
    [
        ("SUBJ-101-001", "SITE-101"),
        ("SUBJ-101-002", "SITE-101"),
        ("SUBJ-102-001", "SITE-102"),
    ];

    /// <summary>The roles the realm's users are granted: code, name and capabilities.</summary>
    internal static readonly (string Code, string Name, string[] Capabilities)[] Roles =
    [
        ("DEMO_CRC", "Clinical research coordinator", ["participant.register", "participant.screen", "enrollment.change_status", "observation.record", "observation.correct", "collection.open", "collection.record", "execution.query.answer"]),
        ("DEMO_INVESTIGATOR", "Site investigator", ["participant.screen", "observation.record", "observation.correct", "collection.open", "collection.record", "collection.sign", "execution.query.answer"]),
        ("DEMO_MONITOR", "Clinical research associate", ["validation_finding.investigate", "validation_finding.classify", "validation_finding.explain", "observation.verify", "assertion.retract", "execution.query.raise", "execution.query.manage"]),
        ("DEMO_DATA_MANAGER", "Data manager", ["validation_run.record", "validation_finding.accept", "validation_finding.reopen", "observation.lock", "observation.verify", "assertion.retract", "execution.query.raise", "execution.query.manage", "study.declare_standards"]),
        ("DEMO_DESIGNER", "Study designer", ["organization.register", "study.register", "concept.define", "protocol.draft", "protocol.revise_draft", "protocol.submit_for_review", "protocol.approve", "protocol.amend", "standard.register", "standard.approve", "library_item.draft", "library_item.approve", "study.declare_standards", "validation_run.record", "attestation.sign"]),
        ("DEMO_SAFETY", "Safety reviewer", ["validation_finding.investigate", "validation_finding.classify"]),
        ("DEMO_AUDITOR", "Auditor", ["validation_finding.investigate"]),
    ];

    /// <summary>
    /// The local realm's users (<c>docker/cadence-realm.json</c>) by their fixed Keycloak subject id, with the role and
    /// the site they are granted at; a null site grants the role for the whole study (or the tenant for the lead designer
    /// who attests protocol approvals).
    /// </summary>
    internal static readonly (string Username, string Subject, string Role, string? Site)[] Users =
    [
        ("crc.site101", "1a4345cc-364c-4b8b-881e-93bbea528b0a", "DEMO_CRC", "SITE-101"),
        ("pi.site101", "cf64a3b4-33cc-4925-8452-75e29ca9526e", "DEMO_INVESTIGATOR", "SITE-101"),
        ("cra.monitor", "ee2cfdb6-d893-4fb4-a10f-d50830b6728a", "DEMO_MONITOR", "SITE-101"),
        ("dm.lead", "47a9d88f-73d7-4aca-9fd6-14b8e6e9284f", "DEMO_DATA_MANAGER", null),
        ("designer_demo", "d0b6257b-57fe-420b-9526-366e83c756aa", "DEMO_DESIGNER", null),
        ("designer.lead", DesignerLeadSubject, "DEMO_DESIGNER", null),
        ("safety.officer", "3d290711-4167-4da5-b55a-4c75760bd27c", "DEMO_SAFETY", null), // deid-ignore: a demo realm user's fixed subject id, not a telephone number
        ("auditor.lead", "d43095eb-1912-40dd-a528-fa04335a2342", "DEMO_AUDITOR", null),
    ];

    /// <summary>
    /// The Execution module's kernel event consumers (ADR-2622, #5461), each a service account with the role it needs
    /// across the tenant: opening its side of every form the kernel opens, registering pseudonymous participant ids on
    /// site screening, and stating each query step as a kernel assertion. Production grants for module service accounts are #5566.
    /// </summary>
    private static readonly (string Consumer, string Role, string Name, string[] Capabilities)[] ModuleServiceAccounts =
    [
        ("execution-forms", "DEMO_EXECUTION_FORMS", "Execution forms consumer", ["execution.form.open", "participant.register"]),
        ("execution-queries", "DEMO_EXECUTION_QUERIES", "Execution queries consumer", ["observation.query", "observation.query.answer", "assertion.retract"]),
        ("execution-edit-checks", "DEMO_EXECUTION_EDIT_CHECKS", "Execution edit checks consumer", ["validation_run.record", "execution.query.raise", "execution.query.manage"]),
    ];

    /// <summary>The capabilities the bootstrap grants itself, tenant-wide, to write the demo study.</summary>
    private static readonly string[] SeederCapabilities =
    [
        "organization.register",
        "study.register",
        "site.register",
        "study_site.link",
        "study_site.activate",
        "participant.register",
        "participant.screen",
        "concept.define",
        "protocol.draft",
        "protocol.submit_for_review",
        "artifact.register",
        "effectivity.declare",
        "effectivity.change_status",
        "standard.register",
        "library_item.draft",
        "study.declare_standards",
    ];

    /// <summary>Writes the demo study into <paramref name="tenant"/>, or replays what an earlier start wrote.</summary>
    public static async Task SeedAsync(TenantId tenant, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        var ids = new DemoIds(tenant);
        var seeder = new Sender(tenant, scopes);

        var seederRole = await seeder.SendAsync(
            new DefineRole(tenant, ids.Role("DEMO_SEEDER"), "DEMO_SEEDER", "Development demo seeder", SeederCapabilities, Key("role:DEMO_SEEDER"), Reason),
            cancellationToken).ConfigureAwait(false);
        await seeder.SendAsync(
            new IssueGrant(tenant, ids.Grant("seeder"), DevelopmentTenantBootstrap.Operator, null, seederRole.RoleId, AuthorityScope.Tenant(), Epoch, null, false, Key("grant:seeder"), Reason),
            cancellationToken).ConfigureAwait(false);

        var sponsor = await seeder.SendAsync(
            new RegisterOrganization(tenant, ids.Organization("sponsor"), "CADENCE-PHARMA", "Cadence Pharma (demo sponsor)", OrganizationKind.Sponsor, Key("organization:sponsor"), Reason),
            cancellationToken).ConfigureAwait(false);
        var study = await seeder.SendAsync(
            new RegisterStudy(tenant, ids.Study(StudyCode), sponsor, StudyCode, "Cadence demo study: cardiovascular outcomes", "Phase 3", Key($"study:{StudyCode}"), Reason),
            cancellationToken).ConfigureAwait(false);

        var studySites = new Dictionary<string, StudySiteId>(StringComparer.Ordinal);
        foreach (var (code, name, country) in Sites)
        {
            var institution = await seeder.SendAsync(
                new RegisterOrganization(tenant, ids.Organization(code), $"INST-{code}", name, OrganizationKind.SiteInstitution, Key($"organization:{code}"), Reason),
                cancellationToken).ConfigureAwait(false);
            var site = await seeder.SendAsync(
                new RegisterSite(tenant, ids.Site(code), institution, code, name, country, Key($"site:{code}"), Reason), cancellationToken).ConfigureAwait(false);
            studySites[code] = await seeder.SendAsync(
                new LinkSiteToStudy(tenant, ids.StudySite(code), study, site, Key($"study-site:{code}"), Reason), cancellationToken).ConfigureAwait(false);
        }

        var roles = new Dictionary<string, RoleId>(StringComparer.Ordinal);
        foreach (var (code, name, capabilities) in Roles)
        {
            roles[code] = (await seeder.SendAsync(
                new DefineRole(tenant, ids.Role(code), code, name, capabilities, Key($"role:{code}"), Reason), cancellationToken).ConfigureAwait(false)).RoleId;
        }

        foreach (var (consumer, code, name, capabilities) in ModuleServiceAccounts)
        {
            var role = await seeder.SendAsync(
                new DefineRole(tenant, ids.Role(code), code, name, capabilities, Key($"role:{code}"), Reason), cancellationToken).ConfigureAwait(false);
            await seeder.SendAsync(
                new IssueGrant(tenant, ids.Grant($"svc:{consumer}"), Actor.ServiceAccount(consumer), null, role.RoleId, AuthorityScope.Tenant(), Epoch, null, false, Key($"grant:svc:{consumer}"), Reason),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var (username, subject, role, site) in Users)
        {
            AuthorityScope scope;
            if (site is not null)
            {
                scope = AuthorityScope.StudySite(studySites[site]);
            }
            else if (string.Equals(role, "DEMO_DESIGNER", StringComparison.Ordinal))
            {
                scope = AuthorityScope.Tenant();
            }
            else
            {
                scope = AuthorityScope.Study(study);
            }

            await seeder.SendAsync(
                new IssueGrant(tenant, ids.Grant(username), Actor.Person(subject), null, roles[role], scope, Epoch, null, false, Key($"grant:{username}"), Reason),
                cancellationToken).ConfigureAwait(false);
        }

        await SeedEffectiveProtocolAsync(tenant, study, studySites.Values, ids, seeder, scopes, cancellationToken).ConfigureAwait(false);
        await SeedPinnedEditCheckRulePackAsync(tenant, study, ids, seeder, scopes, cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < Participants.Length; i++)
        {
            var (subjectCode, site) = Participants[i];
            var participant = await seeder.SendAsync(
                new RegisterParticipant(tenant, ids.Participant(subjectCode), Key($"participant:{subjectCode}"), Reason), cancellationToken).ConfigureAwait(false);
            SubjectCode.TryFrom(subjectCode, out var code);
            await seeder.SendAsync(
                new ScreenParticipant(tenant, ids.Enrollment(subjectCode), participant, studySites[site], code, Epoch.AddDays(7 + i), Key($"enrollment:{subjectCode}"), Reason),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SeedEffectiveProtocolAsync(
        TenantId tenant,
        StudyId study,
        IEnumerable<StudySiteId> studySites,
        DemoIds ids,
        Sender seeder,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        var systolic = await DefineConceptAsync(tenant, ids, seeder, "sbp", "Systolic blood pressure", cancellationToken).ConfigureAwait(false);
        var diastolic = await DefineConceptAsync(tenant, ids, seeder, "dbp", "Diastolic blood pressure", cancellationToken).ConfigureAwait(false);
        var heartRate = await DefineConceptAsync(tenant, ids, seeder, "hr", "Resting heart rate", cancellationToken).ConfigureAwait(false);
        var examFinding = await DefineConceptAsync(tenant, ids, seeder, "pe-finding", "Physical examination summary", cancellationToken).ConfigureAwait(false);
        var adverseEventTerm = await DefineConceptAsync(tenant, ids, seeder, "ae-term", "Adverse event reported term", cancellationToken).ConfigureAwait(false);
        var adverseEventSeverity = await DefineConceptAsync(tenant, ids, seeder, "ae-severity", "Adverse event severity grade", cancellationToken).ConfigureAwait(false);
        var medicationName = await DefineConceptAsync(tenant, ids, seeder, "cm-name", "Concomitant medication name", cancellationToken).ConfigureAwait(false);
        var medicationIndication = await DefineConceptAsync(tenant, ids, seeder, "cm-indication", "Concomitant medication indication", cancellationToken).ConfigureAwait(false);

        var design = BuildDemoProtocolDesign(
            systolic,
            diastolic,
            heartRate,
            examFinding,
            adverseEventTerm,
            adverseEventSeverity,
            medicationName,
            medicationIndication);

        var versionId = ids.ProtocolVersion("v1");
        await seeder.SendAsync(
            new CreateProtocolDraft(tenant, study, versionId, ProtocolDesignDocument.Write(design), Key("protocol:v1:draft"), Reason),
            cancellationToken).ConfigureAwait(false);
        var inReview = await seeder.SendAsync(
            new SubmitProtocolVersionForReview(tenant, versionId, Key("protocol:v1:submit"), Reason),
            cancellationToken).ConfigureAwait(false);

        if (!await IsProtocolApprovedAsync(tenant, versionId, scopes, cancellationToken).ConfigureAwait(false))
        {
            var signer = DesignerLeadActor();
            DateTimeOffset authenticatedAt;
            await using (var clockScope = scopes.CreateAsyncScope())
            {
                authenticatedAt = clockScope.ServiceProvider.GetRequiredService<IClock>().UtcNow.AddMinutes(-1);
            }

            var target = new AttestationTarget(AttestationTarget.ProtocolVersionKind, versionId.Value, inReview.ContentHash!);
            var signature = await seeder.SendAsAsync(
                signer,
                new Attest(tenant, ids.Attestation("protocol:v1"), AttestationMeaning.Approved, target, authenticatedAt, "pwd+otp", Key("protocol:v1:attest"), Reason),
                cancellationToken).ConfigureAwait(false);
            await seeder.SendAsAsync(
                signer,
                new ApproveProtocolVersion(tenant, versionId, signature, Key("protocol:v1:approve"), Reason),
                cancellationToken).ConfigureAwait(false);
        }

        const string ethicsPayload = "cadence-demo-ethics-approval-v1";
        var ethicsBytes = Encoding.UTF8.GetBytes(ethicsPayload);
        var ethicsVersionId = ids.ArtifactVersion("ethics:v1");
        var ethicsContent = new ArtifactContent(
            ContentHash.OfBytes(ethicsBytes),
            ethicsBytes.Length,
            "application/pdf",
            "blob://demo/ethics-approval-v1.pdf");
        await seeder.SendAsync(
            new RegisterArtifact(
                tenant,
                ids.Artifact("ethics"),
                ethicsVersionId,
                ArtifactKind.EthicsApproval,
                study,
                "Institutional Review Board approval letter",
                ethicsContent,
                Key("artifact:ethics:v1"),
                Reason),
            cancellationToken).ConfigureAwait(false);

        var effectivityId = ids.Effectivity("protocol:v1:study");
        await seeder.SendAsync(
            new DeclareEffectivity(tenant, effectivityId, versionId, EffectivityScope.Study(), Epoch, Key("effectivity:v1:declare"), Reason),
            cancellationToken).ConfigureAwait(false);
        await seeder.SendAsync(
            new ChangeEffectivityStatus(tenant, effectivityId, EffectivityStatus.Effective, Epoch.AddDays(1), [ethicsVersionId], Key("effectivity:v1:effective"), Reason),
            cancellationToken).ConfigureAwait(false);

        foreach (var studySite in studySites)
        {
            await seeder.SendAsync(
                new ActivateStudySite(tenant, studySite, Key($"study-site-activate:{studySite.Value:D}"), Reason),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task<ConceptRef> DefineConceptAsync(
        TenantId tenant, DemoIds ids, Sender seeder, string slug, string label, CancellationToken cancellationToken) =>
        seeder.SendAsync(
            new DefineConcept(
                tenant,
                ids.Concept(slug),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                label,
                $"{label} recorded in the demo cardiovascular study.",
                Key($"concept:{slug}"),
                Reason),
            cancellationToken);

    private static async Task SeedPinnedEditCheckRulePackAsync(
        TenantId tenant,
        StudyId study,
        DemoIds ids,
        Sender seeder,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        var rulePackId = ids.LibraryItem("cadence-101-edit-checks:v1");
        var rulePackJson = RulePackJson.ToJson(BuildDemoRulePack());
        var drafted = await seeder.SendAsync(
            new DraftLibraryItem(
                tenant,
                rulePackId,
                LibraryItemKind.RulePack,
                "cadence-101-edit-checks",
                "CADENCE-101 Cardiovascular Edit Checks",
                rulePackJson,
                [],
                Key("library-item:cadence-101-edit-checks:v1:draft"),
                Reason),
            cancellationToken).ConfigureAwait(false);

        if (!await IsLibraryItemApprovedAsync(tenant, rulePackId, scopes, cancellationToken).ConfigureAwait(false))
        {
            var signer = DesignerLeadActor();
            DateTimeOffset authenticatedAt;
            await using (var clockScope = scopes.CreateAsyncScope())
            {
                authenticatedAt = clockScope.ServiceProvider.GetRequiredService<IClock>().UtcNow.AddMinutes(-1);
            }

            var target = new AttestationTarget(NodeKinds.LibraryItem, rulePackId.Value, drafted.ContentHash);
            var signature = await seeder.SendAsAsync(
                signer,
                new Attest(tenant, ids.Attestation("library-item:cadence-101-edit-checks:v1"), AttestationMeaning.Approved, target, authenticatedAt, "pwd+otp", Key("library-item:cadence-101-edit-checks:v1:attest"), Reason),
                cancellationToken).ConfigureAwait(false);
            await seeder.SendAsAsync(
                signer,
                new ApproveLibraryItem(tenant, rulePackId, signature, Key("library-item:cadence-101-edit-checks:v1:approve"), Reason),
                cancellationToken).ConfigureAwait(false);
        }

        var standardId = await EnsureBaselineStandardApprovedAsync(tenant, ids, seeder, scopes, cancellationToken).ConfigureAwait(false);

        await seeder.SendAsync(
            new DeclareStudyStandards(
                tenant,
                study,
                ids.StudyStandardsProfile(StudyCode),
                [standardId],
                [],
                [rulePackId],
                Key($"study-standards:{StudyCode}:v1"),
                Reason),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StandardVersionId> EnsureBaselineStandardApprovedAsync(
        TenantId tenant,
        DemoIds ids,
        Sender seeder,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        const string code = "CDASHIG";
        const string version = "2.3";
        var standardId = ids.StandardVersion($"{code}:{version}");

        StandardVersionSummary? summary;
        await using (var scope = scopes.CreateAsyncScope())
        {
            summary = await scope.ServiceProvider.GetRequiredService<IStandardsRegistry>()
                .FindStandardVersionAsync(tenant, code, version, cancellationToken)
                .ConfigureAwait(false);
        }

        if (summary is null)
        {
            var package = new StandardPackage(
                code,
                version,
                "CDISC Clinical Data Acquisition Standards Harmonization Implementation Guide v2.3",
                "CDISC",
                "/mdr/cdashig/2-3",
                new DateOnly(2023, 9, 29),
                [
                    new DatasetDefinitionData(
                        "VS",
                        "Vital Signs",
                        "Findings",
                        "One record per vital sign measurement per visit per subject",
                        1,
                        [
                            new VariableDefinitionData(
                                "VSORRES",
                                "Result or Finding in Original Units",
                                1,
                                StandardDataType.Num,
                                "Result Qualifier",
                                VariableCore.Expected,
                                [],
                                [],
                                null,
                                "Recorded vital sign value in collected units."),
                        ]),
                ],
                [],
                []);

            standardId = await seeder.SendAsync(
                new RegisterStandardVersion(tenant, standardId, package, null, Key($"standard:{code}:{version}:register"), Reason),
                cancellationToken).ConfigureAwait(false);

            await using var scope = scopes.CreateAsyncScope();
            summary = await scope.ServiceProvider.GetRequiredService<IStandardsRegistry>()
                .FindStandardVersionAsync(tenant, standardId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (summary is not null && summary.Status != GovernanceStatus.Approved)
        {
            var signer = DesignerLeadActor();
            DateTimeOffset authenticatedAt;
            await using (var clockScope = scopes.CreateAsyncScope())
            {
                authenticatedAt = clockScope.ServiceProvider.GetRequiredService<IClock>().UtcNow.AddMinutes(-1);
            }

            var target = new AttestationTarget(NodeKinds.StandardVersion, summary.Id.Value, summary.ContentHash);
            var signature = await seeder.SendAsAsync(
                signer,
                new Attest(tenant, ids.Attestation($"standard:{code}:{version}"), AttestationMeaning.Approved, target, authenticatedAt, "pwd+otp", Key($"standard:{code}:{version}:attest"), Reason),
                cancellationToken).ConfigureAwait(false);
            await seeder.SendAsAsync(
                signer,
                new ApproveStandardVersion(tenant, summary.Id, signature, Key($"standard:{code}:{version}:approve"), Reason),
                cancellationToken).ConfigureAwait(false);
        }

        return summary?.Id ?? standardId;
    }

    private static async Task<bool> IsLibraryItemApprovedAsync(
        TenantId tenant, LibraryItemId itemId, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var item = await scope.ServiceProvider.GetRequiredService<IStandardsRegistry>()
            .FindLibraryItemAsync(tenant, itemId, cancellationToken)
            .ConfigureAwait(false);
        return item?.Status == GovernanceStatus.Approved;
    }

    private static RulePack BuildDemoRulePack()
    {
        var citation = new SourceCitation("CADENCE-101 Data Validation Plan", "Section 4.1", null);
        var vitalsScope = new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.vitals");
        var peScope = new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.physical_exam");
        var sbp = new VariableExpression("item.sbp");
        var dbp = new VariableExpression("item.dbp");
        var hr = new VariableExpression("item.hr");
        var peSig = new VariableExpression("item.pe_clinically_significant");
        var aeTerm = new VariableExpression("cs.adverse_event.item.ae_term");

        return new RulePack(
            RuleAuthority.Study,
            "CADENCE-101 Cardiovascular Edit Checks",
            [
                new Rule(
                    "VS-SBP-RANGE",
                    1,
                    RuleAuthority.Study,
                    citation,
                    vitalsScope,
                    ValidationSeverity.Error,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [sbp]),
                            new LogicalExpression(
                                LogicalOperator.Or,
                                [
                                    new ComparisonExpression(ComparisonOperator.Less, sbp, new LiteralExpression(RuleValue.FromNumber(70m))),
                                    new ComparisonExpression(ComparisonOperator.Greater, sbp, new LiteralExpression(RuleValue.FromNumber(240m))),
                                ]),
                        ]),
                    "Systolic blood pressure is outside [70, 240] mm[Hg].",
                    "Verify seated cuff measurement against source vital signs sheet or explain out-of-range value.",
                    EffectivePeriod.Always),
                new Rule(
                    "VS-DBP-LT-SBP",
                    1,
                    RuleAuthority.Study,
                    citation,
                    vitalsScope,
                    ValidationSeverity.Error,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [dbp]),
                            new FunctionExpression(RuleFunction.IsNotEmpty, [sbp]),
                            new ComparisonExpression(ComparisonOperator.GreaterOrEqual, dbp, sbp),
                        ]),
                    "Diastolic blood pressure must be strictly less than systolic blood pressure.",
                    "Confirm systolic and diastolic blood pressure values were not transposed.",
                    EffectivePeriod.Always),
                new Rule(
                    "VS-HR-RANGE",
                    1,
                    RuleAuthority.Study,
                    citation,
                    vitalsScope,
                    ValidationSeverity.Warning,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [hr]),
                            new LogicalExpression(
                                LogicalOperator.Or,
                                [
                                    new ComparisonExpression(ComparisonOperator.Less, hr, new LiteralExpression(RuleValue.FromNumber(40m))),
                                    new ComparisonExpression(ComparisonOperator.Greater, hr, new LiteralExpression(RuleValue.FromNumber(150m))),
                                ]),
                        ]),
                    "Resting heart rate is outside expected physiological window [40, 150] /min.",
                    "Confirm resting pulse duration and document bradycardia or tachycardia if clinically accurate.",
                    EffectivePeriod.Always),
                new Rule(
                    "PE-CLIN-SIG-AE",
                    1,
                    RuleAuthority.Study,
                    citation,
                    peScope,
                    ValidationSeverity.Warning,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [peSig]),
                            new ComparisonExpression(ComparisonOperator.Equal, peSig, new LiteralExpression(RuleValue.True)),
                            new FunctionExpression(RuleFunction.IsEmpty, [aeTerm]),
                        ]),
                    "Clinically significant physical examination finding recorded without an Adverse Event log entry.",
                    "Open an Adverse Event Log form for the enrollment or confirm the finding represents baseline medical history.",
                    EffectivePeriod.Always),
            ]);
    }

    private static async Task<bool> IsProtocolApprovedAsync(
        TenantId tenant, ProtocolVersionId versionId, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var version = await scope.ServiceProvider.GetRequiredService<IProtocolVersionReader>()
            .GetAsync(tenant, versionId, cancellationToken)
            .ConfigureAwait(false);
        return version?.Status == ProtocolVersionStatus.Approved;
    }

    private static ProtocolDesign BuildDemoProtocolDesign(
        ConceptRef systolic,
        ConceptRef diastolic,
        ConceptRef heartRate,
        ConceptRef examFinding,
        ConceptRef adverseEventTerm,
        ConceptRef adverseEventSeverity,
        ConceptRef medicationName,
        ConceptRef medicationIndication) =>
        new()
        {
            Title = "Cardiovascular outcomes clinical protocol",
            Objectives =
            [
                new Objective("obj.primary", ObjectiveLevel.Primary, "Evaluate blood pressure control over 4 weeks of treatment."),
            ],
            Endpoints =
            [
                new Cadence.Kernel.Protocol.Design.Endpoint("ep.sbp_change", "obj.primary", ObjectiveLevel.Primary, "Change from baseline in systolic blood pressure at Week 4.", systolic),
            ],
            Estimands =
            [
                new Estimand(
                    "est.primary",
                    "ep.sbp_change",
                    "pop.itt",
                    ["arm.active", "arm.placebo"],
                    "Change in seated systolic blood pressure from Day 1 to Week 4",
                    [new IntercurrentEvent("Treatment discontinuation", IntercurrentEventStrategy.TreatmentPolicy)],
                    "Difference in mean change between arms"),
            ],
            Populations =
            [
                new Population("pop.itt", "Intent-to-treat", ["crit.htn"]),
            ],
            EligibilityCriteria =
            [
                new EligibilityCriterion("crit.htn", CriterionCategory.Inclusion, "Seated systolic blood pressure >= 140 mm[Hg] at screening.", [systolic]),
            ],
            Interventions =
            [
                new Intervention("int.investigational", "Investigational antihypertensive", Dose: new Dose(10m, "mg"), Route: "Oral", Regimen: "Once daily"),
                new Intervention("int.placebo", "Matching placebo", Route: "Oral", Regimen: "Once daily"),
            ],
            Arms =
            [
                new Arm("arm.active", "Active treatment", ArmType.Experimental, ["int.investigational"]),
                new Arm("arm.placebo", "Placebo", ArmType.PlaceboComparator, ["int.placebo"]),
            ],
            SafetyRules =
            [
                new ProtocolRule("rule.safety_monitoring", "Monitor treatment-emergent adverse events and concomitant medications.", [adverseEventTerm, medicationName]),
            ],
            Epochs =
            [
                new Epoch("ep.screening", "Screening", 1),
                new Epoch("ep.treatment", "Treatment", 2),
                new Epoch("ep.followup", "Follow-up", 3),
            ],
            PlannedActivities =
            [
                new PlannedActivity("act.screening", "Screening Visit", "ep.screening", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(-7, 7, 0)),
                new PlannedActivity("act.day1", "Day 1 Baseline", "ep.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(1, 0, 1)),
                new PlannedActivity("act.week2", "Week 2 Visit", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(14, 2, 2)),
                new PlannedActivity("act.week4", "Week 4 Primary Endpoint", "ep.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(28, 3, 3)),
                new PlannedActivity("act.eos", "End of Study", "ep.followup", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(42, 3, 5)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [
                        new CollectionItem("item.sbp", systolic, "mm[Hg]", Required: true),
                        new CollectionItem("item.dbp", diastolic, "mm[Hg]", Required: true),
                        new CollectionItem("item.hr", heartRate, "/min", Required: true),
                        new CollectionItem("item.vs_notes", systolic, ValueKind: ObservationValueKind.Text),
                    ],
                    new DataPurpose { EndpointIds = ["ep.sbp_change"], EstimandIds = ["est.primary"] }),
                new CollectionSpec(
                    "cs.physical_exam",
                    "Physical Examination",
                    [
                        new CollectionItem("item.pe_summary", examFinding, Required: true, ValueKind: ObservationValueKind.Text),
                        new CollectionItem("item.pe_clinically_significant", examFinding, Required: true, ValueKind: ObservationValueKind.Boolean),
                    ],
                    new DataPurpose { OperationalNeed = "Baseline and follow-up clinical assessment" }),
                new CollectionSpec(
                    "cs.adverse_event",
                    "Adverse Event Log",
                    [
                        new CollectionItem("item.ae_term", adverseEventTerm, Required: true, ValueKind: ObservationValueKind.Text),
                        new CollectionItem("item.ae_severity", adverseEventSeverity, Required: true, ValueKind: ObservationValueKind.Text),
                    ],
                    new DataPurpose { SafetyRuleIds = ["rule.safety_monitoring"] }),
                new CollectionSpec(
                    "cs.concomitant_meds",
                    "Concomitant Medications",
                    [
                        new CollectionItem("item.cm_name", medicationName, Required: true, ValueKind: ObservationValueKind.Text),
                        new CollectionItem("item.cm_indication", medicationIndication, ValueKind: ObservationValueKind.Text),
                    ],
                    new DataPurpose { SafetyRuleIds = ["rule.safety_monitoring"] }),
            ],
        };

    private static string Key(string record) => $"development-demo:{record}";

    /// <summary>Sends each command in its own scope as the bootstrap's service account; a refusal stops the host.</summary>
    private sealed class Sender(TenantId tenant, IServiceScopeFactory scopes)
    {
        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken) =>
            SendAsAsync(DevelopmentTenantBootstrap.Operator, command, cancellationToken);

        public async Task<TResult> SendAsAsync<TResult>(Actor actor, ICommand<TResult> command, CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ServiceActorScope>().Actor = actor;
            var result = await services.GetRequiredService<IKernelCommandSender>().SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? result.Value!
                : throw new InvalidOperationException(
                    $"Could not seed the demo study in tenant {tenant}: {command.GetType().Name} was refused with {result.Error?.Code} ({result.Error?.Message}).");
        }
    }

    /// <summary>
    /// Ids derived from the tenant and a record's name (an RFC 9562 version 8 UUID over SHA-256), so a later start names
    /// the same records.
    /// </summary>
    private sealed class DemoIds(TenantId tenant)
    {
        public OrganizationId Organization(string name) => OrganizationId.FromGuid(Derive("organization", name));

        public StudyId Study(string name) => StudyId.FromGuid(Derive("study", name));

        public SiteId Site(string name) => SiteId.FromGuid(Derive("site", name));

        public StudySiteId StudySite(string name) => StudySiteId.FromGuid(Derive("study-site", name));

        public ParticipantId Participant(string name) => ParticipantId.FromGuid(Derive("participant", name));

        public EnrollmentId Enrollment(string name) => EnrollmentId.FromGuid(Derive("enrollment", name));

        public RoleId Role(string name) => RoleId.FromGuid(Derive("role", name));

        public GrantId Grant(string name) => GrantId.FromGuid(Derive("grant", name));

        public ConceptId Concept(string name) => ConceptId.FromGuid(Derive("concept", name));

        public ProtocolVersionId ProtocolVersion(string name) => ProtocolVersionId.FromGuid(Derive("protocol-version", name));

        public AttestationId Attestation(string name) => AttestationId.FromGuid(Derive("attestation", name));

        public ArtifactId Artifact(string name) => ArtifactId.FromGuid(Derive("artifact", name));

        public ArtifactVersionId ArtifactVersion(string name) => ArtifactVersionId.FromGuid(Derive("artifact-version", name));

        public EffectivityId Effectivity(string name) => EffectivityId.FromGuid(Derive("effectivity", name));

        public LibraryItemId LibraryItem(string name) => LibraryItemId.FromGuid(Derive("library-item", name));

        public StandardVersionId StandardVersion(string name) => StandardVersionId.FromGuid(Derive("standard-version", name));

        public StudyStandardsProfileId StudyStandardsProfile(string name) => StudyStandardsProfileId.FromGuid(Derive("study-standards-profile", name));

        private Guid Derive(string kind, string name)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-development-demo:{tenant}:{kind}:{name}"), hash);
            var bytes = hash[..16];
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes, bigEndian: true);
        }
    }
}
