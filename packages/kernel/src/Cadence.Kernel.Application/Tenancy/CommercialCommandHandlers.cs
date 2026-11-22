using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;
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

        if (await context.Work.FindAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != command.TenantId)
        {
            return CommercialErrors.SubscriptionUnknown;
        }

        if (subscription.IsExpiredAt(context.RecordedAt))
        {
            return KernelErrors.SubscriptionExpired;
        }

        subscription.SwitchTierInPlace(command.Tier, context.Actor.Id, command.ReasonForChange);
        context.Work.Raise(new SubscriptionTrialTierSwitched(
            command.TenantId,
            subscription.Id,
            command.Tier,
            subscription.Status,
            subscription.TrialEndsAt));
        return CommandResult<SubscriptionId>.Success(subscription.Id);
    }
}

/// <summary>Handles <see cref="ActivateSubscription"/>.</summary>
internal sealed class ActivateSubscriptionHandler : ICommandHandler<ActivateSubscription, SubscriptionId>
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

        if (await context.Work.FindAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != command.TenantId)
        {
            return CommercialErrors.SubscriptionUnknown;
        }

        var normalizedScoping = SubscriptionScopingValidator.Normalize(command.Scoping!);
        subscription.Activate(command.Tier, normalizedScoping, context.RecordedAt, context.Actor.Id, command.ReasonForChange);
        context.Work.Raise(new SubscriptionActivated(command.TenantId, subscription.Id, command.Tier, normalizedScoping));
        return CommandResult<SubscriptionId>.Success(subscription.Id);
    }
}

/// <summary>Handles <see cref="GrantProductEntitlement"/>.</summary>
internal sealed class GrantProductEntitlementHandler : ICommandHandler<GrantProductEntitlement, ProductEntitlementId>
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

        if (command.SubscriptionId.IsEmpty)
        {
            return CommercialErrors.SubscriptionRequired;
        }

        if (await context.Work.ReadAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != command.TenantId)
        {
            return CommercialErrors.SubscriptionUnknown;
        }

        var existingFamily = await context.Work
            .ReadAllAsync<ProductEntitlement>(
                e => e.TenantId == command.TenantId && e.Family == command.Family,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingFamily.Count > 0)
        {
            var existing = existingFamily[0];
            if (existing.IsEnabled)
            {
                return CommandResult<ProductEntitlementId>.Success(existing.Id);
            }

            var tracked = await context.Work.FindAsync<ProductEntitlement>(existing.Id, cancellationToken).ConfigureAwait(false);
            if (tracked is null)
            {
                return CommercialErrors.EntitlementRequired;
            }

            tracked.Enable(command.SubscriptionId, context.RecordedAt, context.Actor.Id, command.ReasonForChange);
            context.Work.Raise(new ProductEntitlementGranted(
                command.TenantId,
                tracked.Id,
                command.SubscriptionId,
                command.Family));
            return CommandResult<ProductEntitlementId>.Success(tracked.Id);
        }

        if (await context.Work.ExistsAsync<ProductEntitlement>(command.ProductEntitlementId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var entitlement = new ProductEntitlement(
            command.ProductEntitlementId,
            command.TenantId,
            command.SubscriptionId,
            command.Family,
            context.RecordedAt,
            context.Actor.Id,
            command.ReasonForChange);
        context.Work.Add(entitlement);
        context.Work.Raise(new ProductEntitlementGranted(
            command.TenantId,
            command.ProductEntitlementId,
            command.SubscriptionId,
            command.Family));
        return CommandResult<ProductEntitlementId>.Success(command.ProductEntitlementId);
    }
}

/// <summary>Handles <see cref="RevokeProductEntitlement"/>.</summary>
internal sealed class RevokeProductEntitlementHandler : ICommandHandler<RevokeProductEntitlement, SubscriptionId>
{
    public async Task<CommandResult<SubscriptionId>> HandleAsync(
        RevokeProductEntitlement command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.SubscriptionId.IsEmpty)
        {
            return CommercialErrors.SubscriptionRequired;
        }

        if (await context.Work.ReadAsync<Subscription>(command.SubscriptionId, cancellationToken).ConfigureAwait(false) is not { } subscription
            || subscription.TenantId != command.TenantId)
        {
            return CommercialErrors.SubscriptionUnknown;
        }

        var existingFamily = await context.Work
            .ReadAllAsync<ProductEntitlement>(
                e => e.TenantId == command.TenantId
                    && e.SubscriptionId == command.SubscriptionId
                    && e.Family == command.Family,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingFamily.Count == 0 || !existingFamily[0].IsEnabled)
        {
            return CommandResult<SubscriptionId>.Success(command.SubscriptionId);
        }

        if (await context.Work.FindAsync<ProductEntitlement>(existingFamily[0].Id, cancellationToken).ConfigureAwait(false) is { } tracked)
        {
            tracked.Disable(context.Actor.Id, command.ReasonForChange);
            context.Work.Raise(new ProductEntitlementRevoked(
                command.TenantId,
                tracked.Id,
                command.SubscriptionId,
                command.Family));
        }

        return CommandResult<SubscriptionId>.Success(command.SubscriptionId);
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
