using Inbox.Domain.Contacts;
using Inbox.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Inbox.Infrastructure.Persistence;

public sealed class InboxDbContext(DbContextOptions<InboxDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InboxDbContext).Assembly);
    }
}
