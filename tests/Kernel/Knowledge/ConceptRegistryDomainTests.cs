using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Knowledge;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Knowledge;

/// <summary>Concept Registry domain rules (spec §6.1, ADR-2252, ADR-2470) that need no database.</summary>
[Trait("Category", "Unit")]
public class ConceptRegistryDomainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.New();

    /// <summary>A ConceptRef always names a concept and a version; there is no unversioned reference.</summary>
    [Fact(DisplayName = "ConceptRef_WithoutConceptOrVersion_IsRefused")]
    public void ConceptRefWithoutConceptOrVersionIsRefused()
    {
        var noConcept = () => new ConceptRef(ConceptId.Empty, 1);
        var noVersion = () => new ConceptRef(ConceptId.New(), 0);

        noConcept.Should().Throw<ArgumentException>();
        noVersion.Should().Throw<ArgumentOutOfRangeException>();
        default(ConceptRef).Version.Should().Be(0, "only the constructor makes a usable reference");
    }

    /// <summary>
    /// Revising a concept adds a version and keeps the earlier one exactly as it was, so a fact that referenced
    /// version 1 keeps its meaning.
    /// @req:PRD-MDR-002
    /// </summary>
    [Fact(DisplayName = "Concept_Revise_AddsAVersion_AndKeepsTheEarlierOneUnchanged")]
    [Cadence.Testing.GxPRequirement("PRD-MDR-002")]
    public void ConceptReviseAddsAVersionAndKeepsTheEarlierOneUnchanged()
    {
        var concept = Concept.Define(ConceptId.New(), Tenant, ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept,
            "Grip strength", "Maximum hand grip force.", T0).Value!;

        concept.Revise("Hand grip strength", "Maximum isometric hand grip force.", T0.AddDays(1)).Should().BeNull();

        concept.Current.Should().Be(new ConceptRef(concept.Id, 2));
        concept.Revisions.Select(r => (r.Version, r.PreferredLabel)).Should().Equal((1, "Grip strength"), (2, "Hand grip strength"));
        concept.Revise("Hand grip strength", "Maximum isometric hand grip force.", T0.AddDays(2))
            .Should().Be(KnowledgeErrors.ConceptUnchanged);
    }

    /// <summary>A CDISC Biomedical Concept carries its CDISC concept code; a sponsor concept does not.</summary>
    [Theory(DisplayName = "Concept_Define_ExternalCodeMustMatchOrigin")]
    [InlineData(ConceptOrigin.CdiscBiomedicalConcept, null)]
    [InlineData(ConceptOrigin.Sponsor, "C25298")]
    public void ConceptDefineExternalCodeMustMatchOrigin(ConceptOrigin origin, string? externalCode)
    {
        var result = Concept.Define(ConceptId.New(), Tenant, origin, externalCode, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic blood pressure.", T0);

        result.Error.Should().Be(KnowledgeErrors.ExternalCodeMismatch);
    }

    /// <summary>A deprecated concept cannot be revised or deprecated again.</summary>
    [Fact(DisplayName = "Concept_Deprecated_RefusesFurtherChange")]
    public void ConceptDeprecatedRefusesFurtherChange()
    {
        var concept = Concept.Define(ConceptId.New(), Tenant, ConceptOrigin.CdiscBiomedicalConcept, "C25298", ConceptKind.BiomedicalConcept,
            "Systolic Blood Pressure", "The maximum arterial pressure during contraction of the left ventricle.", T0).Value!;

        concept.Deprecate().Should().BeNull();

        concept.Deprecate().Should().Be(KnowledgeErrors.ConceptDeprecated);
        concept.Revise("x", "y", T0).Should().Be(KnowledgeErrors.ConceptDeprecated);
        concept.Current.Version.Should().Be(1);
    }

    /// <summary>Licensed code systems cannot be registered as open or accepted-terms content (K-SEM-2).</summary>
    [Theory(DisplayName = "CodeSystem_Register_KnownUriWithWrongLicence_IsRefused")]
    [InlineData(KnownCodeSystems.MedDra, LicenseClass.Open)]
    [InlineData(KnownCodeSystems.SnomedCt, LicenseClass.AcceptedTerms)]
    [InlineData(KnownCodeSystems.WhoDrug, LicenseClass.Open)]
    [InlineData(KnownCodeSystems.Loinc, LicenseClass.Open)]
    public void CodeSystemRegisterKnownUriWithWrongLicenceIsRefused(string uri, LicenseClass licence)
    {
        CodeSystem.Register(CodeSystemId.New(), Tenant, uri, "x", "y", licence).Error!.Code
            .Should().Be(KnowledgeErrors.LicenseMismatch.Code);
    }

    /// <summary>A sponsor dictionary with its own URI may be registered under any licence class.</summary>
    [Fact(DisplayName = "CodeSystem_Register_SponsorDictionary_IsAccepted")]
    public void CodeSystemRegisterSponsorDictionaryIsAccepted()
    {
        var result = CodeSystem.Register(CodeSystemId.New(), Tenant, "https://sponsor.example/dictionaries/visit-reasons", "Visit reasons", "Sponsor", LicenseClass.Licensed);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>A version must be named, bound to a SHA-256, and have unique codes per group.</summary>
    [Theory(DisplayName = "CodeSystemVersion_Import_RefusesUnpinnedOrInvalidPackages")]
    [MemberData(nameof(InvalidImports))]
    public void CodeSystemVersionImportRefusesUnpinnedOrInvalidPackages(string version, TerminologyPackage package, CodeSystemEntryData[] entries, string expectedCode)
    {
        var result = CodeSystemVersion.Import(CodeSystemVersionId.New(), Tenant, CodeSystemId.New(), version, new DateOnly(2024, 9, 27), package, entries);

        result.Error!.Code.Should().Be(expectedCode);
    }

    public static TheoryData<string, TerminologyPackage, CodeSystemEntryData[], string> InvalidImports()
    {
        var package = new TerminologyPackage(new string('a', 64), "ct.txt", "https://evs.nci.nih.gov/ftp1/CDISC/", 10, "nci-evs-tsv");
        CodeSystemEntryData[] entries = [new("C1", "One")];
        return new()
        {
            { " ", package, entries, KnowledgeErrors.VersionRequired.Code },
            { "2024-09-27", package with { Sha256 = "abc" }, entries, KnowledgeErrors.PackageInvalid.Code },
            { "2024-09-27", package, [], KnowledgeErrors.EntriesInvalid.Code },
            { "2024-09-27", package, [new("C1", "One", "G"), new("C1", "Uno", "G")], KnowledgeErrors.DuplicateEntry.Code },
        };
    }

    /// <summary>The same code may appear in several groups (a term in two codelists).</summary>
    [Fact(DisplayName = "CodeSystemVersion_Import_SameCodeInTwoGroups_IsAccepted")]
    public void CodeSystemVersionImportSameCodeInTwoGroupsIsAccepted()
    {
        var package = new TerminologyPackage(new string('b', 64), "ct.txt", "https://evs.nci.nih.gov/ftp1/CDISC/", 10, "nci-evs-tsv");
        var result = CodeSystemVersion.Import(CodeSystemVersionId.New(), Tenant, CodeSystemId.New(), "2024-09-27", new DateOnly(2024, 9, 27), package,
            [new("C62122", "Sitting", "C78431"), new("C62122", "Sitting", "C71148")]);

        result.Value!.Entries.Select(e => e.Ordinal).Should().Equal(0, 1);
    }

    /// <summary>Mapping review: proposed → confirmed or rejected, confirmed → rejected, and nothing leaves rejected.</summary>
    [Theory(DisplayName = "ConceptMapping_Review_FollowsTheReviewStateMachine")]
    [InlineData(false, MappingStatus.Confirmed, true)]
    [InlineData(false, MappingStatus.Rejected, true)]
    [InlineData(false, MappingStatus.Proposed, false)]
    [InlineData(true, MappingStatus.Rejected, true)]
    [InlineData(true, MappingStatus.Confirmed, false)]
    [InlineData(true, MappingStatus.Proposed, false)]
    public void ConceptMappingReviewFollowsTheReviewStateMachine(bool confirmed, MappingStatus next, bool allowed)
    {
        var mapping = ConceptMapping.Create(ConceptMappingId.New(), Tenant, new ConceptRef(ConceptId.New(), 1),
            new Coding(CodeSystemVersionId.New(), "8480-6"), MappingRelation.Exact, "CDISC BC coding", confirmed).Value!;

        (mapping.Review(next) is null).Should().Be(allowed);
    }

    /// <summary>A mapping without evidence is refused.</summary>
    [Fact(DisplayName = "ConceptMapping_Create_WithoutEvidence_IsRefused")]
    public void ConceptMappingCreateWithoutEvidenceIsRefused()
    {
        ConceptMapping.Create(ConceptMappingId.New(), Tenant, new ConceptRef(ConceptId.New(), 1),
                new Coding(CodeSystemVersionId.New(), "8480-6"), MappingRelation.Related, " ", false)
            .Error.Should().Be(KnowledgeErrors.EvidenceRequired);
    }

    /// <summary>
    /// K-SEM-2: the repository ships the structure of licensed code systems, never their content. Fails on any file
    /// named like a MedDRA ASCII release, a WHODrug release or a SNOMED CT RF2 release.
    /// </summary>
    [Fact(DisplayName = "Repository_ContainsNoLicensedTerminologyContent")]
    public void RepositoryContainsNoLicensedTerminologyContent()
    {
        var root = RepositoryRoot();
        string[] medDraFiles = ["llt.asc", "pt.asc", "hlt.asc", "hlgt.asc", "soc.asc", "mdhier.asc", "hlt_pt.asc", "hlgt_hlt.asc", "soc_hlgt.asc", "intl_ord.asc"];
        var offending = new List<string>();
        foreach (var file in Files(root))
        {
            var name = Path.GetFileName(file).ToLowerInvariant();
            if (medDraFiles.Contains(name)
                || name.StartsWith("sct2_", StringComparison.Ordinal) || name.StartsWith("der2_", StringComparison.Ordinal)
                || (name.Contains("whodrug", StringComparison.Ordinal) && !name.EndsWith(".md", StringComparison.Ordinal) && !name.EndsWith(".cs", StringComparison.Ordinal))
                || name is "dd.txt" or "ddsource.txt" or "ina.txt" or "ing.txt" or "bna.txt")
            {
                offending.Add(Path.GetRelativePath(root, file));
            }
        }

        offending.Should().BeEmpty("licensed terminology is loaded per tenant under its licence, never committed (K-SEM-2)");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cadence.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Cadence.sln not found above the test output.");
    }

    private static IEnumerable<string> Files(string directory)
    {
        string[] skipped = [".git", "node_modules", "bin", "obj", ".pnpm-store"];
        var pending = new Stack<string>([directory]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }

            foreach (var sub in Directory.EnumerateDirectories(current))
            {
                if (!skipped.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
        }
    }
}
