using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Search;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Search;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Standards;
using Cadence.Standards;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Kernel search against PostgreSQL (ADR-2484): code and title matches across studies, sites, organizations,
/// concepts and terminology codes, NodeRef results, tenant isolation through Row-Level Security, and the GIN indexes
/// the migration creates. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelSearchPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Integration test";

    /// <summary>
    /// A study, its site and its organization are found by words of their titles and by their codes; results are
    /// NodeRefs pinned to the version read, and an exact code ranks first.
    /// </summary>
    [Fact(DisplayName = "Search_FindsStudiesSitesAndOrganizations")]
    [KernelRule("K-ID-8")]
    public async Task SearchFindsStudiesSitesAndOrganizations()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seeded = await SeedTenantAsync("SRCH", "Hypertension");

        var byTitle = await Search().SearchAsync(seeded.Tenant, new SearchRequest("hypert phase"), ct);
        var byCode = await Search().SearchAsync(seeded.Tenant, new SearchRequest("SRCH-S1"), ct);
        var onlySites = await Search().SearchAsync(seeded.Tenant, new SearchRequest("SRCH", [NodeKinds.Site]), ct);

        byTitle.Select(h => h.Ref).Should().Equal(NodeRef.For(seeded.Study, 1));
        byTitle[0].Title.Should().Be("SRCH Hypertension Phase 3");
        byTitle[0].Code.Should().Be("SRCH-301");
        byCode[0].Should().Match<SearchHit>(h => h.Ref == NodeRef.For(seeded.Site, 1) && h.ExactCode && h.Detail == "DE");
        onlySites.Select(h => h.Ref.Kind).Should().OnlyContain(k => k == NodeKinds.Site).And.NotBeEmpty();
    }

    /// <summary>
    /// A code ending in a hyphen and digits (<c>HYPH-301</c>) is found by its exact code, ranked first, although the
    /// full-text parser indexes <c>-301</c> as a signed number that the search words never match (#5487); its words
    /// still find it too.
    /// </summary>
    [Fact(DisplayName = "Search_FindsAnExactCodeEndingInHyphenDigits")]
    public async Task SearchFindsAnExactCodeEndingInHyphenDigits()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seeded = await SeedTenantAsync("HYPH", "Asthma");

        var exact = await Search().SearchAsync(seeded.Tenant, new SearchRequest("hyph-301"), ct);
        var studiesOnly = await Search().SearchAsync(seeded.Tenant, new SearchRequest("HYPH-301", [NodeKinds.Study]), ct);
        var byWords = await Search().SearchAsync(seeded.Tenant, new SearchRequest("hyph asthma"), ct);

        exact[0].Should().Match<SearchHit>(h => h.Ref == NodeRef.For(seeded.Study, 1) && h.ExactCode && h.Code == "HYPH-301");
        studiesOnly.Should().ContainSingle().Which.ExactCode.Should().BeTrue();
        byWords.Select(h => h.Ref.Id).Should().Contain([seeded.Study.Value, seeded.Organization.Value]);
    }

    /// <summary>
    /// Two tenants hold objects with the same words and codes. Each tenant's search returns only its own objects:
    /// the query runs under that tenant's Row-Level Security, like every read of the source tables.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "Search_NeverReturnsAnotherTenantsObjects")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-CRF-013")]
    [KernelRule("K-ID-8")]
    [KernelRule("K-ID-2")]
    public async Task SearchNeverReturnsAnotherTenantsObjects()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var first = await SeedTenantAsync("ISOA", "Oncology");
        var second = await SeedTenantAsync("ISOB", "Oncology");

        var firstHits = await Search().SearchAsync(first.Tenant, new SearchRequest("oncology", Limit: SearchRequest.MaxLimit), ct);
        var secondHits = await Search().SearchAsync(second.Tenant, new SearchRequest("oncology", Limit: SearchRequest.MaxLimit), ct);
        var crossCode = await Search().SearchAsync(first.Tenant, new SearchRequest("ISOB-301"), ct);

        firstHits.Select(h => h.Ref.Id).Should().NotBeEmpty().And.BeSubsetOf(first.Ids);
        secondHits.Select(h => h.Ref.Id).Should().NotBeEmpty().And.BeSubsetOf(second.Ids);
        crossCode.Should().BeEmpty();
    }

    /// <summary>
    /// Terminology codes resolve to their code-system version with the code system and version as detail, and
    /// concepts are found by label and by their CDISC code.
    /// </summary>
    [Fact(DisplayName = "Search_FindsTerminologyCodesAndConcepts")]
    public async Task SearchFindsTerminologyCodesAndConcepts()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        await Send(new ProvisionTenant(tenant, $"T{tenant.Value:N}"[..20], "Search terminology", Key(), Reason), ct);
        var codeSystem = await Send(new RegisterCodeSystem(tenant, CodeSystemId.New(), "urn:test:search", "Search Dictionary", "Cadence", LicenseClass.Open, Key(), Reason), ct);
        var version = await Send(new ImportCodeSystemVersion(
            tenant, CodeSystemVersionId.New(), codeSystem, "27.1", new DateOnly(2026, 3, 1),
            new TerminologyPackage(new string('a', 64), "dictionary.txt", "https://example.test/dictionary", 42, "text"),
            [new CodeSystemEntryData("10020772", "Hypertension"), new CodeSystemEntryData("10005750", "Blood pressure increased")],
            Key(), Reason), ct);
        var concept = await Send(new DefineConcept(
            tenant, ConceptId.New(), ConceptOrigin.CdiscBiomedicalConcept, "C25298", ConceptKind.BiomedicalConcept,
            "Systolic Blood Pressure", "Pressure during systole.", Key(), Reason), ct);

        var code = await Search().SearchAsync(tenant, new SearchRequest("10020772"), ct);
        var words = await Search().SearchAsync(tenant, new SearchRequest("blood press"), ct);

        code.Should().ContainSingle().Which.Should().Match<SearchHit>(h =>
            h.Ref == NodeRef.For(version, 1) && h.Title == "Hypertension" && h.ExactCode && h.Detail == "Search Dictionary 27.1");
        words.Select(h => (h.Ref.Kind, h.Title)).Should().BeEquivalentTo(new[]
        {
            (NodeKinds.CodeSystemVersion, "Blood pressure increased"),
            (NodeKinds.Concept, "Systolic Blood Pressure"),
        });
        words.Single(h => h.Ref.Kind == NodeKinds.Concept).Ref.Should().Be(NodeRef.For(concept.ConceptId, concept.Version));
        (await Search().SearchAsync(tenant, new SearchRequest("C25298"), ct))[0].ExactCode.Should().BeTrue();
    }

    /// <summary>The migration creates one GIN index per searched table.</summary>
    [Fact(DisplayName = "Migration_CreatesTheSearchIndexes")]
    public async Task MigrationCreatesTheSearchIndexes()
    {
        fixture.SkipIfUnavailable();
        await using var db = fixture.CreateAdminContext();

        var indexes = await db.Database
            .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'kernel' AND indexname LIKE 'ix_%_search'")
            .ToListAsync(TestContext.Current.CancellationToken);

        indexes.Should().BeEquivalentTo(KernelSearchStore.BuiltInSources.Select(s => s.IndexName).Distinct());
    }

    /// <summary>
    /// Each source's query expression is the one its index was built on, so PostgreSQL can answer the match from the
    /// index (checked with sequential scans disabled).
    /// </summary>
    [Fact(DisplayName = "Search_Expressions_UseTheIndexes")]
    public async Task SearchExpressionsUseTheIndexes()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateAdminContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using (var off = connection.CreateCommand())
        {
            off.CommandText = "SET enable_seqscan = off";
            await off.ExecuteNonQueryAsync(ct);
        }

        foreach (var source in KernelSearchStore.BuiltInSources)
        {
            await using var explain = connection.CreateCommand();
            explain.CommandText = $"""
                EXPLAIN SELECT {source.Id}
                FROM {source.From}
                WHERE {source.Document} @@ to_tsquery('simple'::regconfig, 'x:*')
                """;
            var plan = new System.Text.StringBuilder();
            await using var reader = await explain.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                plan.AppendLine(reader.GetString(0));
            }

            plan.ToString().Should().Contain(source.IndexName, $"the {source.Name} union arm should use its GIN index");
        }
    }

    /// <summary>
    /// All nine clinical entity kinds added to <see cref="KernelSearchStore"/> (<c>study_site</c>, <c>enrollment</c>,
    /// <c>protocol_version</c>, <c>collection_instance</c>, <c>observation</c>, <c>validation_finding</c>,
    /// <c>qualification</c>, <c>artifact</c>, <c>proposal</c>) are searchable by code, title, and hyphenated prefixes.
    /// </summary>
    [Fact(DisplayName = "Search_FindsAllClinicalEntityKinds_PinnedToVersion")]
    [KernelRule("K-ID-8")]
    public async Task SearchFindsAllClinicalEntityKindsPinnedToVersion()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await new KernelSignedTargetsScenario(fixture).CreateAsync(ct);
        var tenant = world.Tenant;

        var trainingBytes = System.Text.Encoding.UTF8.GetBytes("DEV-2026-014 deviation evidence");
        var devArtifactId = ArtifactId.New();
        var devArtifactVersion = ArtifactVersionId.New();
        await Send(
            new RegisterArtifact(
                tenant,
                devArtifactId,
                devArtifactVersion,
                Cadence.Kernel.Evidence.ArtifactKind.TrainingRecord,
                world.Study,
                "DEV-2026-014 Protocol Deviation Report",
                new Cadence.Kernel.Evidence.ArtifactContent(
                    Cadence.Kernel.Trust.ContentHash.OfBytes(trainingBytes),
                    trainingBytes.Length,
                    "application/pdf",
                    "blob://deviations/dev-2026-014.pdf"),
                Key(),
                Reason),
            ct);

        var qualification = await Send(
            new RecordQualification(
                tenant,
                QualificationId.New(),
                Actor.Person("crc@search.test"),
                QualificationKind.Training,
                "gcp_ich_e6r3",
                null,
                devArtifactVersion,
                fixture.Clock.UtcNow,
                null,
                Key(),
                Reason),
            ct);

        var runAt = fixture.Clock.UtcNow;
        await Send(
            new Cadence.Kernel.Validation.RecordValidationRun(
                tenant,
                Cadence.Kernel.Validation.ValidationRunId.New(),
                world.Study,
                Cadence.Kernel.Validation.ValidationStage.Collection,
                [world.LibraryItem],
                new DateOnly(2026, 10, 2),
                null,
                null,
                null,
                [],
                ["VITALS"],
                runAt.AddSeconds(-1),
                runAt,
                [
                    new Cadence.Kernel.Validation.ValidationDetection(
                        "signed-targets-pack",
                        "DEV-2026-01",
                        1,
                        Cadence.Kernel.Validation.ValidationSeverity.Error,
                        new Cadence.Kernel.Validation.FindingTarget(
                            Cadence.Kernel.Validation.FindingTargetKind.Observation,
                            "VITALS",
                            NodeRef.For(world.FormObservation).ToString(),
                            "systolic"),
                        "Systolic out of protocol window DEV-2026-01.",
                        null,
                        [new Cadence.Kernel.Validation.ObservedVariable("systolic", "188")]),
                ],
                Key(),
                Reason),
            ct);

        string siteCode;
        string subjectCode;
        string collectionSpecId;
        string proposalIntent;
        await using (var db = fixture.CreateContext(tenant))
        {
            var siteId = await db.StudySites.AsNoTracking().Where(ss => ss.Id == world.StudySite).Select(ss => ss.SiteId).SingleAsync(ct);
            siteCode = await db.Sites.AsNoTracking().Where(s => s.Id == siteId).Select(s => s.SiteCode).SingleAsync(ct);
            subjectCode = (await db.Enrollments.AsNoTracking().Where(e => e.Id == world.Enrollment).Select(e => e.SubjectCode).SingleAsync(ct)).Value;
            collectionSpecId = await db.CollectionInstances.AsNoTracking().Where(ci => ci.Id == world.CollectionInstance).Select(ci => ci.CollectionSpecId).SingleAsync(ct);
            proposalIntent = await db.Proposals.AsNoTracking().Where(p => p.Id == world.Proposal).Select(p => p.Intent).SingleAsync(ct);
        }

        var search = Search();

        var studySiteHits = await search.SearchAsync(tenant, new SearchRequest(siteCode, [NodeKinds.StudySite]), ct);
        studySiteHits.Should().Contain(h => h.Ref.Kind == NodeKinds.StudySite && h.Ref.Id == world.StudySite.Value && h.ExactCode);

        var enrollmentHits = await search.SearchAsync(tenant, new SearchRequest(subjectCode, [NodeKinds.Enrollment]), ct);
        enrollmentHits.Should().ContainSingle(h => h.Ref.Kind == NodeKinds.Enrollment && h.Ref.Id == world.Enrollment.Value && h.ExactCode);

        var protocolHits = await search.SearchAsync(tenant, new SearchRequest("v1", [NodeKinds.ProtocolVersion]), ct);
        protocolHits.Should().Contain(h => h.Ref.Kind == NodeKinds.ProtocolVersion && h.Ref.Id == world.ProtocolVersion.Value && h.ExactCode);

        var formHits = await search.SearchAsync(tenant, new SearchRequest(collectionSpecId, [NodeKinds.CollectionInstance]), ct);
        formHits.Should().Contain(h => h.Ref.Kind == NodeKinds.CollectionInstance && h.Ref.Id == world.CollectionInstance.Value);

        var obsHits = await search.SearchAsync(tenant, new SearchRequest("systolic", [NodeKinds.Observation]), ct);
        obsHits.Should().Contain(h => h.Ref.Kind == NodeKinds.Observation && h.Ref.Id == world.FormObservation.Value);

        var devHits = await search.SearchAsync(tenant, new SearchRequest("DEV-2026", Limit: SearchRequest.MaxLimit), ct);
        devHits.Select(h => h.Ref.Kind).Should().Contain([NodeKinds.ValidationFinding, NodeKinds.Artifact]);
        devHits.Should().Contain(h => h.Ref.Kind == NodeKinds.Artifact && h.Ref.Id == devArtifactId.Value);
        devHits.Should().Contain(h => h.Ref.Kind == NodeKinds.ValidationFinding && h.Ref.Version == null);

        var decidedFindingHits = await search.SearchAsync(tenant, new SearchRequest("reversible cuff", [NodeKinds.ValidationFinding]), ct);
        decidedFindingHits.Should().Contain(h => h.Ref.Kind == NodeKinds.ValidationFinding && h.Ref.Id == world.ValidationFinding.Value && h.Ref.Version >= 1);

        var qualHits = await search.SearchAsync(tenant, new SearchRequest("gcp ich", [NodeKinds.Qualification]), ct);
        qualHits.Should().ContainSingle(h => h.Ref.Kind == NodeKinds.Qualification && h.Ref.Id == qualification.QualificationId.Value);

        var firstWord = KernelSearch.Terms(proposalIntent)[0];
        var proposalHits = await search.SearchAsync(tenant, new SearchRequest(firstWord, [NodeKinds.Proposal]), ct);
        proposalHits.Should().Contain(h => h.Ref.Kind == NodeKinds.Proposal && h.Ref.Id == world.Proposal.Value);
    }

    /// <summary>
    /// Code systems, standard versions and library items are found by their titles and codes, with their detail
    /// column (publisher, standard version), and a code typed in another case still counts as an exact match.
    /// </summary>
    [Fact(DisplayName = "Search_FindsCodeSystemsStandardsAndLibraryItems")]
    public async Task SearchFindsCodeSystemsStandardsAndLibraryItems()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        await Send(new ProvisionTenant(tenant, $"T{tenant.Value:N}"[..20], "Search registry", Key(), Reason), ct);
        var codeSystem = await Send(new RegisterCodeSystem(tenant, CodeSystemId.New(), "urn:test:zephyr", "Zephyr Dictionary", "Zephyr Group", LicenseClass.Open, Key(), Reason), ct);
        var package = StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package;
        var standard = await Send(new RegisterStandardVersion(tenant, StandardVersionId.New(), package, null, Key(), Reason), ct);
        using var content = JsonDocument.Parse("""{"codelist":"C66770","terms":["mmHg","beats/min"]}""");
        var item = await Send(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.CodelistSubset, "VS original units", "Vital sign units", content.RootElement, [], Key(), Reason), ct);

        var byName = await Search().SearchAsync(tenant, new SearchRequest("zephyr"), ct);
        var byLowerCaseCode = await Search().SearchAsync(tenant, new SearchRequest("sdtmig"), ct);
        var byItemTitle = await Search().SearchAsync(tenant, new SearchRequest("vital sign"), ct);

        byName.Should().ContainSingle().Which.Should().Match<SearchHit>(h =>
            h.Ref == NodeRef.For(codeSystem, 1) && h.Title == "Zephyr Dictionary" && h.Code == null && h.Detail == "Zephyr Group" && !h.ExactCode);
        byLowerCaseCode[0].Should().Match<SearchHit>(h =>
            h.Ref.Kind == NodeKinds.StandardVersion && h.Ref.Id == standard.Value && h.Code == "SDTMIG" && h.ExactCode && h.Detail == package.Version);
        byItemTitle.Should().ContainSingle().Which.Should().Match<SearchHit>(h =>
            h.Ref.Kind == NodeKinds.LibraryItem && h.Ref.Id == item.Id.Value && h.Title == "Vital sign units" && h.Code == "VS original units");
    }

    /// <summary>
    /// Results that match equally are ordered by title, and the limit cuts the ordered list rather than an arbitrary
    /// subset.
    /// </summary>
    [Fact(DisplayName = "Search_OrdersEqualMatchesByTitle_AndStopsAtTheLimit")]
    public async Task SearchOrdersEqualMatchesByTitleAndStopsAtTheLimit()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var sponsor = OrganizationId.New();
        var actor = KernelPostgresFixture.ActorId;
        await fixture.SeedAsync(
            new Tenant(tenant, "ORDR", "Order Pharma", actor, Reason),
            new Organization(sponsor, tenant, "ORDR-ORG", "Order Sponsor", OrganizationKind.Sponsor, actor, Reason),
            new Study(StudyId.New(), tenant, sponsor, "ORDR-1", "Delta Oncology", "Phase 2", actor, Reason),
            new Study(StudyId.New(), tenant, sponsor, "ORDR-2", "Alpha Oncology", "Phase 2", actor, Reason),
            new Study(StudyId.New(), tenant, sponsor, "ORDR-3", "Charlie Oncology", "Phase 2", actor, Reason),
            new Study(StudyId.New(), tenant, sponsor, "ORDR-4", "Bravo Oncology", "Phase 2", actor, Reason));

        var all = await Search().SearchAsync(tenant, new SearchRequest("oncology", [NodeKinds.Study]), ct);
        var firstTwo = await Search().SearchAsync(tenant, new SearchRequest("oncology", [NodeKinds.Study], Limit: 2), ct);

        all.Select(h => h.Title).Should().Equal("Alpha Oncology", "Bravo Oncology", "Charlie Oncology", "Delta Oncology");
        firstTwo.Select(h => h.Title).Should().Equal("Alpha Oncology", "Bravo Oncology");
    }

    private KernelSearch Search() => new(new KernelSearchStore(fixture.TenantContexts), new PermitProbe());

    private async Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct)
    {
        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private async Task<Seeded> SeedTenantAsync(string code, string topic)
    {
        var tenant = TenantId.New();
        var organization = OrganizationId.New();
        var site = SiteId.New();
        var study = StudyId.New();
        var actor = KernelPostgresFixture.ActorId;
        await fixture.SeedAsync(
            new Tenant(tenant, code, $"{code} Pharma", actor, Reason),
            new Organization(organization, tenant, $"{code}-ORG", $"{code} {topic} Institute", OrganizationKind.SiteInstitution, actor, Reason),
            new Site(site, tenant, organization, $"{code}-S1", $"{code} Clinic", "DE", actor, Reason),
            new Study(study, tenant, organization, $"{code}-301", $"{code} {topic} Phase 3", "Phase 3", actor, Reason));
        return new Seeded(tenant, organization, site, study);
    }

    private static string Key() => Guid.CreateVersion7().ToString();

    private sealed record Seeded(TenantId Tenant, OrganizationId Organization, SiteId Site, StudyId Study)
    {
        public Guid[] Ids => [Organization.Value, Site.Value, Study.Value];
    }

    private sealed class PermitProbe : IAuthorizationProbe
    {
        public ValueTask<AuthorizationDecision> CanIAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit());

        public ValueTask<System.Collections.Generic.IReadOnlyList<CommandAvailability>> GetCommandsAsync(
            TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
