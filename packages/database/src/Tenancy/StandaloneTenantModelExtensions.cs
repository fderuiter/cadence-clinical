using System;
using System.Collections.Generic;
using System.Linq.Expressions;

using Cadence.Kernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Implemented by standalone clinical <see cref="DbContext"/> types to expose dynamic tenant predicates for
/// in-memory test hosts when PostgreSQL Row-Level Security is absent.
/// </summary>
public interface ITenantScopedDbContext
{
    Guid? ActiveInMemoryTenantFilter { get; }

    string? ActiveInMemoryTenantStringFilter { get; }
}

public static class StandaloneTenantModelExtensions
{
    public const string TenantPropertyName = "TenantId";
    public const string TenantColumnName = "tenant_id";

    public static readonly Guid UnboundTenantSentinel = new("ffffffff-ffff-ffff-ffff-ffffffffffff");
    public static readonly string EmptyGuidString = Guid.Empty.ToString("D");

    /// <summary>
    /// Infrastructure tables that do not carry a tenant column (single-use signature tokens and MassTransit outbox bookkeeping).
    /// </summary>
    public static readonly IReadOnlySet<string> NonTenantTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "consumed_signature_tokens",
        "inbox_state",
        "outbox_message",
        "outbox_state",
        "InboxState",
        "OutboxMessage",
        "OutboxState",
    };

    private static readonly ValueConverter<string, Guid> StringToGuidTenantConverter = new(
        value => ParseOrEmpty(value),
        guid => guid.ToString("D"));

    private static readonly ValueConverter<TenantId, Guid> KernelTenantIdConverter = new(
        tenantId => tenantId.Value,
        guid => guid == Guid.Empty ? default : TenantId.FromGuid(guid));

    /// <summary>
    /// Identifies append-only/insert-only audit, outbox, transition, and historical revision entities that are never
    /// updated in place and therefore do not carry mutable row optimistic-concurrency tokens.
    /// </summary>
    public static bool IsImmutableStandaloneHistoryEntity(IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        string clrName = entityType.ClrType.Name;
        return clrName is "AuditLogEntry" or "TransactionalOutboxMessage" or "TmfAuditLedgerSeal" or "TmfDocumentQcTransition"
            || clrName.EndsWith("AuditLog", StringComparison.Ordinal)
            || clrName.EndsWith("Revision", StringComparison.Ordinal);
    }

    public static Guid? ResolveInMemoryTenantFilter(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ext = context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext is null || ext.IsRelational || ext.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return null;
        }

        if (ext.TenantAccessor.GetCurrentTenantId() is { } activeTenant)
        {
            return activeTenant;
        }

        if (ext.TenantAccessor.HasHttpRequestWithoutTenant)
        {
            return UnboundTenantSentinel;
        }

        return null;
    }

    public static string? ResolveInMemoryTenantStringFilter(DbContext context) =>
        ResolveInMemoryTenantFilter(context)?.ToString("D");

    /// <summary>
    /// Configures <c>tenant_id uuid NOT NULL</c>, index, and in-memory tenant query predicates on every
    /// tenant-scoped root entity in <paramref name="modelBuilder"/>.
    /// </summary>
    public static void ConfigureStandaloneTenancy<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext, ITenantScopedDbContext
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        var rootEntityTypes = new List<IMutableEntityType>();
        foreach (IMutableEntityType candidate in modelBuilder.Model.GetEntityTypes())
        {
            if (candidate.IsOwned()
                || candidate.BaseType is not null
                || candidate.HasSharedClrType
                || candidate.FindPrimaryKey() is null
                || candidate.GetViewName() is not null
                || candidate.ClrType == typeof(Dictionary<string, object>))
            {
                continue;
            }

            string? tableName = candidate.GetTableName();
            if (tableName is not null && NonTenantTableNames.Contains(tableName))
            {
                continue;
            }

            rootEntityTypes.Add(candidate);
        }

        foreach (IMutableEntityType entityType in rootEntityTypes)
        {
            ConfigureEntityMethod
                .MakeGenericMethod(entityType.ClrType, typeof(TContext))
                .Invoke(null, [modelBuilder, entityType, context]);
        }
    }

    private static readonly System.Reflection.MethodInfo ConfigureEntityMethod =
        typeof(StandaloneTenantModelExtensions).GetMethod(
            nameof(ConfigureEntity),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;

    public static void ConfigureEntity<TEntity, TContext>(
        ModelBuilder modelBuilder,
        IMutableEntityType entityType,
        TContext context)
        where TEntity : class
        where TContext : DbContext, ITenantScopedDbContext
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(context);
        var entityBuilder = modelBuilder.Entity<TEntity>();
        IMutableProperty? existingProp = entityType.FindProperty(TenantPropertyName);
        Type? clrTenantType = existingProp?.ClrType
            ?? entityType.ClrType.GetProperty(
                TenantPropertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.PropertyType;

        if (clrTenantType == typeof(string))
        {
            existingProp?.SetMaxLength(null);
            entityBuilder.Property<string>(TenantPropertyName)
                .HasColumnName(TenantColumnName)
                .HasColumnType("uuid")
                .HasConversion(StringToGuidTenantConverter)
                .IsRequired();
            entityBuilder.HasIndex(TenantPropertyName);

            if (!context.Database.IsRelational())
            {
                ApplyStringTenantQueryFilter(entityBuilder, entityType, context);
            }
        }
        else if (clrTenantType == typeof(TenantId))
        {
            entityBuilder.Property<TenantId>(TenantPropertyName)
                .HasColumnName(TenantColumnName)
                .HasColumnType("uuid")
                .HasConversion(KernelTenantIdConverter)
                .IsRequired();
            entityBuilder.HasIndex(TenantPropertyName);

            if (!context.Database.IsRelational())
            {
                ApplyKernelTenantIdQueryFilter(entityBuilder, entityType, context);
            }
        }
        else
        {
            entityBuilder.Property<Guid>(TenantPropertyName)
                .HasColumnName(TenantColumnName)
                .HasColumnType("uuid")
                .IsRequired();
            entityBuilder.HasIndex(TenantPropertyName);

            if (!context.Database.IsRelational())
            {
                ApplyGuidTenantQueryFilter(entityBuilder, entityType, context);
            }
        }

        ConfigureConcurrencyToken(entityBuilder, entityType);
    }

    private static void ConfigureConcurrencyToken<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entityBuilder,
        IMutableEntityType entityType)
        where TEntity : class
    {
        foreach (IMutableProperty existing in entityType.GetProperties())
        {
            if (existing.IsConcurrencyToken)
            {
                return;
            }
        }

        IMutableProperty? versionIndex = entityType.FindProperty(Concurrency.StandaloneConcurrencyTokenInterceptor.VersionIndexPropertyName);
        if (versionIndex is not null && (versionIndex.ClrType == typeof(int) || versionIndex.ClrType == typeof(long)))
        {
            versionIndex.IsConcurrencyToken = true;
            return;
        }

        entityBuilder.Property<long>(Concurrency.StandaloneConcurrencyTokenInterceptor.ShadowVersionPropertyName)
            .IsConcurrencyToken()
            .HasDefaultValue(0L);
    }

    private static void ApplyGuidTenantQueryFilter<TContext>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder entityBuilder,
        IMutableEntityType entityType,
        TContext context)
        where TContext : DbContext, ITenantScopedDbContext
    {
        var parameter = Expression.Parameter(entityType.ClrType, "e");
        var contextConst = Expression.Constant(context, typeof(TContext));
        var activeFilter = Expression.Property(contextConst, nameof(ITenantScopedDbContext.ActiveInMemoryTenantFilter));
        var filterIsNull = Expression.Equal(activeFilter, Expression.Constant(null, typeof(Guid?)));

        var efPropertyCall = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(Guid)],
            parameter,
            Expression.Constant(TenantPropertyName));

        var matchesActive = Expression.Equal(
            Expression.Convert(efPropertyCall, typeof(Guid?)),
            activeFilter);
        var notUnbound = Expression.NotEqual(
            activeFilter,
            Expression.Constant((Guid?)UnboundTenantSentinel, typeof(Guid?)));
        var matchesSeed = Expression.AndAlso(
            notUnbound,
            Expression.Equal(efPropertyCall, Expression.Constant(Guid.Empty)));

        Expression predicate = Expression.OrElse(filterIsNull, Expression.OrElse(matchesActive, matchesSeed));
        CombineAndSetQueryFilter(entityBuilder, entityType, parameter, predicate);
    }

    private static void ApplyKernelTenantIdQueryFilter<TContext>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder entityBuilder,
        IMutableEntityType entityType,
        TContext context)
        where TContext : DbContext, ITenantScopedDbContext
    {
        var parameter = Expression.Parameter(entityType.ClrType, "e");
        var contextConst = Expression.Constant(context, typeof(TContext));
        var activeFilter = Expression.Property(contextConst, nameof(ITenantScopedDbContext.ActiveInMemoryTenantFilter));
        var filterIsNull = Expression.Equal(activeFilter, Expression.Constant(null, typeof(Guid?)));

        var efPropertyCall = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(TenantId)],
            parameter,
            Expression.Constant(TenantPropertyName));
        var valueProp = Expression.Property(efPropertyCall, nameof(TenantId.Value));

        var matchesActive = Expression.Equal(
            Expression.Convert(valueProp, typeof(Guid?)),
            activeFilter);
        var notUnbound = Expression.NotEqual(
            activeFilter,
            Expression.Constant((Guid?)UnboundTenantSentinel, typeof(Guid?)));
        var matchesSeed = Expression.AndAlso(
            notUnbound,
            Expression.Equal(valueProp, Expression.Constant(Guid.Empty)));

        Expression predicate = Expression.OrElse(filterIsNull, Expression.OrElse(matchesActive, matchesSeed));
        CombineAndSetQueryFilter(entityBuilder, entityType, parameter, predicate);
    }

    private static void ApplyStringTenantQueryFilter<TContext>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder entityBuilder,
        IMutableEntityType entityType,
        TContext context)
        where TContext : DbContext, ITenantScopedDbContext
    {
        var parameter = Expression.Parameter(entityType.ClrType, "e");
        var contextConst = Expression.Constant(context, typeof(TContext));
        var activeFilter = Expression.Property(contextConst, nameof(ITenantScopedDbContext.ActiveInMemoryTenantStringFilter));
        var filterIsNull = Expression.Equal(activeFilter, Expression.Constant(null, typeof(string)));

        var efPropertyCall = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(string)],
            parameter,
            Expression.Constant(TenantPropertyName));

        var matchesActive = Expression.Equal(efPropertyCall, activeFilter);
        var notUnbound = Expression.NotEqual(
            activeFilter,
            Expression.Constant(UnboundTenantSentinel.ToString("D"), typeof(string)));
        var matchesEmptyGuid = Expression.Equal(efPropertyCall, Expression.Constant(EmptyGuidString));
        var matchesBlank = Expression.Equal(efPropertyCall, Expression.Constant(string.Empty));
        var matchesSeed = Expression.AndAlso(notUnbound, Expression.OrElse(matchesEmptyGuid, matchesBlank));

        Expression predicate = Expression.OrElse(
            filterIsNull,
            Expression.OrElse(matchesActive, matchesSeed));
        CombineAndSetQueryFilter(entityBuilder, entityType, parameter, predicate);
    }

    private static void CombineAndSetQueryFilter(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder entityBuilder,
        IMutableEntityType entityType,
        ParameterExpression parameter,
        Expression tenantPredicate)
    {
#pragma warning disable CS0618 // EF Core 10 marks unnamed GetQueryFilter() obsolete in favor of named filters; modules register a single unnamed filter.
        LambdaExpression? existing = entityType.GetQueryFilter();
#pragma warning restore CS0618
        if (existing is not null)
        {
            Expression rewritten = ReplacingExpressionVisitor.Replace(existing.Parameters[0], parameter, existing.Body);
            tenantPredicate = Expression.AndAlso(rewritten, tenantPredicate);
        }

        entityBuilder.HasQueryFilter(Expression.Lambda(tenantPredicate, parameter));
    }

    public static Guid ParseOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Guid.Empty;
        }

        return Guid.TryParse(value.Trim(), out Guid parsed)
            ? parsed
            : DeterministicTenantCodeResolver.ComputeDeterministicGuid(value);
    }
}
