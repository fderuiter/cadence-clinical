using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;

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
        repo.Validators.Add(RejectOptionLikeValue);
        var profile = new Option<string>("--profile")
        {
            Description = $"Reconciliation profile: '{RulesetService.PausedActionsProfile}' (default while Actions is paused; omits status checks and merge_queue) or '{RulesetService.ActiveActionsProfile}' (full declarative rulesets)",
            DefaultValueFactory = _ => RulesetService.PausedActionsProfile,
        };
        profile.Validators.Add(ValidateProfileOption);

        command.Options.Add(dryRun);
        command.Options.Add(repo);
        command.Options.Add(profile);
        command.SetAction(result => RulesetService.Sync(
            dryRun: result.GetValue(dryRun),
            repoOverride: result.GetValue(repo),
            profile: result.GetValue(profile) ?? RulesetService.PausedActionsProfile));
    }

    private static void RejectOptionLikeValue(OptionResult optionResult)
    {
        foreach (string value in optionResult.Tokens.Select(token => token.Value))
        {
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-'))
            {
                optionResult.AddError(
                    $"Option '{optionResult.Option.Name}' requires a value, got '{value}'.");
            }
        }
    }

    private static void ValidateProfileOption(OptionResult optionResult)
    {
        foreach (string value in optionResult.Tokens.Select(token => token.Value))
        {
            if (!RulesetService.TryNormalizeProfile(value, out _))
            {
                optionResult.AddError(
                    $"Unsupported ruleset profile '{value}'. Expected '{RulesetService.PausedActionsProfile}' or '{RulesetService.ActiveActionsProfile}'.");
            }
        }
    }
}
