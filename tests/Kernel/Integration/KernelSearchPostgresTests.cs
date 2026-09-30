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

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// Kernel search against PostgreSQL (ADR-2484): code and title matches across studies, sites, organizations,
/// concepts and terminology codes, NodeRef results, tenant isolation through Row-Level Security, and the GIN indexes
/// the migration creates. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
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
    /// Two tenants hold objects with the same words and codes. Each tenant's search returns only its own objects:
    /// the query runs under that tenant's Row-Level Security, like every read of the source tables.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "Search_NeverReturnsAnotherTenantsObjects")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-CRF-013")]
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

        foreach (var source in KernelSearchStore.BuiltInSources.DistinctBy(s => s.Table))
        {
            await using var explain = connection.CreateCommand();
            explain.CommandText = $"EXPLAIN SELECT 1 FROM kernel.{source.Table} t WHERE {source.Document} @@ to_tsquery('simple'::regconfig, 'x:*')";
            var plan = new System.Text.StringBuilder();
            await using var reader = await explain.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                plan.AppendLine(reader.GetString(0));
            }

            plan.ToString().Should().Contain(source.IndexName, $"the {source.Name} query should use its index");
        }
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
