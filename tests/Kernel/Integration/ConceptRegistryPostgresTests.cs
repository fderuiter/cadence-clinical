using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Knowledge.Terminology;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Testing;
using Cadence.UnitTests.Kernel.Knowledge;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// The Concept Registry through the command pipeline and its read port against PostgreSQL (spec §6.1, ADR-2252,
/// ADR-2470): checksum-verified terminology loading, version pinning, concept versions, mappings, and tenant
/// isolation. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConceptRegistryPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Concept Registry integration test";

    /// <summary>
    /// A CDISC CT package loads as one immutable code-system version: codelists and terms are readable by the version
    /// that was named, loading the same file again replays the import, and the audit entry names the package digest.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "CdiscCtPackage_Loads_AsOnePinnedVersion_WithAuditedDigest")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CdiscCtPackageLoadsAsOnePinnedVersionWithAuditedDigest()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await NewTenantAsync(ct);
        var ctSystem = await RegisterAsync(tenant, KnownCodeSystems.NciThesaurus, LicenseClass.Open, ct);

        var release = Release(tenant, ctSystem, "2024-09-27", TerminologyFixtures.CdiscCt);
        var loader = new TerminologyPackageLoader(fixture.Sender);
        var loaded = await loader.LoadAsync(release, TerminologyFixtures.CdiscCt, new CdiscControlledTerminologyParser(), ct);

        loaded.IsSuccess.Should().BeTrue(loaded.Error?.Message);
        var registry = fixture.ConceptRegistry;
        var version = await registry.FindCodeSystemVersionAsync(tenant, KnownCodeSystems.NciThesaurus, "2024-09-27", ct);
        version!.Id.Should().Be(loaded.Value);
        version.EntryCount.Should().Be(8);
        version.PackageSha256.Should().Be(TerminologyFixtures.Sha256(TerminologyFixtures.CdiscCt));

        var positions = await registry.GetGroupAsync(tenant, version.Id, "C78431", ct);
        positions.Select(p => p.SubmissionValue).Should().Equal("SITTING", "STANDING", "SUPINE");
        (await registry.LookupAsync(tenant, new Coding(version.Id, "C62167"), ct)).Single().Display.Should().Be("Supine Position");
        (await registry.FindCodeSystemVersionAsync(tenant, KnownCodeSystems.NciThesaurus, "2025-03-28", ct)).Should().BeNull();

        var again = await loader.LoadAsync(release, TerminologyFixtures.CdiscCt, new CdiscControlledTerminologyParser(), ct);
        again.Replayed.Should().BeTrue();
        again.Value.Should().Be(loaded.Value);

        await using var db = fixture.CreateAdminContext();
        var audit = await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.TenantId == tenant && a.AggregateType == "CodeSystemVersion", ct);
        audit.Action.Should().Be("Created");
        audit.ReasonForChange.Should().Be(Reason);
        audit.Changes.Should().Contain(version.PackageSha256);
    }

    /// <summary>Nothing resolves "the latest": a lookup must name its version.</summary>
    [Fact(DisplayName = "CodeSystemVersion_LookupWithoutVersion_Throws")]
    public async Task CodeSystemVersionLookupWithoutVersionThrows()
    {
        fixture.SkipIfUnavailable();

        var act = () => fixture.ConceptRegistry.FindCodeSystemVersionAsync(TenantId.New(), KnownCodeSystems.Loinc, " ", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>A second import of a version that already exists is refused, even under a new idempotency key.</summary>
    [Fact(DisplayName = "CodeSystemVersion_ImportedTwice_IsRefused")]
    public async Task CodeSystemVersionImportedTwiceIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await NewTenantAsync(ct);
        var ctSystem = await RegisterAsync(tenant, KnownCodeSystems.NciThesaurus, LicenseClass.Open, ct);
        (await LoadCdiscCtAsync(tenant, ctSystem, ct)).IsSuccess.Should().BeTrue();

        var duplicate = await fixture.Sender.SendAsync(
            new ImportCodeSystemVersion(tenant, CodeSystemVersionId.New(), ctSystem, "2024-09-27", new DateOnly(2024, 9, 27),
                new TerminologyPackage(new string('c', 64), "other.txt", "https://example.test", 1, "nci-evs-tsv"),
                [new CodeSystemEntryData("C1", "One")], Key(), Reason),
            cancellationToken: ct);

        duplicate.Error.Should().Be(KernelErrors.AlreadyExists);
    }

    /// <summary>
    /// ADR-2252 verification: a LOINC-coded lab feed and an eCRF field for the same Biomedical Concept resolve to the
    /// same ConceptRef. Only confirmed mappings resolve a code.
    /// </summary>
    [Fact(DisplayName = "BiomedicalConcept_LoincCodeAndEcrfField_ResolveToTheSameConceptRef")]
    public async Task BiomedicalConceptLoincCodeAndEcrfFieldResolveToTheSameConceptRef()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await NewTenantAsync(ct);
        var loinc = await RegisterAsync(tenant, KnownCodeSystems.Loinc, LicenseClass.AcceptedTerms, ct);
        var loincVersion = (await new TerminologyPackageLoader(fixture.Sender).LoadAsync(
            Release(tenant, loinc, "2.78", TerminologyFixtures.LoincTable), TerminologyFixtures.LoincTable, new LoincTableParser(), ct)).Value;

        var systolic = await DefineAsync(tenant, ConceptOrigin.CdiscBiomedicalConcept, "C25298", "Systolic Blood Pressure", ct);
        var sponsorGuess = await DefineAsync(tenant, ConceptOrigin.Sponsor, null, "Arm pressure (upper)", ct);
        var systolicLoinc = new Coding(loincVersion, "8480-6");
        (await MapAsync(tenant, systolic, systolicLoinc, confirmed: true, ct)).IsSuccess.Should().BeTrue();
        (await MapAsync(tenant, sponsorGuess, systolicLoinc, confirmed: false, ct)).IsSuccess.Should().BeTrue();

        var registry = fixture.ConceptRegistry;
        var fromLabFeed = (await registry.ResolveCodingAsync(tenant, systolicLoinc, ct)).Single().Concept;
        var fromEcrfField = (await registry.FindBiomedicalConceptAsync(tenant, "C25298", ct))!.Concept;

        fromLabFeed.Should().Be(fromEcrfField).And.Be(systolic);
        (await registry.GetCurrentAsync(tenant, systolic.ConceptId, ct))!.MappingState.Should().Be(ConceptMappingState.Mapped);
        (await registry.GetCurrentAsync(tenant, sponsorGuess.ConceptId, ct))!.MappingState.Should().Be(ConceptMappingState.Proposed);
    }

    /// <summary>A mapping to a code that is not in the pinned version, or to a missing concept version, is refused.</summary>
    [Fact(DisplayName = "MapConcept_UnknownCodeOrConceptVersion_IsRefused")]
    public async Task MapConceptUnknownCodeOrConceptVersionIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await NewTenantAsync(ct);
        var ctSystem = await RegisterAsync(tenant, KnownCodeSystems.NciThesaurus, LicenseClass.Open, ct);
        var ctVersion = (await LoadCdiscCtAsync(tenant, ctSystem, ct)).Value;
        var sitting = await DefineAsync(tenant, ConceptOrigin.Sponsor, null, "Sitting position", ct);

        (await MapAsync(tenant, sitting, new Coding(ctVersion, "C99999"), confirmed: true, ct)).Error.Should().Be(KnowledgeErrors.CodeUnknown);
        (await MapAsync(tenant, new ConceptRef(sitting.ConceptId, 2), new Coding(ctVersion, "C62122"), confirmed: true, ct))
            .Error.Should().Be(KnowledgeErrors.ConceptUnknown);
    }

    /// <summary>
    /// Revising a concept creates version 2 while version 1 resolves exactly as it was, with its mappings.
    /// @req:PRD-MDR-002
    /// </summary>
    [Fact(DisplayName = "Concept_Revised_OldVersionStillResolvesUnchanged")]
    [GxPRequirement("PRD-MDR-002")]
    public async Task ConceptRevisedOldVersionStillResolvesUnchanged()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await NewTenantAsync(ct);
        var v1 = await DefineAsync(tenant, ConceptOrigin.Sponsor, null, "Grip strength", ct);

        var revised = await fixture.Sender.SendAsync(
            new ReviseConcept(tenant, v1.ConceptId, "Hand grip strength", "Maximum isometric hand grip force.", Key(), Reason), cancellationToken: ct);

        revised.Value.Should().Be(new ConceptRef(v1.ConceptId, 2));
        var registry = fixture.ConceptRegistry;
        (await registry.ResolveAsync(tenant, v1, ct))!.PreferredLabel.Should().Be("Grip strength");
        var current = await registry.GetCurrentAsync(tenant, v1.ConceptId, ct);
        current!.PreferredLabel.Should().Be("Hand grip strength");
        current.Concept.Version.Should().Be(2);
        (await registry.ResolveAsync(tenant, new ConceptRef(v1.ConceptId, 3), ct)).Should().BeNull();
    }

    /// <summary>A licensed code system cannot be registered as open content through the pipeline (K-SEM-2).</summary>
    [Fact(DisplayName = "RegisterCodeSystem_MedDraAsOpen_IsRefused")]
    public async Task RegisterCodeSystemMedDraAsOpenIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await NewTenantAsync(ct);

        var result = await fixture.Sender.SendAsync(
            new RegisterCodeSystem(tenant, CodeSystemId.New(), KnownCodeSystems.MedDra, "MedDRA", "ICH", LicenseClass.Open, Key(), Reason),
            cancellationToken: ct);

        result.Error!.Code.Should().Be(KnowledgeErrors.LicenseMismatch.Code);
    }

    /// <summary>Terminology is per tenant: another tenant sees none of it (ADR-2470, K-ID-2).</summary>
    [Fact(DisplayName = "Terminology_IsInvisibleToOtherTenants")]
    public async Task TerminologyIsInvisibleToOtherTenants()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var owner = await NewTenantAsync(ct);
        var other = await NewTenantAsync(ct);
        var ctSystem = await RegisterAsync(owner, KnownCodeSystems.NciThesaurus, LicenseClass.Open, ct);
        var version = (await LoadCdiscCtAsync(owner, ctSystem, ct)).Value;
        var concept = await DefineAsync(owner, ConceptOrigin.CdiscBiomedicalConcept, "C49676", "Pulse Rate", ct);

        var registry = fixture.ConceptRegistry;
        (await registry.FindCodeSystemVersionAsync(other, KnownCodeSystems.NciThesaurus, "2024-09-27", ct)).Should().BeNull();
        (await registry.LookupAsync(other, new Coding(version, "C62122"), ct)).Should().BeEmpty();
        (await registry.ResolveAsync(other, concept, ct)).Should().BeNull();
        (await registry.FindBiomedicalConceptAsync(other, "C49676", ct)).Should().BeNull();
    }

    /// <summary>Loads the CDISC CT fixture into a tenant, for tests (here and in the pipeline tests) that need rows.</summary>
    internal static async Task SeedAsync(KernelPostgresFixture fixture, CancellationToken ct)
    {
        var tests = new ConceptRegistryPostgresTests(fixture);
        var tenant = await tests.NewTenantAsync(ct);
        var ctSystem = await tests.RegisterAsync(tenant, KnownCodeSystems.NciThesaurus, LicenseClass.Open, ct);
        var version = (await tests.LoadCdiscCtAsync(tenant, ctSystem, ct)).Value;
        var concept = await tests.DefineAsync(tenant, ConceptOrigin.Sponsor, null, "Sitting position", ct);
        (await tests.MapAsync(tenant, concept, new Coding(version, "C62122"), confirmed: true, ct)).IsSuccess.Should().BeTrue();
    }

    private async Task<TenantId> NewTenantAsync(CancellationToken ct)
    {
        var id = TenantId.New();
        var result = await fixture.Sender.SendAsync(new ProvisionTenant(id, $"T{id.Value:N}"[..20], "Knowledge tenant", Key(), Reason), cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return id;
    }

    private async Task<CodeSystemId> RegisterAsync(TenantId tenant, string uri, LicenseClass licence, CancellationToken ct)
    {
        var result = await fixture.Sender.SendAsync(
            new RegisterCodeSystem(tenant, CodeSystemId.New(), uri, uri, "Publisher", licence, Key(), Reason), cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private Task<CommandResult<CodeSystemVersionId>> LoadCdiscCtAsync(TenantId tenant, CodeSystemId codeSystem, CancellationToken ct) =>
        new TerminologyPackageLoader(fixture.Sender).LoadAsync(
            Release(tenant, codeSystem, "2024-09-27", TerminologyFixtures.CdiscCt), TerminologyFixtures.CdiscCt, new CdiscControlledTerminologyParser(), ct);

    private async Task<ConceptRef> DefineAsync(TenantId tenant, ConceptOrigin origin, string? code, string label, CancellationToken ct)
    {
        var result = await fixture.Sender.SendAsync(
            new DefineConcept(tenant, ConceptId.New(), origin, code, ConceptKind.BiomedicalConcept, label, $"{label} (definition).", Key(), Reason),
            cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private Task<CommandResult<ConceptMappingId>> MapAsync(TenantId tenant, ConceptRef concept, Coding target, bool confirmed, CancellationToken ct) =>
        fixture.Sender.SendAsync(
            new MapConcept(tenant, ConceptMappingId.New(), concept, target, MappingRelation.Exact, "Reviewed against the CDISC BC coding.", confirmed, Key(), Reason),
            cancellationToken: ct);

    private static TerminologyRelease Release(TenantId tenant, CodeSystemId codeSystem, string version, byte[] content) => new(
        tenant, codeSystem, CodeSystemVersionId.New(), version, new DateOnly(2024, 9, 27), "terminology.txt",
        "https://evs.nci.nih.gov/ftp1/CDISC/", TerminologyFixtures.Sha256(content), Reason);

    private static string Key() => Guid.CreateVersion7().ToString();
}
