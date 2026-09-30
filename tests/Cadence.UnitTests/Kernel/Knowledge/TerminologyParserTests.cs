using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Knowledge.Terminology;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.TestSupport.Kernel.Knowledge;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Kernel.Knowledge;

/// <summary>Parsers for published terminology formats and the checksum-verifying loader (spec §6.1, ADR-2470).</summary>
[Trait("Category", "Unit")]
public class TerminologyParserTests
{
    /// <summary>A codelist row becomes an ungrouped entry; each term is grouped under its codelist's C-code.</summary>
    [Fact(DisplayName = "CdiscCt_Parse_GroupsTermsUnderTheirCodelist")]
    public void CdiscCtParseGroupsTermsUnderTheirCodelist()
    {
        var entries = new CdiscControlledTerminologyParser("sdtmct").Parse(new MemoryStream(TerminologyFixtures.CdiscCt));

        entries.Should().HaveCount(8);
        var codelist = entries.Single(e => e.Code == "C78431");
        codelist.GroupCode.Should().BeNull();
        codelist.Display.Should().Be("Vital Signs Position of Subject");
        codelist.SubmissionValue.Should().Be("VSPOS");

        var standing = entries.Single(e => e.Code == "C62166");
        standing.GroupCode.Should().Be("C78431");
        standing.Display.Should().Be("Standing");
        standing.SubmissionValue.Should().Be("STANDING");
        standing.Synonyms.Should().Equal("Orthostatic", "Standing");
    }

    /// <summary>A CT file without the published columns is refused rather than half-read.</summary>
    [Fact(DisplayName = "CdiscCt_Parse_MissingColumns_Throws")]
    public void CdiscCtParseMissingColumnsThrows()
    {
        var act = () => new CdiscControlledTerminologyParser("sdtmct").Parse(new MemoryStream("Code\tName\nC1\tx\n"u8.ToArray()));

        act.Should().Throw<FormatException>().WithMessage("*Codelist Code*");
    }

    /// <summary>The LOINC table reader handles quoted commas and doubled quotes, and groups by CLASS.</summary>
    [Fact(DisplayName = "Loinc_Parse_ReadsQuotedCsv_AndGroupsByClass")]
    public void LoincParseReadsQuotedCsvAndGroupsByClass()
    {
        var entries = new LoincTableParser().Parse(new MemoryStream(TerminologyFixtures.LoincTable));

        entries.Select(e => e.Code).Should().Equal("8480-6", "8462-4", "2823-3");
        entries[2].Display.Should().Be("Potassium [Moles/volume] in Serum or Plasma, \"measured\"");
        entries[2].GroupCode.Should().Be("CHEM");
        entries[0].Synonyms.Should().Equal("BP sys");
    }

    /// <summary>UCUM atoms are grouped by property and prefixes under "prefix".</summary>
    [Fact(DisplayName = "Ucum_Parse_ReadsPrefixesAndUnits")]
    public void UcumParseReadsPrefixesAndUnits()
    {
        var entries = new UcumEssenceParser().Parse(new MemoryStream(TerminologyFixtures.UcumEssence));

        entries.Select(e => (e.Code, e.GroupCode)).Should().Equal(("m", "prefix"), ("g", "mass"), ("mm[Hg]", "pressure"));
        entries[2].SubmissionValue.Should().Be("mm Hg");
    }

    /// <summary>
    /// A package whose bytes do not match the expected SHA-256 is refused before anything is parsed or sent.
    /// </summary>
    [Fact(DisplayName = "Loader_ChecksumMismatch_IsRefused_AndNothingIsSent")]
    public async Task LoaderChecksumMismatchIsRefusedAndNothingIsSent()
    {
        var sender = Substitute.For<IKernelCommandSender>();
        var loader = new TerminologyPackageLoader(sender);

        var result = await loader.LoadAsync(Release(new string('0', 64)), TerminologyFixtures.CdiscCt, new CdiscControlledTerminologyParser("sdtmct"), TestContext.Current.CancellationToken);

        result.Error.Should().Be(KnowledgeErrors.PackageChecksumMismatch with { Message = result.Error!.Message });
        sender.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>A verified package is sent as one import naming its digest, with an idempotency key derived from it.</summary>
    [Fact(DisplayName = "Loader_VerifiedPackage_SendsOneImport_BoundToItsDigest")]
    public async Task LoaderVerifiedPackageSendsOneImportBoundToItsDigest()
    {
        var sender = Substitute.For<IKernelCommandSender>();
        ImportCodeSystemVersion? sent = null;
        sender.SendAsync(Arg.Do<ICommand<CodeSystemVersionId>>(c => sent = (ImportCodeSystemVersion)c), Arg.Any<CommandMetadata?>(), Arg.Any<CancellationToken>())
            .Returns(ci => CommandResult<CodeSystemVersionId>.Success(((ImportCodeSystemVersion)ci[0]).CodeSystemVersionId));
        var digest = TerminologyFixtures.Sha256(TerminologyFixtures.CdiscCt);

        var result = await new TerminologyPackageLoader(sender).LoadAsync(Release(digest.ToUpperInvariant()), TerminologyFixtures.CdiscCt, new CdiscControlledTerminologyParser("sdtmct"), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        sent!.Package.Sha256.Should().Be(digest);
        sent.Package.Format.Should().Be("nci-evs-tsv");
        sent.Package.ByteLength.Should().Be(TerminologyFixtures.CdiscCt.Length);
        sent.Entries.Should().HaveCount(8);
        sent.IdempotencyKey.Should().EndWith(digest);
    }

    private static TerminologyRelease Release(string sha) => new(
        TenantId.New(), CodeSystemId.New(), CodeSystemVersionId.New(), "2024-09-27", new DateOnly(2024, 9, 27),
        "CDASH Terminology.txt", "https://evs.nci.nih.gov/ftp1/CDISC/", sha, "Adopt CDASH CT 2024-09-27");
}
