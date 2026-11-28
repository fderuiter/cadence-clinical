using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Tenancy;

/// <summary>Handles <see cref="ProvisionSubscription"/>.</summary>
internal sealed class ProvisionSubscriptionHandler : ICommandHandler<ProvisionSubscription, SubscriptionId>
{
    public async Task<CommandResult<SubscriptionId>> HandleAsync(
        ProvisionSubscription command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.SubscriptionId.IsEmpty)
        {
            return CommercialErrors.SubscriptionRequired;
        }

        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        if (command.Status == SubscriptionStatus.Trialing
            && (command.TrialEndsAt is not { } endsAt || KernelTimestamps.ToStoredPrecision(endsAt) <= context.RecordedAt))
        {
            return CommercialErrors.TrialWindowInvalid;
        }

        if (await context.Work.ExistsAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var existingForTenant = await context.Work
            .ReadAllAsync<Subscription>(s => s.TenantId == command.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (existingForTenant.Count > 0)
        {
            return CommercialErrors.SubscriptionAlreadyProvisioned;
        }

        var subscription = new Subscription(
            command.SubscriptionId,
            command.TenantId,
            command.Tier,
            command.Status,
            context.RecordedAt,
            command.TrialEndsAt,
            context.Actor.Id,
            command.ReasonForChange);
        context.Work.Add(subscription);
        context.Work.Raise(new SubscriptionProvisioned(
            command.TenantId,
            command.SubscriptionId,
            command.Tier,
            command.Status,
            subscription.TrialEndsAt));
        foreach (var entitlement in subscription.Entitlements)
        {
            if (entitlement.IsEnabled)
            {
                context.Work.Raise(new ProductEntitlementGranted(
                    command.TenantId,
                    entitlement.Id,
                    command.SubscriptionId,
                    entitlement.Family));
            }
        }

        return CommandResult<SubscriptionId>.Success(command.SubscriptionId);
    }
}

/// <summary>Handles <see cref="ExpireSubscription"/>.</summary>
internal sealed class ExpireSubscriptionHandler : ICommandHandler<ExpireSubscription, SubscriptionId>
{
    public async Task<CommandResult<SubscriptionId>> HandleAsync(
        ExpireSubscription command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.SubscriptionId.IsEmpty)
        {
            return CommercialErrors.SubscriptionRequired;
        }

        await context.Work.LockAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != command.TenantId)
        {
            return CommercialErrors.SubscriptionUnknown;
        }

        if (subscription.Status == SubscriptionStatus.Expired)
        {
            return CommandResult<SubscriptionId>.Success(subscription.Id);
        }

        subscription.Expire(context.RecordedAt, context.Actor.Id, command.ReasonForChange);
        context.Work.Raise(new SubscriptionExpired(command.TenantId, subscription.Id, context.RecordedAt));
        return CommandResult<SubscriptionId>.Success(subscription.Id);
    }
}

/// <summary>Handles <see cref="SwitchTrialTier"/>.</summary>
internal sealed class SwitchTrialTierHandler : ICommandHandler<SwitchTrialTier, SubscriptionId>
{
    public async Task<CommandResult<SubscriptionId>> HandleAsync(
        SwitchTrialTier command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.SubscriptionId.IsEmpty)
        {
            return CommercialErrors.SubscriptionRequired;
        }

        await context.Work.LockAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != command.TenantId)
        {
            return CommercialErrors.SubscriptionUnknown;
        }

        if (subscription.IsExpiredAt(context.RecordedAt))
        {
            return KernelErrors.SubscriptionExpired;
        }

        var diff = subscription.SwitchTier(command.Tier, context.RecordedAt, context.Actor.Id, command.ReasonForChange);
        context.Work.Raise(new SubscriptionTrialTierSwitched(
            command.TenantId,
            subscription.Id,
            command.Tier,
            subscription.Status,
            subscription.TrialEndsAt));
        CommercialEntitlementEvents.RaiseDiff(context.Work, command.TenantId, subscription.Id, diff);
        return CommandResult<SubscriptionId>.Success(subscription.Id);
    }
}

