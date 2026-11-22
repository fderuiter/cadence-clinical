using System;

using Cadence.Kernel;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Trust;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The content hash a form signature binds to (spec §9.5, 21 CFR 11.70, #5463): the exact revisions of the form's values,
/// whatever order they are read in, and a different hash after any correction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CollectionContentHashTests
{
    private static readonly ObservationId Systolic = ObservationId.FromGuid(Guid.Parse("01900000-0000-7000-8000-000000000001"));
    private static readonly ObservationId Diastolic = ObservationId.FromGuid(Guid.Parse("01900000-0000-7000-8000-000000000002"));

    private static readonly CollectionContentEntry[] Form =
    [
        new(Systolic, 1, new QuantityValue(120m, "mm[Hg]")),
        new(Diastolic, 1, new QuantityValue(80m, "mm[Hg]")),
    ];

    /// <summary>The order the values are read in does not change the hash, which is a well-formed content hash.</summary>
    [Fact(DisplayName = "ContentHashOf_IgnoresReadOrder")]
    public void ContentHashOfIgnoresReadOrder()
    {
        var hash = CollectionInstance.ContentHashOf(Form);

        CollectionInstance.ContentHashOf([Form[1], Form[0]]).Should().Be(hash);
        ContentHash.IsWellFormed(hash).Should().BeTrue();
    }

    /// <summary>
    /// A new revision changes the hash even when it restores the same value, as do a different value, unit or kind, a
    /// different observation and a missing or added value: a signature names exactly what the signer saw.
    /// </summary>
    [Fact(DisplayName = "ContentHashOf_ChangesWithEveryRevisionAndValue")]
    public void ContentHashOfChangesWithEveryRevisionAndValue()
    {
        var hash = CollectionInstance.ContentHashOf(Form);
        CollectionContentEntry[][] changed =
        [
            [Form[0] with { Revision = 2 }, Form[1]],
            [Form[0] with { Value = new QuantityValue(121m, "mm[Hg]") }, Form[1]],
            [Form[0] with { Value = new QuantityValue(120m, "kPa") }, Form[1]],
            [Form[0] with { Value = new TextValue("120") }, Form[1]],
            [Form[0] with { ObservationId = ObservationId.New() }, Form[1]],
            [Form[0]],
            [.. Form, new(ObservationId.New(), 1, new TextValue("sitting"))],
        ];

        foreach (var content in changed)
        {
            CollectionInstance.ContentHashOf(content).Should().NotBe(hash);
        }
    }

    /// <summary>
    /// Revision-1 <see cref="NullFlavor.NotAsked"/> placeholders seeded for automated discrepancy queries do not alter
    /// the signed content hash, while any clinical missing flavor or subsequent revision supersedes the signature.
    /// </summary>
    [Fact(DisplayName = "ContentHashOf_ExcludesRevision1NotAskedPlaceholders_UntilClinicallyEdited")]
    public void ContentHashOfExcludesRevision1NotAskedPlaceholdersUntilClinicallyEdited()
    {
        var baselineHash = CollectionInstance.ContentHashOf(Form);
        var unaskedId = ObservationId.FromGuid(Guid.Parse("01900000-0000-7000-8000-000000000003"));
        CollectionContentEntry[] withUnaskedPlaceholder =
        [
            .. Form,
            new(unaskedId, 1, new MissingValue(NullFlavor.NotAsked)),
        ];

        CollectionInstance.HasSignableContent(withUnaskedPlaceholder).Should().BeTrue();
        CollectionInstance.ContentHashOf(withUnaskedPlaceholder).Should().Be(baselineHash);

        // Once corrected (revision 2) or recorded with a clinical missing reason (NotDone), the hash changes.
        CollectionInstance.ContentHashOf([.. Form, new(unaskedId, 2, new QuantityValue(72m, "/min"))])
            .Should().NotBe(baselineHash);
        CollectionInstance.ContentHashOf([.. Form, new(unaskedId, 2, new MissingValue(NullFlavor.NotAsked))])
            .Should().NotBe(baselineHash);
        CollectionInstance.ContentHashOf([.. Form, new(unaskedId, 1, new MissingValue(NullFlavor.NotDone, "Refused"))])
            .Should().NotBe(baselineHash);
    }

    /// <summary>A form whose only observations are revision-1 <see cref="NullFlavor.NotAsked"/> placeholders has no signable content.</summary>
    [Fact(DisplayName = "HasSignableContent_IsFalse_WhenFormContainsOnlyRevision1NotAskedPlaceholders")]
    public void HasSignableContentIsFalseWhenFormContainsOnlyRevision1NotAskedPlaceholders()
    {
        CollectionContentEntry[] placeholdersOnly =
        [
            new(ObservationId.New(), 1, new MissingValue(NullFlavor.NotAsked)),
            new(ObservationId.New(), 1, new MissingValue(NullFlavor.NotAsked)),
        ];

        CollectionInstance.HasSignableContent([]).Should().BeFalse();
        CollectionInstance.HasSignableContent(placeholdersOnly).Should().BeFalse();
    }
}
