using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shared.Data;

public class WriteDbContextFactory : IDesignTimeDbContextFactory<WriteDbContext>
{
    public WriteDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("OMNIAI_WRITE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=omniai;Username=omniai;Password=omniai_dev_password";

        var optionsBuilder = new DbContextOptionsBuilder<WriteDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new WriteDbContext(optionsBuilder.Options);
    }
}
