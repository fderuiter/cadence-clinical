using Cadence.Apps.Tickets.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Tickets.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Reference).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Reference).IsUnique();
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reporter).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AssigneeUser).HasMaxLength(255);
        builder.Property(x => x.AssigneeRole).HasMaxLength(255);
        builder.Property(x => x.OrgId).HasMaxLength(255);
        builder.HasIndex(x => x.OrgId);
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.StudyId).HasMaxLength(255);
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.RelatedEntityType).HasMaxLength(100);
        builder.Property(x => x.RelatedEntityId).HasMaxLength(255);
        builder.Property(x => x.GxpSeverity).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RootCauseCategory).HasMaxLength(50);
        builder.Property(x => x.RootCauseSummary).HasMaxLength(1000);
        builder.Property(x => x.ResolutionCode).HasMaxLength(50);
        builder.Property(x => x.WorkflowType).HasMaxLength(100);
        builder.Property(x => x.ActionType).HasMaxLength(100);
        builder.Property(x => x.SignatureAction).HasMaxLength(255);
        builder.Property(x => x.SignatureToken).HasMaxLength(512);
        builder.Property(x => x.SignatureMeaning).HasMaxLength(255);
        builder.Property(x => x.SignatureUser).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TicketId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.TicketId);
        builder.Property(x => x.Visibility).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("ticket_attachments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TicketId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.TicketId);
        builder.Property(x => x.Filename).HasMaxLength(255).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StorageUri).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Sha256Hash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UploadedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class TicketAuditLogConfiguration : IEntityTypeConfiguration<TicketAuditLog>
{
    public void Configure(EntityTypeBuilder<TicketAuditLog> builder)
    {
        builder.ToTable("ticket_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TicketId).HasMaxLength(36);
        builder.HasIndex(x => x.TicketId);
        builder.HasIndex(x => x.CreatedAt);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CreatedBy);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RecordId).HasMaxLength(255);
    }
}
