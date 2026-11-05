using Inbox.Domain.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inbox.Infrastructure.Persistence.Configurations;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(c => c.Id).HasName("pk_contacts");
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.TelegramChatId).HasColumnName("telegram_chat_id").IsRequired();
        builder.HasIndex(c => c.TelegramChatId).IsUnique().HasDatabaseName("ux_contacts_telegram_chat_id");
        builder.Property(c => c.FirstName).HasColumnName("first_name").IsRequired();
        builder.Property(c => c.LastName).HasColumnName("last_name");
        builder.Property(c => c.Username).HasColumnName("username");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Ignore(c => c.DisplayName);
    }
}
