using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Modules;

/// <summary>
/// A module's <see cref="DbContext"/> for the records its commands write (ADR-2600). The kernel command pipeline creates
/// it on the command's connection, enlists it in the command's transaction and saves it; any other save of an audited
/// record (<see cref="IGxPAuditable"/>) throws, so every clinical change to a module record goes through
/// <see cref="Commands.IKernelCommandSender"/> and lands on the kernel audit chain (one history per record).
/// </summary>
/// <param name="options">Options built by the module's <see cref="IModuleCommandStore"/>.</param>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    private int _permittedWriters;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditedWritesPermitted();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAuditedWritesPermitted();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Opens the window in which the command pipeline saves this context; dispose to close it.</summary>
    internal IDisposable PermitCommandWrites()
    {
        Interlocked.Increment(ref _permittedWriters);
        return new WriteWindow(this);
    }

    private void EnsureAuditedWritesPermitted()
    {
        if (Volatile.Read(ref _permittedWriters) > 0)
        {
            return;
        }

        if (ChangeTracker.Entries<IGxPAuditable>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "ADR-2600: audited module records are written only through the kernel command pipeline (IKernelCommandSender).");
        }
    }

    private sealed class WriteWindow(ModuleDbContext context) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref context._permittedWriters);
            }
        }
    }
}
