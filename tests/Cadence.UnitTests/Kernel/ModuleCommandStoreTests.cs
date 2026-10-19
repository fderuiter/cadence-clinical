using System;
using System.Linq;

using Cadence.Kernel.Modules;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The module SDK's command store registration (ADR-2600): module names, one store per module and context, no
/// retrying execution strategy, and the Row-Level Security DDL a module migration applies.
/// </summary>
public sealed class ModuleCommandStoreTests
{
    /// <summary>Validates that a registered store reports its module and creates its context on the given connection.</summary>
    [Fact(DisplayName = "AddModuleCommandStore_CreatesContextOnTheCommandConnection")]
    public void AddModuleCommandStoreCreatesContextOnTheCommandConnection()
    {
        using var provider = new ServiceCollection()
            .AddModuleCommandStore<ProbeModuleContext>("probe_module", o => new ProbeModuleContext(o))
            .BuildServiceProvider();
        using var connection = new NpgsqlConnection("Host=localhost;Database=unused");

        var store = provider.GetServices<IModuleCommandStore>().Single();
        using var context = store.Create(connection);

        store.Module.Should().Be("probe_module");
        context.Should().BeOfType<ProbeModuleContext>();
        context.Database.GetDbConnection().Should().BeSameAs(connection);
        context.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeFalse();
    }

    /// <summary>Validates that a module name must be lower snake case, since it prefixes audit aggregate types.</summary>
    [Theory(DisplayName = "AddModuleCommandStore_RejectsInvalidModuleNames")]
    [InlineData("")]
    [InlineData("Execution")]
    [InlineData("1module")]
    [InlineData("exec.module")]
    [InlineData("exec-module")]
    public void AddModuleCommandStoreRejectsInvalidModuleNames(string module)
    {
        var act = () => new ServiceCollection().AddModuleCommandStore<ProbeModuleContext>(module, o => new ProbeModuleContext(o));

        act.Should().Throw<ArgumentException>().WithMessage("*module name*");
    }

    /// <summary>Validates that a module, and a context type, register at most one command store.</summary>
    [Fact(DisplayName = "AddModuleCommandStore_RejectsASecondStoreForTheSameModuleOrContext")]
    public void AddModuleCommandStoreRejectsASecondStoreForTheSameModuleOrContext()
    {
        var services = new ServiceCollection()
            .AddModuleCommandStore<ProbeModuleContext>("probe_module", o => new ProbeModuleContext(o));

        var sameModule = () => services.AddModuleCommandStore<OtherModuleContext>("probe_module", o => new OtherModuleContext(o));
        var sameContext = () => services.AddModuleCommandStore<ProbeModuleContext>("other_module", o => new ProbeModuleContext(o));

        sameModule.Should().Throw<InvalidOperationException>();
        sameContext.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Validates that a context with a retrying execution strategy is refused: a retry cannot replay a command's
    /// transaction, so the client retries the whole command with its idempotency key (K-TX-3).
    /// </summary>
    [Fact(DisplayName = "ModuleCommandStore_RefusesARetryingExecutionStrategy")]
    public void ModuleCommandStoreRefusesARetryingExecutionStrategy()
    {
        using var provider = new ServiceCollection()
            .AddModuleCommandStore<ProbeModuleContext>("probe_module", o => new ProbeModuleContext(o), npgsql => npgsql.EnableRetryOnFailure())
            .BuildServiceProvider();
        using var connection = new NpgsqlConnection("Host=localhost;Database=unused");

        var act = () => provider.GetServices<IModuleCommandStore>().Single().Create(connection);

        act.Should().Throw<InvalidOperationException>().WithMessage("*retrying execution strategy*ADR-2600*");
    }

    /// <summary>Validates that the module RLS DDL enables and forces RLS with the kernel's tenant predicate.</summary>
    [Fact(DisplayName = "ModuleTenantIsolationSql_EnablesAndForcesRls_OnTheKernelTenant")]
    public void ModuleTenantIsolationSqlEnablesAndForcesRlsOnTheKernelTenant()
    {
        var sql = ModuleTenantIsolationSql.EnableForTenantColumn("execution", "form_records");

        sql.Should().Contain("ALTER TABLE \"execution\".\"form_records\" ENABLE ROW LEVEL SECURITY;")
            .And.Contain("ALTER TABLE \"execution\".\"form_records\" FORCE ROW LEVEL SECURITY;")
            .And.Contain("USING (tenant_id = kernel.current_tenant_id())")
            .And.Contain("WITH CHECK (tenant_id = kernel.current_tenant_id())");
    }

    /// <summary>Validates that the RLS DDL accepts only lower snake case identifiers, so no name can alter the statement.</summary>
    [Theory(DisplayName = "ModuleTenantIsolationSql_RejectsUnsafeIdentifiers")]
    [InlineData("execution\"; DROP TABLE x; --", "notes")]
    [InlineData("execution", "Notes")]
    public void ModuleTenantIsolationSqlRejectsUnsafeIdentifiers(string schema, string table)
    {
        var act = () => ModuleTenantIsolationSql.EnableForTenantColumn(schema, table);

        act.Should().Throw<ArgumentException>();
    }

    private sealed class ProbeModuleContext(DbContextOptions<ProbeModuleContext> options) : ModuleDbContext(options);

    private sealed class OtherModuleContext(DbContextOptions<OtherModuleContext> options) : ModuleDbContext(options);
}
