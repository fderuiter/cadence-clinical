using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using Cadence.Kernel;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Standards;
using Cadence.Standards;
using Cadence.Standards.CdiscLibrary;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Standards;

/// <summary>
/// Standards Registry model without a database (spec §6.3, #4809): package validation, the content hash, governance
/// transitions, study declarations, and the CDISC Library loader over the shipped seeds.
/// </summary>
public class StandardsRegistryTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Fact(DisplayName = "StandardSeeds_Load_ReadsSdtmigAndAdamigSubsets")]
    [Trait("Category", "Unit")]
    public void StandardSeedsLoadReadsSdtmigAndAdamigSubsets()
    {
        var seeds = StandardSeeds.LoadAll();

        var sdtmig = seeds.Single(s => s.Package.StandardCode == "SDTMIG").Package;
        sdtmig.Version.Should().Be("3.4");
        sdtmig.Publisher.Should().Be("CDISC");
        sdtmig.Source.Should().Be("/mdr/sdtmig/3-4");
        sdtmig.EffectiveDate.Should().Be(new DateOnly(2021, 11, 29));
        sdtmig.Datasets.Select(d => d.Name).Should().BeEquivalentTo("DM", "AE", "VS", "LB");
        sdtmig.ControlledTerminology.Should().ContainSingle().Which.Should().Be(new CodeSystemVersionRef("sdtmct", "2024-09-27"));

        var vs = sdtmig.Datasets.Single(d => d.Name == "VS");
        vs.Class.Should().Be("Findings");
        var testCode = vs.Variables.Single(v => v.Name == "VSTESTCD");
        testCode.Codelists.Should().Equal("C66741");
        testCode.Core.Should().Be(VariableCore.Required);
        testCode.Role.Should().Be("Topic");
        vs.Variables.Single(v => v.Name == "VSSTRESN").DataType.Should().Be(StandardDataType.Num);
        vs.Variables.Single(v => v.Name == "DOMAIN").ValueList.Should().Equal("VS");
        vs.Variables.Single(v => v.Name == "VSDTC").DescribedValueDomain.Should().Be("ISO 8601 datetime or interval");

        var adsl = seeds.Single(s => s.Package.StandardCode == "ADaMIG").Package.Datasets.Should().ContainSingle().Subject;
        adsl.Name.Should().Be("ADSL");
        adsl.Variables.Should().Contain(v => v.Name == "USUBJID" && v.Core == VariableCore.Required && v.Role == "Identifier");
        adsl.Variables.Should().Contain(v => v.Core == VariableCore.Conditional);

        foreach (var seed in seeds)
        {
            StandardPackageContent.Validate(seed.Package).Should().BeEmpty($"{seed.File} is valid registry content");
        }
    }

    [Fact(DisplayName = "ChecksumVerifier_RejectsChangedContent")]
    [Trait("Category", "Unit")]
    public void ChecksumVerifierRejectsChangedContent()
    {
        var content = Encoding.UTF8.GetBytes("""{"name":"SDTMIG v3.4"}""");
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

        ((Action)(() => ChecksumVerifier.Verify(content, sha, "seed"))).Should().NotThrow();

        content[^2] = (byte)'5';
        ((Action)(() => ChecksumVerifier.Verify(content, sha, "seed"))).Should().Throw<InvalidDataException>()
            .WithMessage("*changed or corrupted*");
    }

    [Theory(DisplayName = "CdiscLibraryProductReader_ParsesCodelistLinks")]
    [InlineData("/mdr/ct/packages/sdtmct-2024-09-27/codelists/C66741", "C66741", "sdtmct", "2024-09-27")]
    [InlineData("https://library.cdisc.org/api/mdr/ct/packages/adamct-2021-12-17/codelists/C81224", "C81224", "adamct", "2021-12-17")]
    [InlineData("/mdr/root/ct/sdtmct/codelists/C66742", "C66742", null, null)]
    [Trait("Category", "Unit")]
    public void CdiscLibraryProductReaderParsesCodelistLinks(string href, string codelist, string? codeSystem, string? version)
    {
        var (parsed, package) = CdiscLibraryProductReader.ParseCodelistHref(href, "X");

        parsed.Should().Be(codelist);
        package.Should().Be(codeSystem is null ? null : new CodeSystemVersionRef(codeSystem, version!));
    }

    [Fact(DisplayName = "CdiscLibraryProductReader_AcceptsSingleLinkObjects_AndRejectsUnknownTypes")]
    [Trait("Category", "Unit")]
    public void CdiscLibraryProductReaderAcceptsSingleLinkObjectsAndRejectsUnknownTypes()
    {
        const string product = """
            {"name":"SENDIG v3.1","version":"3.1","_links":{"self":{"href":"/mdr/sendig/3-1"}},
             "classes":[{"name":"Findings","datasets":[{"name":"BW","label":"Body Weight","datasetVariables":[
               {"ordinal":1,"name":"BWTESTCD","label":"Test Short Name","simpleDatatype":"Char","core":"Req",
                "_links":{"codelist":{"href":"/mdr/ct/packages/sendct-2024-09-27/codelists/C89963"}}}]}]}]}
            """;

        var package = CdiscLibraryProductReader.Read(Encoding.UTF8.GetBytes(product), "SENDIG");

        package.Datasets.Single().Variables.Single().Codelists.Should().Equal("C89963");
        package.ControlledTerminology.Should().Equal(new CodeSystemVersionRef("sendct", "2024-09-27"));

        var unknownType = product.Replace("\"Char\"", "\"Text\"", StringComparison.Ordinal);
        ((Action)(() => CdiscLibraryProductReader.Read(Encoding.UTF8.GetBytes(unknownType), "SENDIG")))
            .Should().Throw<FormatException>().WithMessage("*simpleDatatype 'Text'*");
    }

    [Fact(DisplayName = "StandardPackageContent_Hash_IgnoresOrder_AndDetectsChanges")]
    [Trait("Category", "Unit")]
    public void StandardPackageContentHashIgnoresOrderAndDetectsChanges()
    {
        var package = Seed("SDTMIG");
        var reordered = package with
        {
            Datasets = [.. package.Datasets.Reverse().Select(d => d with { Variables = [.. d.Variables.Reverse()] })],
        };
        var changed = package with
        {
            Datasets = [.. package.Datasets.Select(d => d.Name == "VS"
                ? d with { Variables = [.. d.Variables.Select(v => v.Name == "VSORRESU" ? v with { Core = VariableCore.Required } : v)] }
                : d)],
        };

        var hash = StandardPackageContent.Hash(StandardPackageContent.Normalize(package));

        hash.Should().StartWith(CanonicalJson.HashPrefix).And.HaveLength(71);
        StandardPackageContent.Hash(StandardPackageContent.Normalize(reordered)).Should().Be(hash);
        StandardPackageContent.Hash(StandardPackageContent.Normalize(changed)).Should().NotBe(hash);
    }

    [Fact(DisplayName = "StandardVersion_ToPackage_RehashesToItsContentHash")]
    [Trait("Category", "Unit")]
    public void StandardVersionToPackageRehashesToItsContentHash()
    {
        var (version, error) = StandardVersion.Register(StandardVersionId.New(), Tenant, Seed("ADaMIG"), expectedContentHash: null);

        error.Should().BeNull();
        version!.Status.Should().Be(GovernanceStatus.Draft);
        StandardPackageContent.Hash(version.ToPackage()).Should().Be(version.ContentHash);
    }

    [Fact(DisplayName = "StandardVersion_Register_RejectsInvalidPackages_AndHashMismatch")]
    [Trait("Category", "Unit")]
    public void StandardVersionRegisterRejectsInvalidPackagesAndHashMismatch()
    {
        var package = Seed("SDTMIG");
        var vs = package.Datasets.Single(d => d.Name == "VS");
        var duplicate = package with { Datasets = [.. package.Datasets, vs] };
        var badCodelist = package with
        {
            Datasets = [vs with { Variables = [vs.Variables[0] with { Codelists = ["66741"] }] }],
        };
        var orphanValueLevel = package with
        {
            ValueLevelDefinitions = [new ValueLevelDefinitionData("VS", "VSORRESU", [new WhereCondition("NOPE", ["SYSBP"])], StandardDataType.Char, null, [], ["mmHg"], null)],
        };

        StandardVersion.Register(StandardVersionId.New(), Tenant, duplicate, null).Error!.Message.Should().Contain("Dataset VS appears more than once");
        StandardVersion.Register(StandardVersionId.New(), Tenant, badCodelist, null).Error!.Message.Should().Contain("not an NCI C-code");
        StandardVersion.Register(StandardVersionId.New(), Tenant, orphanValueLevel, null).Error!.Message.Should().Contain("conditions on NOPE");
        StandardVersion.Register(StandardVersionId.New(), Tenant, package, "sha256:00").Error!.Code.Should().Be(StandardsErrors.ContentHashMismatch.Code);
    }

    [Fact(DisplayName = "StandardVersion_Governance_FollowsDraftApprovedDeprecated")]
    [Trait("Category", "Unit")]
    public void StandardVersionGovernanceFollowsDraftApprovedDeprecated()
    {
        var version = StandardVersion.Register(StandardVersionId.New(), Tenant, Seed("SDTMIG"), null).Version!;
        var approval = AttestationId.New();
        var deprecation = AttestationId.New();

        version.Approve(approval).Should().BeNull();
        version.Approve(AttestationId.New())!.Code.Should().Be(StandardsErrors.InvalidTransition.Code);
        version.Deprecate(deprecation).Should().BeNull();
        version.Deprecate(AttestationId.New())!.Code.Should().Be(StandardsErrors.InvalidTransition.Code);

        version.Status.Should().Be(GovernanceStatus.Deprecated);
        version.ApprovalAttestationId.Should().Be(approval);
        version.DeprecationAttestationId.Should().Be(deprecation);
    }

    [Fact(DisplayName = "LibraryItem_Draft_StoresCanonicalContent_AndHash")]
    [Trait("Category", "Unit")]
    public void LibraryItemDraftStoresCanonicalContentAndHash()
    {
        using var a = JsonDocument.Parse("""{ "b": [1, 2], "a": "xA" }""");
        using var b = JsonDocument.Parse("""{"a":"xA","b":[1,2]}""");

        var first = LibraryItem.Draft(LibraryItemId.New(), Tenant, LibraryItemKind.CodelistSubset, "VS units", 1, "Vital sign units", a.RootElement, []).Item!;
        var second = LibraryItem.Draft(LibraryItemId.New(), Tenant, LibraryItemKind.CodelistSubset, "VS units", 2, "Vital sign units", b.RootElement, []).Item!;

        first.Content.Should().Be("""{"a":"xA","b":[1,2]}""");
        first.ContentHash.Should().Be(second.ContentHash);
        first.Status.Should().Be(GovernanceStatus.Draft);

        using var array = JsonDocument.Parse("[1]");
        LibraryItem.Draft(LibraryItemId.New(), Tenant, LibraryItemKind.CodelistSubset, "k", 1, "t", array.RootElement, []).Error!.Code
            .Should().Be(StandardsErrors.LibraryItemInvalid.Code);
        LibraryItem.Draft(LibraryItemId.New(), Tenant, LibraryItemKind.CodelistSubset, " k", 1, "t", b.RootElement, []).Error!.Code
            .Should().Be(StandardsErrors.LibraryItemInvalid.Code);
    }

    [Fact(DisplayName = "StudyStandardsProfile_Declare_AcceptsOnlyApprovedAndOneVersionEach")]
    [Trait("Category", "Unit")]
    public void StudyStandardsProfileDeclareAcceptsOnlyApprovedAndOneVersionEach()
    {
        var sdtmig34 = Summary("SDTMIG", "3.4", GovernanceStatus.Approved);
        var sdtmig33 = Summary("SDTMIG", "3.3", GovernanceStatus.Approved);
        var draft = Summary("ADaMIG", "1.3", GovernanceStatus.Draft);
        var standards = new[] { sdtmig34, sdtmig33, draft }.ToDictionary(s => s.Id);
        var pinV1 = Item("VS units", 1, GovernanceStatus.Approved);
        var pinV2 = Item("VS units", 2, GovernanceStatus.Approved);
        var pins = new[] { pinV1, pinV2 }.ToDictionary(p => p.Id);
        var sdtmct = new CodeSystemVersionRef("sdtmct", "2024-09-27");

        var profile = StudyStandardsProfile.Create(StudyStandardsProfileId.New(), Tenant, StudyId.New());

        profile.Declare([sdtmig34.Id, sdtmig33.Id], [sdtmct], [], standards, pins)!.Message.Should().Contain("one version of SDTMIG");
        profile.Declare([draft.Id], [sdtmct], [], standards, pins)!.Code.Should().Be(StandardsErrors.NotApproved.Code);
        profile.Declare([StandardVersionId.New()], [sdtmct], [], standards, pins)!.Code.Should().Be(StandardsErrors.StandardVersionUnknown.Code);
        profile.Declare([sdtmig34.Id], [sdtmct, sdtmct with { Version = "2023-12-15" }], [], standards, pins)!.Message.Should().Contain("one version of sdtmct");
        profile.Declare([sdtmig34.Id], [sdtmct], [pinV1.Id, pinV2.Id], standards, pins)!.Message.Should().Contain("pins one version");
        profile.Declare([], [sdtmct], [], standards, pins)!.Code.Should().Be(StandardsErrors.DeclarationInvalid.Code);

        profile.Declare([sdtmig34.Id], [sdtmct], [pinV1.Id], standards, pins).Should().BeNull();
        profile.StandardVersionIds.Should().Equal(sdtmig34.Id);
        profile.ControlledTerminologyRefs.Should().Equal(sdtmct);
        profile.LibraryPins.Should().Equal(pinV1.Id);
    }

    private static StandardPackage Seed(string code) =>
        StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == code).Package;

    private static StandardVersionSummary Summary(string code, string version, GovernanceStatus status) =>
        new(StandardVersionId.New(), code, version, code, "CDISC", null, null, "sha256:x", status, null, null, []);

    private static LibraryItemView Item(string key, int version, GovernanceStatus status)
    {
        using var content = JsonDocument.Parse("{}");
        return new LibraryItemView(
            LibraryItemId.New(), LibraryItemKind.CodelistSubset, key, version, key, content.RootElement.Clone(), "sha256:x", status,
            new List<StandardVersionId>(), null, null);
    }
}
