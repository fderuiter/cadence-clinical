using Cadence.Database;

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="KnowledgeDbContext"/>.
/// </summary>
public sealed class KnowledgeDbContextFactory()
    : CadenceDesignTimeDbContextFactory<KnowledgeDbContext>(options => new KnowledgeDbContext(options))
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "knowledge";
}
