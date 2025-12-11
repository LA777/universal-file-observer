using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Ufo.Database.Contexts;

/// <summary>
/// Factory for creating UfoDbContext instances for migrations.
/// Required by EF Core CLI commands like dotnet ef migrations add
/// </summary>
public class UfoDbContextFactory : IDesignTimeDbContextFactory<UfoDbContext>
{
    public UfoDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<UfoDbContext>();
        
        // Load configuration from appsettings files
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Ufo.Server"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true);
        
        var config = configBuilder.Build();
        
        // Get connection string from configuration, with fallback options
        var connectionString = config.GetConnectionString("DefaultConnection");
        
        // Fallback to ApplicationSettings section if available
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = config["ApplicationSettings:SqliteDbConnectionStrings"];
        }
        
        // Final fallback to environment variable or default
        connectionString ??= Environment.GetEnvironmentVariable("UFO_CONNECTION_STRING");
        connectionString ??= "Data Source=snapshots.db;Cache=Shared";
        
        optionsBuilder.UseSqlite(connectionString);
        
        return new UfoDbContext(optionsBuilder.Options);
    }
}
