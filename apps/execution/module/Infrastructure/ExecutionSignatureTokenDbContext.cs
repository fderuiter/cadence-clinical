using System;

using Cadence.Database.Signatures;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// The used e-signature step-up tokens of the kernel host's signature routes (ADR-2515, #5463): the table
/// <c>execution.consumed_signature_tokens</c>, which <see cref="ExecutionModuleDbContext"/>'s migrations create. Each
/// use is one insert whose primary key lets exactly one request spend a token, across every instance of the host and
/// across restarts. It holds no tenant data and no audited record, so it is a plain context outside the command pipeline.
/// </summary>
/// <param name="options">Options on the host's application-role connection.</param>
public sealed class ExecutionSignatureTokenDbContext(DbContextOptions<ExecutionSignatureTokenDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(ExecutionModuleDbContext.Schema);
        modelBuilder.ApplyConfiguration(new ConsumedSignatureTokenConfiguration());
    }
}
