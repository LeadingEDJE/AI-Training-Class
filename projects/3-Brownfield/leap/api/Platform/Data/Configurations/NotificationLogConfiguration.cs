using LeadingEDJE.Leap.Api.Platform.Domain;
using LeadingEDJE.Leap.Api.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadingEDJE.Leap.Api.Platform.Data.Configurations;

/// <summary>EF Core configuration for the NotificationLog entity (Slack and email delivery audit trail).</summary>
public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    /// <summary>Applies the entity configuration to the model builder.</summary>
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedOnAdd();

        builder.Property(n => n.EmployeeId).IsRequired().HasMaxLength(100);
        builder.Property(n => n.NotificationType).IsRequired().HasMaxLength(50);
        builder.Property(n => n.Channel).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Status).IsRequired().HasMaxLength(20);
        builder.Property(n => n.RecipientEmail).HasMaxLength(255);
        builder.Property(n => n.SlackUserId).HasMaxLength(50);
        // The bound is NotificationErrorMessage's, not a literal, because every writer of this column has
        // to truncate to the same number: a longer value raises 22001 at save time, and in the retry job
        // that aborts the whole pass with retry_count still 0 (#591's duplicate storm, re-entered).
        builder.Property(n => n.ErrorMessage).HasMaxLength(NotificationErrorMessage.MaxLength);

        // Captured message, so a retry re-sends the original rather than a placeholder (#532) from the
        // original sender (#531). Subject and Body are left unbounded (Npgsql `text`): a persisted subject
        // must never fail an insert on length, and the Pending-log SaveChanges runs before delivery, so an
        // overflow there would fail the triggering action. Address/name are genuinely bounded like their
        // RecipientEmail sibling. All nullable — legacy rows carry none.
        builder.Property(n => n.Subject);
        builder.Property(n => n.Body);
        builder.Property(n => n.FromAddress).HasMaxLength(255);
        builder.Property(n => n.FromName).HasMaxLength(255);

        builder.HasIndex(n => new { n.EmployeeId, n.NotificationType, n.PeriodWeekStart })
            .IsUnique()
            .HasDatabaseName("ix_notification_log_idempotency");
    }
}
