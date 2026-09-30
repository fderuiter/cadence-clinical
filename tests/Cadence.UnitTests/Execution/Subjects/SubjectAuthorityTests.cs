using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>The role rules behind the subject routes (#4943).</summary>
public sealed class SubjectAuthorityTests
{
    [Theory]
    [InlineData("investigator", true)]
    [InlineData("principal_investigator", true)]
    [InlineData("sub_investigator", true)]
    [InlineData("lead_investigator", true)]
    [InlineData("crc", true)]
    [InlineData("sponsor_dm", false)]
    [InlineData("data_manager", false)]
    [InlineData("Data Manager", false)]
    [InlineData("dm", false)]
    [InlineData("cra", false)]
    [InlineData("monitor", false)]
    [InlineData("auditor", false)]
    [InlineData("subject", false)]
    [InlineData("emergency_unblinder", false)]
    [InlineData("", false)]
    public void CanManageParticipants_AllowsSiteRolesOnly(string role, bool expected) =>
        SubjectAuthority.CanManageParticipants(role).Should().Be(expected);

    [Theory]
    [InlineData("principal_investigator", true)]
    [InlineData("Principal Investigator", true)]
    [InlineData("pi", true)]
    [InlineData("authorized_er_physician", true)]
    [InlineData("Authorized ER Physician", true)]
    [InlineData("emergency_unblinder", false)]
    [InlineData("lead_investigator", false)]
    [InlineData("investigator", false)]
    [InlineData("crc", false)]
    [InlineData("sponsor_dm", false)]
    [InlineData("pharmacist", false)]
    [InlineData("", false)]
    public void CanUnblind_AllowsTheUnblindingAuthoritiesOnly(string role, bool expected) =>
        SubjectAuthority.CanUnblind(role).Should().Be(expected);

    [Fact]
    public void AuthorizeStateChange_ToUnblinded_NeedsAnUnblindingAuthority_EvenForASiteRole()
    {
        var coordinator = new AuthenticatedCaller("crc-1", ["crc"]);
        var unblinder = new AuthenticatedCaller("er-1", ["authorized_er_physician"]);

        SubjectAuthority.AuthorizeStateChange(coordinator, "UNBLINDED")!.Code.Should().Be("UNAUTHORIZED_UNBLIND_ROLE");
        SubjectAuthority.AuthorizeStateChange(unblinder, "UNBLINDED").Should().BeNull();
        SubjectAuthority.AuthorizeStateChange(coordinator, "WITHDRAWN").Should().BeNull();
        SubjectAuthority.AuthorizeStateChange(unblinder, "WITHDRAWN")!.Code.Should().Be("UNAUTHORIZED_SUBJECT_ROLE");
    }

    [Fact]
    public void AuthorizeParticipantChange_RefusesAnUnauthenticatedCaller() =>
        SubjectAuthority.AuthorizeParticipantChange(null)!.Code.Should().Be("UNAUTHORIZED_SUBJECT_ROLE");
}
