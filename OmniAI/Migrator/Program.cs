using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shared.Data;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("WriteDatabase")
    ?? throw new InvalidOperationException("ConnectionStrings:WriteDatabase nao configurada.");

var optionsBuilder = new DbContextOptionsBuilder<WriteDbContext>();
optionsBuilder.UseNpgsql(connectionString);

using var context = new WriteDbContext(optionsBuilder.Options);

Console.WriteLine("Aplicando migrations no banco de escrita...");
await context.Database.MigrateAsync();
Console.WriteLine("Migrations aplicadas com sucesso.");
