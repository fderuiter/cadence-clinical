using System;
using System.Globalization;
using System.Linq;

using FluentValidation;

namespace Cadence.Apps.Payments.Domain.Ledger;

/// <summary>
/// The limits the Python <c>GrantContract</c> model enforced when it was built (<c>financial/models.py</c>):
/// rates between 0 and 1, a holdback rate of at most 50%, non-negative amounts, and payee splits that total 100%.
/// </summary>
public sealed class GrantContractValidator : AbstractValidator<GrantContract>
{
    /// <summary>How far payee splits may drift from exactly 100% (the Python model accepted 0.999 to 1.001).</summary>
    public const decimal SplitTolerance = 0.001m;

    /// <summary>Creates the validator.</summary>
    public GrantContractValidator()
    {
        RuleFor(c => c.ContractId).NotEmpty();
        RuleFor(c => c.Currency).NotEmpty();
        RuleFor(c => c.DefaultOverheadRate).InclusiveBetween(0m, 1m);
        RuleFor(c => c.DefaultWithholdingRate).InclusiveBetween(0m, 1m);
        RuleFor(c => c.PaymentTermsDays).GreaterThanOrEqualTo(0);

        RuleFor(c => c.HoldbackRule.HoldbackRate).InclusiveBetween(0m, 0.50m).OverridePropertyName("HoldbackRule.HoldbackRate");
        RuleFor(c => c.HoldbackRule.CapAmount).GreaterThanOrEqualTo(0m).When(c => c.HoldbackRule.CapAmount is not null)
            .OverridePropertyName("HoldbackRule.CapAmount");

        RuleForEach(c => c.OverheadTiers).ChildRules(tier =>
        {
            tier.RuleFor(t => t.MinThreshold).GreaterThanOrEqualTo(0m);
            tier.RuleFor(t => t.MaxThreshold).GreaterThanOrEqualTo(0m).When(t => t.MaxThreshold is not null);
            tier.RuleFor(t => t.OverheadRate).InclusiveBetween(0m, 1m);
        });

        RuleForEach(c => c.PayeeSplits).ChildRules(split =>
        {
            split.RuleFor(s => s.PayeeId).NotEmpty();
            split.RuleFor(s => s.Percentage).GreaterThan(0m).LessThanOrEqualTo(1m);
        });

        RuleFor(c => c.PayeeSplits)
            .Must(splits => splits.Count == 0 || Math.Abs(splits.Sum(s => s.Percentage) - 1m) <= SplitTolerance)
            .WithMessage(c => string.Create(CultureInfo.InvariantCulture, $"Payee splits must sum to 1.0 (100%), got {c.PayeeSplits.Sum(s => s.Percentage):0.0000}"));

        RuleForEach(c => c.FeeMatrix).ChildRules(item =>
        {
            item.RuleFor(i => i.ItemCode).NotEmpty();
            item.RuleFor(i => i.BaseAmount).GreaterThanOrEqualTo(0m);
            item.RuleFor(i => i.MaxAllowableEvents).GreaterThanOrEqualTo(1).When(i => i.MaxAllowableEvents is not null);
        });
    }
}