/// <summary>Handles <see cref="ActivateSubscription"/>.</summary>
internal sealed class ActivateSubscriptionHandler(CommercialQuoteOptions? quoteOptions = null)
    : ICommandHandler<ActivateSubscription, SubscriptionId>
{
    public async Task<CommandResult<SubscriptionId>> HandleAsync(
        ActivateSubscription command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.SubscriptionId.IsEmpty)
        {
            return CommercialErrors.SubscriptionRequired;
        }

        var validationErrors = SubscriptionScopingValidator.Validate(command.Scoping);
        if (validationErrors.Count > 0)
        {
            return CommercialErrors.ScopingInvalid with { Errors = validationErrors };
        }

        var normalizedScoping = SubscriptionScopingValidator.Normalize(command.Scoping!);
        if (context.Actor != CommercialOperators.BillingOperator
            && !SubscriptionQuoteToken.TryVerify(
                normalizedScoping.BillingConfirmation,
                quoteOptions?.SigningSecret,
                command.TenantId,
                command.Tier,
                normalizedScoping,
                context.RecordedAt,
                out _))
        {
            return CommercialQuoteRefusal.Create(
                NodeRef.For(command.SubscriptionId),
                nameof(ActivateSubscription),
                "Obtain a valid signed commercial quote token bound to this tenant, tier, and study scoping before activating the subscription.");
        }

        await context.Work.LockAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != command.TenantId)
        {
            return CommercialErrors.SubscriptionUnknown;
        }

        var diff = subscription.Activate(command.Tier, normalizedScoping, context.RecordedAt, context.Actor.Id, command.ReasonForChange);
        context.Work.Raise(new SubscriptionActivated(command.TenantId, subscription.Id, command.Tier, normalizedScoping));
        CommercialEntitlementEvents.RaiseDiff(context.Work, command.TenantId, subscription.Id, diff);
        return CommandResult<SubscriptionId>.Success(subscription.Id);
    }
}

/// <summary>Handles <see cref="GrantProductEntitlement"/>.</summary>
internal sealed class GrantProductEntitlementHandler(CommercialQuoteOptions? quoteOptions = null)
    : ICommandHandler<GrantProductEntitlement, ProductEntitlementId>
{
    public async Task<CommandResult<ProductEntitlementId>> HandleAsync(
        GrantProductEntitlement command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.ProductEntitlementId.IsEmpty)
        {
            return CommercialErrors.EntitlementRequired;
        }

        var (subscription, authError) = await CommercialEntitlementEvents.LoadAuthorizedSubscriptionAsync(
            context,
            command.TenantId,
            command.SubscriptionId,
            command.QuoteToken,
            quoteOptions,
            nameof(GrantProductEntitlement),
            "Direct product-entitlement overrides require CommercialOperators.BillingOperator or a verified commercial quote token.",
            cancellationToken).ConfigureAwait(false);
        if (authError is not null)
        {
            return authError;
        }

        var (entitlementId, granted) = subscription!.GrantFamily(
            command.ProductEntitlementId,
            command.Family,
            context.RecordedAt,
            context.Actor.Id,
            command.ReasonForChange);
        if (granted)
        {
            context.Work.Raise(new ProductEntitlementGranted(
                command.TenantId,
                entitlementId,
                command.SubscriptionId,
                command.Family));
        }

        return CommandResult<ProductEntitlementId>.Success(entitlementId);
    }
}

