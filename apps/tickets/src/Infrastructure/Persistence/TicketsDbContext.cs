using System;

using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Tickets.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Tickets microservice persistence gateway.
/// </summary>
public sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketRevision> TicketRevisions => Set<TicketRevision>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<TicketAuditLog> TicketAuditLogs => Set<TicketAuditLog>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TicketConfiguration());
        modelBuilder.ApplyConfiguration(new TicketRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new TicketCommentConfiguration());
        modelBuilder.ApplyConfiguration(new TicketAttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new TicketAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
