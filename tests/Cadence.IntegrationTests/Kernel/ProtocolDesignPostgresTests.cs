using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Submitting a protocol design for review against PostgreSQL (spec §6.2, ADR-2472): every concept version, pinned
/// code-system version and artifact the design names must exist in the tenant (K-SEM-1), and a design that names only
/// what the tenant has goes into review. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProtocolDesignPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// A design naming the tenant's concept at its current version, its CDISC CT version and a filed statistical
    /// analysis plan is submitted, and the stored content reads back as the same design.
    /// </summary>
    [Fact(DisplayName = "Submit_DesignWithRegisteredReferences_GoesIntoReview")]
    public async Task SubmitDesignWithRegisteredReferencesGoesIntoReview()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var (tenant, study) = await _scenario.NewStudyAsync(seed.Tenant, ct);
        var plan = await RegisterPlanAsync(tenant, ct);
        var design = ProtocolDesignSamples.Complete(seed.Concept, seed.CodeSystemVersion, plan);
        var id = ProtocolVersionId.New();

        (await _scenario.CreateDraftAsync(tenant, study, id, ct, ProtocolDesignDocument.Write(design))).IsSuccess.Should().BeTrue();
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready"), ct);

        inReview.Status.Should().Be(ProtocolVersionStatus.InReview);
        await using var db = fixture.CreateContext(tenant);
        var stored = await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == id, ct);
        stored.Design().Should().BeEquivalentTo(design);
    }

    /// <summary>
    /// A draft may name what the tenant does not have yet, but submitting it is refused with the first unknown
    /// reference, and the version stays a draft: a concept that does not exist, a later version of a concept that does,
    /// a code-system version and an artifact the tenant does not have.
    /// </summary>
    [Fact(DisplayName = "Submit_UnknownReference_IsRefused_AndTheVersionStaysADraft")]
    public async Task SubmitUnknownReferenceIsRefusedAndTheVersionStaysADraft()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var tenant = seed.Tenant;
        var plan = await RegisterPlanAsync(tenant, ct);
        var unknownConcept = new ConceptRef(ConceptId.New(), 1);
        var laterVersion = new ConceptRef(seed.Concept.ConceptId, seed.Concept.Version + 1);
        var unknownCodeSystem = CodeSystemVersionId.New();
        var unknownArtifact = ArtifactId.New();

        (string Content, string Unknown)[] cases =
        [
            (Write(unknownConcept, seed.CodeSystemVersion, plan), $"concept {unknownConcept}"),
            (Write(laterVersion, seed.CodeSystemVersion, plan), $"concept {laterVersion}"),
            (Write(seed.Concept, unknownCodeSystem, plan), $"code-system version {unknownCodeSystem}"),
            (Write(seed.Concept, seed.CodeSystemVersion, unknownArtifact), $"artifact {unknownArtifact}"),
        ];

        foreach (var (content, unknown) in cases)
        {
            var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
            var id = ProtocolVersionId.New();
            (await _scenario.CreateDraftAsync(tenant, study, id, ct, content)).IsSuccess.Should().BeTrue("a draft may be incomplete");

            var result = await _scenario.SubmitAsync(tenant, id, ct);

            result.Error!.Code.Should().Be(ProtocolErrors.ReferenceUnknown.Code);
            result.Error.Message.Should().EndWith($"Unknown: {unknown}.");
            await using var db = fixture.CreateContext(tenant);
            var stored = await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == id, ct);
            stored.Status.Should().Be(ProtocolVersionStatus.Draft);
            stored.ContentHash.Should().BeNull();
        }
    }

    /// <summary>Another tenant's concept, code-system version and artifact are unknown here (K-SEM-1 within a tenant).</summary>
    [Fact(DisplayName = "Submit_AnotherTenantsReferences_AreUnknown")]
    public async Task SubmitAnotherTenantsReferencesAreUnknown()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var other = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var id = ProtocolVersionId.New();

        (await _scenario.CreateDraftAsync(tenant, study, id, ct, Write(other.Concept, other.CodeSystemVersion, null))).IsSuccess.Should().BeTrue();
        var result = await _scenario.SubmitAsync(tenant, id, ct);

        result.Error!.Code.Should().Be(ProtocolErrors.ReferenceUnknown.Code);
        result.Error.Message.Should().EndWith($"Unknown: concept {other.Concept}.");
    }

    /// <summary>
    /// The CDISC USDM example study imports through the tenant's Concept Registry once its Biomedical Concepts are
    /// defined there (ADR-2473), and the imported design is a draft that goes into review: every concept it names
    /// exists in the tenant (K-SEM-1). Before the concepts are defined, the import fails.
    /// </summary>
    [Fact(DisplayName = "ImportedUsdmDesign_ResolvesInTheTenant_AndGoesIntoReview")]
    public async Task ImportedUsdmDesignResolvesInTheTenantAndGoesIntoReview()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var usdm = UsdmSamples.CdiscExampleJson();

        var before = await UsdmDesignImporter.ImportAsync(usdm, fixture.ConceptRegistry, tenant, cancellationToken: ct);
        before.IsSuccess.Should().BeFalse();
        before.UnmappedConcepts.Should().HaveCount(7, "the example's seven Biomedical Concepts are not in the tenant yet");

        foreach (var code in UsdmSamples.CdiscExampleConceptCodes)
        {
            await _scenario.OkAsync(
                new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.CdiscBiomedicalConcept, code, ConceptKind.BiomedicalConcept, $"CDISC {code}", $"CDISC Biomedical Concept {code}.", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
                ct);
        }

        var imported = await UsdmDesignImporter.ImportAsync(usdm, fixture.ConceptRegistry, tenant, cancellationToken: ct);
        imported.Problems.Should().BeEmpty();
        var id = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(tenant, study, id, ct, ProtocolDesignDocument.Write(imported.Design!))).IsSuccess.Should().BeTrue();
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Imported from USDM"), ct);

        inReview.Status.Should().Be(ProtocolVersionStatus.InReview);
        await using var db = fixture.CreateContext(tenant);
        (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == id, ct)).Design().Should().BeEquivalentTo(imported.Design);
    }

    private static string Write(ConceptRef concept, CodeSystemVersionId codeSystemVersion, ArtifactId? artifact) =>
        ProtocolDesignDocument.Write(ProtocolDesignSamples.Complete(concept, codeSystemVersion, artifact));

    private async Task<ArtifactId> RegisterPlanAsync(TenantId tenant, CancellationToken ct)
    {
        var artifact = ArtifactId.New();
        var bytes = Encoding.UTF8.GetBytes("statistical analysis plan v1");
        var content = new ArtifactContent(ContentHash.OfBytes(bytes), bytes.Length, "application/pdf", $"blob://evidence/sap-{artifact.Value:N}");
        await _scenario.OkAsync(
            new RegisterArtifact(tenant, artifact, ArtifactVersionId.New(), ArtifactKind.Other, null, "Statistical analysis plan", content, KernelProtocolScenario.Key(), "Filed plan"),
            ct);
        return artifact;
    }
}
