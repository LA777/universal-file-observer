using Microsoft.Extensions.DependencyInjection;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.Database.Extensions;

public static class DependencyExtension
{
    public static async Task AddDataLayerAsync(IServiceCollection services, string? connectionString)
    {
        services.AddScoped<IFileSystemRepository, FileSystemRepository>();

        await DapperDataContext.InitiateDatabaseAsync(connectionString);
    }
}
