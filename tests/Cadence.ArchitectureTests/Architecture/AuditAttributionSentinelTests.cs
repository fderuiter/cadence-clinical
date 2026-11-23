using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Testing;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Keeps anonymous attribution out of the codebase (#4767): no actor-style member (<c>CreatedBy</c>,
/// <c>ReaderId</c>, <c>UserId</c>, and so on) may default to a literal such as <c>"system"</c>. An actor is
/// either the authenticated caller or a named <c>svc:</c> service account set explicitly at the point of work.
/// </summary>
public partial class AuditAttributionSentinelTests
{
    [Fact(DisplayName = "ActorMembers_MustNotDefaultTo_AnonymousSystemIdentity")]
    [Trait("Category", "Architecture")]
    public void ActorMembersMustNotDefaultToAnonymousSystemIdentity()
    {
        string root = FindRepositoryRoot();
        var offenders = new[] { "apps", "packages" }
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(IsProductionSource)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, line, number: index + 1)))
            .Where(x => AnonymousActorDefault().IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(root, x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "writes must be attributed to the authenticated caller or a named svc: service account, never a default such as \"system\"");
    }

    [Theory(DisplayName = "AnonymousActorDefault_Pattern_CatchesKnownShapes")]
    [Trait("Category", "Architecture")]
    [InlineData("    public string CreatedBy { get; set; } = \"system\";", true)]
    [InlineData("    public string ReaderId { get; set; } = \"system\";", true)]
    [InlineData("    private const string DefaultUserId = \"system\";", true)]
    [InlineData("        string userId = \"system\";", true)]
    [InlineData("    public const string SystemUserId = \"system-pubsub\";", true)]
    [InlineData("    public string CreatedBy { get; set; } = string.Empty;", false)]
    [InlineData("    public required string CreatedBy { get; set; }", false)]
    [InlineData("    private const string DefaultChangeReason = \"system_operation\";", false)]
    [InlineData("        new(\"svc:econsent-consumer\", \"Automated consent-completion enrollment\");", false)]
    public void AnonymousActorDefaultPatternCatchesKnownShapes(string line, bool flagged)
    {
        AnonymousActorDefault().IsMatch(line).Should().Be(flagged);
    }

    // An actor-named member or local (…By, …UserId, …ReaderId, …Actor), optionally a { get; set; } property,
    // assigned a "system…" literal.
    [GeneratedRegex(@"\b\w*(By|UserId|ReaderId|Actor)\b(\s*\{[^}]*\})?\s*=\s*""system[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex AnonymousActorDefault();

    private static bool IsProductionSource(string path)
    {
        string normalized = path.Replace('\\', '/');
        return !normalized.Contains("/obj/", StringComparison.Ordinal)
            && !normalized.Contains("/bin/", StringComparison.Ordinal)
            && !normalized.Contains("/Migrations/", StringComparison.Ordinal)
            && !normalized.Contains("/tests/", StringComparison.Ordinal)
            && !normalized.Contains("/packages/testing/", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);
}
