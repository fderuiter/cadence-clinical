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
            Description = $"Reconciliation profile: '{RulesetService.ActiveActionsProfile}' (default; full declarative rulesets including status checks and merge_queue) or '{RulesetService.PausedActionsProfile}' (omits status checks and merge_queue while hosted Actions runners are paused)",
            DefaultValueFactory = _ => RulesetService.ActiveActionsProfile,
        };
        profile.Validators.Add(ValidateProfileOption);
        var verifyLive = new Option<bool>("--verify-live")
        {
            Description = "Query live GitHub Repository Rulesets and GitHub Pages deployment status via gh api and exit non-zero on drift without modifying GitHub",
        };

        command.Options.Add(dryRun);
        command.Options.Add(repo);
        command.Options.Add(profile);
        command.Options.Add(verifyLive);
        command.Validators.Add(result =>
        {
            if (result.GetValue(dryRun) && result.GetValue(verifyLive))
            {
                result.AddError("Options '--dry-run' and '--verify-live' cannot be used together.");
            }
        });
        command.SetAction(result => RulesetService.Sync(
            dryRun: result.GetValue(dryRun),
            repoOverride: result.GetValue(repo),
            profile: result.GetValue(profile) ?? RulesetService.ActiveActionsProfile,
            verifyLive: result.GetValue(verifyLive)));
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
