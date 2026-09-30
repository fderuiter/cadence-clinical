using System;
using System.Linq;
using System.Text;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Evidence artifacts in the domain (spec §8.3): content descriptions, version numbering, the refusal of unchanged
/// content, and the target an attestation over a version signs. <c>KernelArtifactPostgresTests</c> covers the handlers
/// and the database guards.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ArtifactTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;
    private static readonly TenantId Tenant = TenantId.New();

    /// <summary>The hash of a byte string is the prefixed lower-case hex SHA-256 of exactly those bytes.</summary>
    [Fact(DisplayName = "ContentHash_OfBytes_IsPrefixedSha256OfTheBytes")]
    public void ContentHashOfBytesIsPrefixedSha256OfTheBytes()
    {
        var hash = ContentHash.OfBytes(Encoding.ASCII.GetBytes("abc"));

        hash.Should().Be("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        ContentHash.IsWellFormed(hash).Should().BeTrue();
        ContentHash.OfBytes(Encoding.ASCII.GetBytes("abd")).Should().NotBe(hash);
    }

    public static TheoryData<ArtifactContent> InvalidContent() =>
    [
        Content() with { ContentHash = "sha256:ABC" },
        Content() with { ContentHash = "md5:" + new string('a', 32) },
        Content() with { SizeBytes = 0 },
        Content() with { SizeBytes = -1 },
        Content() with { MediaType = "application" },
        Content() with { MediaType = "Application/PDF" },
        Content() with { MediaType = "application/pdf; charset=utf-8" },
        Content() with { MediaType = "application/" + new string('x', ArtifactContent.MaxMediaTypeLength) },
        Content() with { StorageRef = " " },
        Content() with { StorageRef = new string('s', ArtifactContent.MaxStorageRefLength + 1) },
    ];

    /// <summary>A content description needs a well-formed hash, at least one byte, a lower-case media type and a storage reference.</summary>
    [Theory(DisplayName = "ArtifactContent_RefusesMalformedDescriptions")]
    [MemberData(nameof(InvalidContent))]
    public void ArtifactContentRefusesMalformedDescriptions(ArtifactContent content)
    {
        content.Problem().Should().NotBeNull();

        var result = Artifact.Register(ArtifactId.New(), Tenant, ArtifactKind.LabReport, null, "Lab report", ArtifactVersionId.New(), content, T0);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(EvidenceErrors.ContentInvalid.Code);
    }

    /// <summary>Well-formed media types with vendor and suffix parts are accepted.</summary>
    [Theory(DisplayName = "ArtifactContent_AcceptsWellFormedMediaTypes")]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/fhir+json")]
    [InlineData("image/png")]
    public void ArtifactContentAcceptsWellFormedMediaTypes(string mediaType) =>
        (Content() with { MediaType = mediaType }).Problem().Should().BeNull();

    /// <summary>Registration checks the kind and title, trims the title and records version 1 at stored precision.</summary>
    [Fact(DisplayName = "Register_ValidatesAndRecordsVersionOne")]
    public void RegisterValidatesAndRecordsVersionOne()
    {
        var study = StudyId.New();
        var versionId = ArtifactVersionId.New();

        Register(kind: (ArtifactKind)999).Error.Should().Be(EvidenceErrors.KindInvalid);
        Register(title: " ").Error.Should().Be(EvidenceErrors.TitleInvalid);
        Register(title: new string('t', Artifact.MaxTitleLength + 1)).Error.Should().Be(EvidenceErrors.TitleInvalid);
        RegisterWithout().Error.Should().Be(EvidenceErrors.ContentInvalid);

        var artifact = Artifact.Register(ArtifactId.New(), Tenant, ArtifactKind.EthicsApproval, study, "  IRB approval  ", versionId, Content(), T0.AddTicks(7)).Value!;

        artifact.Title.Should().Be("IRB approval");
        artifact.StudyId.Should().Be(study);
        artifact.Versions.Should().ContainSingle();
        artifact.LatestVersion.Should().BeEquivalentTo(new
        {
            Id = versionId,
            TenantId = Tenant,
            ArtifactId = artifact.Id,
            Number = 1,
            Content().ContentHash,
            Content().SizeBytes,
            Content().MediaType,
            Content().StorageRef,
            RecordedAt = T0,
        });
    }

    /// <summary>Versions number on from the latest; content already held by any version is refused.</summary>
    [Fact(DisplayName = "AddVersion_NumbersOnAndRefusesUnchangedContent")]
    public void AddVersionNumbersOnAndRefusesUnchangedContent()
    {
        var artifact = Register().Value!;
        var second = Content("second");
        var secondId = ArtifactVersionId.New();

        artifact.AddVersion(secondId, second, T0.AddMinutes(1)).Should().BeNull();
        artifact.AddVersion(ArtifactVersionId.New(), Content(), T0.AddMinutes(2)).Should().Be(EvidenceErrors.ContentUnchanged);
        artifact.AddVersion(ArtifactVersionId.New(), second, T0.AddMinutes(2)).Should().Be(EvidenceErrors.ContentUnchanged);
        artifact.AddVersion(ArtifactVersionId.New(), second with { SizeBytes = 0 }, T0.AddMinutes(2))!.Code.Should().Be(EvidenceErrors.ContentInvalid.Code);

        artifact.Versions.Select(v => v.Number).Should().Equal(1, 2);
        artifact.LatestVersion.Id.Should().Be(secondId);
        artifact.Version(secondId)!.ContentHash.Should().Be(second.ContentHash);
        artifact.Version(ArtifactVersionId.New()).Should().BeNull();
    }

    /// <summary>An attestation over a version signs its kind, id and content hash (spec §10.3).</summary>
    [Fact(DisplayName = "ArtifactVersion_SignableTarget_NamesTheVersionAndItsHash")]
    public void ArtifactVersionSignableTargetNamesTheVersionAndItsHash()
    {
        var version = Register().Value!.LatestVersion;

        version.SignableTarget.Should().Be(new AttestationTarget(NodeKinds.ArtifactVersion, version.Id.Value, Content().ContentHash));
        NodeRef.For(version.Id).Kind.Should().Be(NodeKinds.ArtifactVersion);
        NodeRef.For(ArtifactId.New()).Kind.Should().Be(NodeKinds.Artifact);
    }

    private static CommandResult<Artifact> Register(ArtifactKind kind = ArtifactKind.SignedConsent, string title = "Signed consent") =>
        Artifact.Register(ArtifactId.New(), Tenant, kind, null, title, ArtifactVersionId.New(), Content(), T0);

    private static CommandResult<Artifact> RegisterWithout() =>
        Artifact.Register(ArtifactId.New(), Tenant, ArtifactKind.SignedConsent, null, "Signed consent", ArtifactVersionId.New(), content: null, T0);

    internal static ArtifactContent Content(string bytes = "first") =>
        new(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/pdf", $"blob://evidence/{bytes}");
}
