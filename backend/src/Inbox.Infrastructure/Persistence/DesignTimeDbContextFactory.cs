using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inbox.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to build the model; no database connection is opened.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InboxDbContext>
{
    public InboxDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InboxDbContext>()
            .UseNpgsql("Host=localhost;Database=inbox_design_time")
            .Options;
        return new InboxDbContext(options);
    }
}
