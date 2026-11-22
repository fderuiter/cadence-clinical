using System.CommandLine;

using Cadence.Cli.Services;

namespace Cadence.Cli.Commands;

/// <summary>
/// <c>cadence rulesets</c>: validates <c>CI Gate</c> workflow coverage and declarative <c>.github/rulesets/*.json</c>
/// definitions (when present) and reconciles rulesets to GitHub Repository Rulesets via <c>gh api</c>.
/// </summary>
public static class RulesetsCommand
{
    public static void Configure(Command command)
    {
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Validate CI Gate workflow coverage and any declarative .github/rulesets/*.json files, printing planned gh api reconciliation without modifying GitHub",
        };
        var repo = new Option<string?>("--repo")
        {
            Description = "Target GitHub repository in 'owner/repo' format (defaults to GITHUB_REPOSITORY or git remote origin)",
        };

        command.Options.Add(dryRun);
        command.Options.Add(repo);
        command.SetAction(result => RulesetService.Sync(
            dryRun: result.GetValue(dryRun),
            repoOverride: result.GetValue(repo)));
    }
}
