using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// App-level replacement for SQL Server's native <c>ROWVERSION</c> auto-increment-on-write behavior, which
/// neither Npgsql/PostgreSQL nor SQLite provide automatically for an arbitrary column (flagged as an open gap
/// in MIG-SUBJ-011's sign-off, closed here per MIG-SUBJ-013's own instructions).
///
/// <para>
/// <c>ClinicalSubjectConfiguration</c>, <c>SubjectConsentConfiguration</c>, and
/// <c>SiteComplianceCacheConfiguration</c> each configure a shadow <c>_version</c> <see cref="long"/> property
/// as <c>.IsConcurrencyToken()</c> — deliberately <b>not</b> <c>.IsRowVersion()</c>, which is shorthand for
/// <c>.IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()</c>. <c>ValueGeneratedOnAddOrUpdate</c> tells EF Core
/// the *database* produces the value on every write, insert or update (a computed column, a trigger, SQL
/// Server's native rowversion): EF never includes such a property in the generated UPDATE column list at all,
/// relying entirely on a post-command read-back to populate the tracked value — so on a provider with no
/// store-side mechanism to actually generate a new value on UPDATE, any value this interceptor assigned before
/// <c>SaveChangesAsync</c> would be silently discarded (the read-back would just observe the unchanged
/// column). Leaving off <c>.ValueGeneratedOnAddOrUpdate()</c> keeps the property's <c>ValueGenerated</c> at
/// whatever EF's own convention infers for a property that only carries <c>.HasDefaultValue(0L)</c> — this
/// evaluates to <c>ValueGenerated.OnAdd</c> (a database default fills a brand-new row when the column is
/// omitted from the INSERT, exactly the starting-value behavior MIG-SUBJ-011's fix already relies on), which
/// — unlike <c>OnAddOrUpdate</c> — has no effect on UPDATE statements at all: EF always sends this property's
/// value explicitly on UPDATE, both in the SET clause (the new value) and, because it is a concurrency token,
/// the WHERE clause (the expected original value) — which is what actually makes this interceptor's
/// assignment take effect and lets a stale write be detected as a genuine
/// <see cref="DbUpdateConcurrencyException"/>.
/// </para>
///
/// <para>
/// On every <c>SaveChangesAsync</c> call, for each tracked entry in the <see cref="EntityState.Modified"/>
/// state carrying a property flagged <c>IsConcurrencyToken</c> of CLR type <see cref="long"/> — deliberately
/// excluding <c>SiteComplianceCache.Version</c>, which is
/// domain data and is explicitly configured <c>IsConcurrencyToken(false)</c>, see MIG-SUBJ-012 — this sets
/// that property's current value to <i>one more than its own current original value</i> (not one more than
/// whatever its current value happens to be). Computing from <c>OriginalValue</c> rather than
/// <c>CurrentValue</c> makes this idempotent/safe to re-run: if a
/// <see cref="Repositories.SaveChangesResiliencePipelineFactory"/> retry reloads an entry's original values
/// from the database between attempts (the documented "client wins" conflict-resolution reload), the next
/// <c>SavingChangesAsync</c> pass recomputes the token relative to that freshly-reloaded baseline rather than
/// compounding on top of a value this interceptor already wrote for a failed attempt.
/// </para>
///
/// <para>
/// <see cref="EntityState.Added"/> entries are left untouched — each configuration's own
/// <c>.HasDefaultValue(0L)</c> already supplies the starting value for a brand-new row (the INSERT-side half
/// of MIG-SUBJ-011's original gap, which that ticket already closed).
/// </para>
///
/// <para>
/// Only <see cref="SavingChangesAsync"/> is overridden, matching this ticket's own "scoped to
/// <c>SaveChangesAsync</c>" wording and <c>GxPAuditInterceptor</c>'s own established async-only convention —
/// every call site in this slice saves asynchronously.
/// </para>
///
/// <para>
/// Registered the same way <c>GxPAuditInterceptor</c> currently is: not inside <c>ExecutionDbContext</c>
/// itself (its own doc comment explains why — the production composition root for this context's
/// <c>DbContextOptions</c> is a later ticket's <c>AddDbContext</c> call), but directly on the
/// <see cref="Microsoft.EntityFrameworkCore.DbContextOptionsBuilder{TContext}"/> at whichever call site builds
/// it — tests do this explicitly (matching <c>GxPAuditInterceptorMerkleChainTests</c>' convention), and the
/// production registration remains open for whichever ticket owns <c>Program.cs</c>'s eventual
/// <c>AddDbContext&lt;ExecutionDbContext&gt;</c> call (MIG-SUBJ-024 per the current backlog) — flagged forward
/// here so that ticket registers both interceptors together.
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public sealed class ConcurrencyTokenIncrementInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        IncrementConcurrencyTokens(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void IncrementConcurrencyTokens(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            foreach (var property in entry.Metadata.GetProperties())
            {
                if (!property.IsConcurrencyToken || property.ClrType != typeof(long))
                {
                    continue;
                }

                var propertyEntry = entry.Property(property.Name);
                long baseline = (long)(propertyEntry.OriginalValue ?? 0L);
                propertyEntry.CurrentValue = baseline + 1;
            }
        }
    }
}
