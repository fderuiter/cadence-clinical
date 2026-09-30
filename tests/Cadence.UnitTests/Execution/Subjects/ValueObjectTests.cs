using System;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Boundary and validation coverage for the strongly-typed IDs and value objects introduced by
/// `MIG-SUBJ-001`: two `Guid`-backed surrogate keys and three validated string wrapper types.
/// </summary>
public class ValueObjectTests
{
    [Fact]
    public void SubjectId_New_ProducesUniqueValues()
    {
        var first = SubjectId.New();
        var second = SubjectId.New();

        first.Should().NotBe(second);
    }

    [Fact]
    public void SubjectId_ToString_ReturnsGuidValue()
    {
        var guid = Guid.NewGuid();
        var id = new SubjectId(guid);

        id.ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void SubjectConsentId_New_ProducesUniqueValues()
    {
        var first = SubjectConsentId.New();
        var second = SubjectConsentId.New();

        first.Should().NotBe(second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SubjectCode_WhitespaceOrEmpty_Throws(string value)
    {
        var act = () => new SubjectCode(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SubjectCode_ExceedsMaxLength_Throws()
    {
        var tooLong = new string('a', 256);

        var act = () => new SubjectCode(tooLong);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SubjectCode_AtMaxLength_IsAccepted()
    {
        var atLimit = new string('a', 255);

        var code = new SubjectCode(atLimit);

        code.Value.Should().Be(atLimit);
    }

    [Fact]
    public void SubjectCode_ToString_ReturnsValue()
    {
        var code = new SubjectCode("SUBJ-001");

        code.ToString().Should().Be("SUBJ-001");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StudyCode_WhitespaceOrEmpty_Throws(string value)
    {
        var act = () => new StudyCode(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StudyCode_ExceedsMaxLength_Throws()
    {
        var tooLong = new string('a', 256);

        var act = () => new StudyCode(tooLong);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StudyCode_AtMaxLength_IsAccepted()
    {
        var atLimit = new string('a', 255);

        var code = new StudyCode(atLimit);

        code.Value.Should().Be(atLimit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SiteCode_WhitespaceOrEmpty_Throws(string value)
    {
        var act = () => new SiteCode(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SiteCode_ValidValue_IsAccepted()
    {
        var code = new SiteCode("SITE-042");

        code.Value.Should().Be("SITE-042");
    }

    [Fact]
    public void SiteCode_AsNullableParameter_MayBeAbsent()
    {
        SiteCode? siteCode = null;

        siteCode.Should().BeNull();
    }

    [Fact]
    public void SubjectCode_Equality_IsValueBased()
    {
        var a = new SubjectCode("SUBJ-001");
        var b = new SubjectCode("SUBJ-001");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }
}