/// <summary>Handles <see cref="RevokeProductEntitlement"/>.</summary>
internal sealed class RevokeProductEntitlementHandler(CommercialQuoteOptions? quoteOptions = null)
    : ICommandHandler<RevokeProductEntitlement, SubscriptionId>
{
    public async Task<CommandResult<SubscriptionId>> HandleAsync(
        RevokeProductEntitlement command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        var (subscription, authError) = await CommercialEntitlementEvents.LoadAuthorizedSubscriptionAsync(
            context,
            command.TenantId,
            command.SubscriptionId,
            command.QuoteToken,
            quoteOptions,
            nameof(RevokeProductEntitlement),
            "Direct product-entitlement revocations require CommercialOperators.BillingOperator or a verified commercial quote token.",
            cancellationToken).ConfigureAwait(false);
        if (authError is not null)
        {
            return authError;
        }

        if (subscription!.RevokeFamily(command.Family, context.RecordedAt, context.Actor.Id, command.ReasonForChange) is { } revokedId)
        {
            context.Work.Raise(new ProductEntitlementRevoked(
                command.TenantId,
                revokedId,
                command.SubscriptionId,
                command.Family));
        }

        return CommandResult<SubscriptionId>.Success(command.SubscriptionId);
    }
}

internal static class CommercialEntitlementEvents
{
    public static async Task<(Subscription? Subscription, KernelError? Error)> LoadAuthorizedSubscriptionAsync(
        ICommandContext context,
        TenantId tenantId,
        SubscriptionId subscriptionId,
        string? quoteToken,
        CommercialQuoteOptions? quoteOptions,
        string commandName,
        string refusalReason,
        CancellationToken cancellationToken)
    {
        if (subscriptionId.IsEmpty)
        {
            return (null, CommercialErrors.SubscriptionRequired);
        }

        await context.Work.LockAsync<Subscription>(subscriptionId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<Subscription>(subscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != tenantId)
        {
            return (null, CommercialErrors.SubscriptionUnknown);
        }

        if (context.Actor != CommercialOperators.BillingOperator
            && !SubscriptionQuoteToken.TryVerify(
                quoteToken,
                quoteOptions?.SigningSecret,
                tenantId,
                subscription.Tier,
                subscription.GetScoping(),
                context.RecordedAt,
                out _))
        {
            return (null, CommercialQuoteRefusal.Create(
                NodeRef.For(subscriptionId),
                commandName,
                refusalReason));
        }

        return (subscription, null);
    }
    public static void RaiseDiff(
        IKernelUnitOfWork work,
        TenantId tenantId,
        SubscriptionId subscriptionId,
        SubscriptionEntitlementDiff diff)
    {
        foreach (var (id, family) in diff.Granted)
        {
            work.Raise(new ProductEntitlementGranted(tenantId, id, subscriptionId, family));
        }

        foreach (var (id, family) in diff.Revoked)
        {
            work.Raise(new ProductEntitlementRevoked(tenantId, id, subscriptionId, family));
        }
    }
}

internal static class CommercialQuoteRefusal
{
    public static KernelError Create(NodeRef subject, string commandName, string actionDescription)
    {
        var condition = EvaluatedCondition.Unsatisfied("commercial.quote_verified", actionDescription, subject);
        return CommercialErrors.QuoteInvalid with
        {
            Explanation = new Explanation(
                subject,
                $"{commandName} is refused because no verified cryptographic commercial quote or billing operator authority was presented.",
                "kernel.commercial",
                [new ExplanationStep(CommercialErrors.QuoteInvalid.Message, [subject])],
                [condition],
                [new NextAction(actionDescription, Target: subject)]),
        };
    }
}

internal static class CommercialServiceCollectionExtensions
{
    public static IServiceCollection AddKernelCommercialHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<ProvisionSubscription, SubscriptionId>, ProvisionSubscriptionHandler>();
        services.AddTransient<ICommandHandler<ExpireSubscription, SubscriptionId>, ExpireSubscriptionHandler>();
        services.AddTransient<ICommandHandler<SwitchTrialTier, SubscriptionId>, SwitchTrialTierHandler>();
        services.AddTransient<ICommandHandler<ActivateSubscription, SubscriptionId>, ActivateSubscriptionHandler>();
        services.AddTransient<ICommandHandler<GrantProductEntitlement, ProductEntitlementId>, GrantProductEntitlementHandler>();
        services.AddTransient<ICommandHandler<RevokeProductEntitlement, SubscriptionId>, RevokeProductEntitlementHandler>();
        return services;
    }
}
