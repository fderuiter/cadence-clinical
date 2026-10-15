using System;
using System.IO;

using Cadence.Cli.Services;
using Cadence.Compliance;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// <c>cadence gxp diff</c> (#5324): the generated documents carry the tested revision, so CI compares them with the
/// committed copies without that evidence block.
/// </summary>
public sealed class GxpDocsDiffServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gxp-diff-{Guid.NewGuid():N}");

    public GxpDocsDiffServiceTests() => Directory.CreateDirectory(Path.Combine(_dir, "docs", "SDLC"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Compare_PassesAndRestoresTheCommittedCopy_WhenOnlyTheEvidenceBlockChanged()
    {
        string committed = Document("`old`", "✅ **Qualified**");
        WriteWorking(Document("`new`", "✅ **Qualified**"));

        GxpDocsDiffService.Compare(_dir, restoreWhenEquivalent: true, readIndexed: _ => committed).Should().Be(0);

        foreach (var document in GxpDocsDiffService.Documents)
        {
            File.ReadAllText(Path.Combine(_dir, document)).Should().Be(committed);
        }
    }

    [Fact]
    public void Compare_Fails_WhenAResultChanged()
    {
        string committed = Document("`old`", "✅ **Qualified**");
        string regenerated = Document("`new`", "⚠️ **Not Verified**");
        WriteWorking(regenerated);

        GxpDocsDiffService.Compare(_dir, restoreWhenEquivalent: true, readIndexed: _ => committed).Should().Be(1);

        File.ReadAllText(Path.Combine(_dir, GxpDocsDiffService.Documents[0])).Should().Be(regenerated, "a real difference is left for the author to see");
    }

    private static string Document(string revision, string status) =>
        GxPSigner.StampGxpMarkdown($"# RTM\n\n{new RtmEvidenceContext(revision, "now", "db", "ci").Render()}\n| PRD-A-1 | {status} |\n");

    private void WriteWorking(string content)
    {
        foreach (var document in GxpDocsDiffService.Documents)
        {
            File.WriteAllText(Path.Combine(_dir, document), content);
        }
    }
}
