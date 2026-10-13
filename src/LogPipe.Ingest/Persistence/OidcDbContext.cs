using Microsoft.EntityFrameworkCore;

namespace LogPipe.Ingest.Persistence;

public sealed class OidcDbContext(DbContextOptions<OidcDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict();
    }
}
