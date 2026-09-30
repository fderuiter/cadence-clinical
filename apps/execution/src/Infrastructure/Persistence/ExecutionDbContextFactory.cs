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
/// design-time tooling process. The connection string is read from <c>POSTGRES_CONNECTION_STRING</c> (the
/// same variable name the compose file's <c>dotnet-dev</c> service already sets) via
/// <see cref="Cadence.Database.CadenceDatabaseExtensions.ResolveDesignTimeConnectionString"/>; there is no
/// built-in fallback credential, so running <c>dotnet ef</c> without it fails with a message naming the
/// variable (ADR-2260).
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public sealed class ExecutionDbContextFactory : IDesignTimeDbContextFactory<ExecutionDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "edc";

    public ExecutionDbContext CreateDbContext(string[] args)
    {
        var connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new ExecutionDbContext(optionsBuilder.Options);
    }
}
