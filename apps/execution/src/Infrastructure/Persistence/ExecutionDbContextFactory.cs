using System;

using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct an <see cref="ExecutionDbContext"/>
/// without spinning up the ASP.NET Core host. <c>Program.cs</c> does not call
/// <c>services.AddDbContext&lt;ExecutionDbContext&gt;(...)</c> yet (that DI wiring is a later ticket's
/// scope — see <see cref="ExecutionDbContext"/>'s own type-level remarks), so EF's usual "build the app's
/// host and resolve the context from its service provider" discovery path has no registration to find.
/// <see cref="IDesignTimeDbContextFactory{TContext}"/> is the standard, documented fallback for exactly
/// this situation (see
/// <see href="https://learn.microsoft.com/ef/core/cli/dbcontext-creation#from-a-design-time-factory"/>):
/// EF Core tooling always prefers an implementation of this interface over every other discovery
/// mechanism, so this type only needs to exist in the project for `dotnet ef` to pick it up — no wiring,
/// no `--` args, nothing else to configure.
///
/// <para>
/// This factory is never referenced by the running application (<c>Program.cs</c> is untouched by this
/// ticket, deliberately — see the implementing ticket's report for why) — it is loaded solely by the
/// design-time tooling process. The connection string below therefore only has to be one Npgsql accepts
/// while it builds the model to diff migrations against; <c>migrations add</c> never opens it. It matches
/// this repo's own local/dev Postgres container (<c>docker/docker-compose.yml</c>'s <c>postgres</c>
/// service: user/db <c>cadence</c>/<c>cadence_edc</c>) so that a real <c>dotnet ef database update</c>
/// against that container works out of the box too; <c>POSTGRES_CONNECTION_STRING</c> (the same variable
/// name the compose file's <c>dotnet-dev</c> service already sets) overrides it for any other target.
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public sealed class ExecutionDbContextFactory : IDesignTimeDbContextFactory<ExecutionDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "edc";

    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=cadence_password";

    public ExecutionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new ExecutionDbContext(optionsBuilder.Options);
    }
}
