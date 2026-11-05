using Inbox.Domain.Contacts;
using Inbox.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inbox.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages", table =>
        {
            table.HasCheckConstraint("ck_messages_direction", "direction IN ('inbound','outbound')");
            table.HasCheckConstraint("ck_messages_status", "status IN ('received','pending','sent','failed')");
        });

        builder.HasKey(m => m.Id).HasName("pk_messages");
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(m => m.Seq).HasColumnName("seq").ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
        builder.Property(m => m.Seq).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.HasIndex(m => m.Seq).IsUnique().HasDatabaseName("ux_messages_seq");

        builder.Property(m => m.ContactId).HasColumnName("contact_id").IsRequired();
        builder.HasOne<Contact>().WithMany().HasForeignKey(m => m.ContactId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_messages_contacts_contact_id");

        builder.Property(m => m.Direction).HasColumnName("direction").IsRequired()
            .HasConversion(new LowercaseEnumConverter<MessageDirection>());
        builder.Property(m => m.Text).HasColumnName("text").IsRequired()
            .HasConversion(text => text.Value, value => MessageText.Create(value));
        builder.Property(m => m.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(m => m.ReadAt).HasColumnName("read_at");
        builder.Property(m => m.Status).HasColumnName("status").IsRequired()
            .HasConversion(new LowercaseEnumConverter<DeliveryStatus>());
        builder.Property(m => m.FailureReason).HasColumnName("failure_reason");
        builder.Property(m => m.TelegramUpdateId).HasColumnName("telegram_update_id");
        builder.HasIndex(m => m.TelegramUpdateId).IsUnique().HasDatabaseName("ux_messages_telegram_update_id");
        builder.Property(m => m.TelegramMessageId).HasColumnName("telegram_message_id");
        builder.Property(m => m.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(m => m.LastQueuedAt).HasColumnName("last_queued_at");
        builder.Ignore(m => m.IsUnread);

        builder.HasIndex(m => m.ContactId).HasFilter("read_at IS NULL").HasDatabaseName("ix_messages_unread");
        builder.HasIndex(m => new { m.ContactId, m.OccurredAt, m.Seq })
            .IsDescending(false, true, true).HasDatabaseName("ix_messages_conversation");
        builder.HasIndex(m => m.LastQueuedAt).HasFilter("status = 'pending'").HasDatabaseName("ix_messages_pending");
    }
}
