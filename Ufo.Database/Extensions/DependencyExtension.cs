//using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;

namespace Ufo.Database.Extensions;

public static class DependencyExtension
{
    public static async Task AddDataLayerAsync(IServiceCollection services, string? connectionString)
    {
        // Register EF Core DbContext with SQLite
        services.AddDbContext<UfoDbContext>(options =>
            options.UseSqlite(connectionString)
        );

        services.AddScoped<IFileSystemRepository, FileSystemEfCoreRepository>();

        //SqlMapper.AddTypeHandler(new SqlUlidTypeHandler());
        //SqlMapper.AddTypeHandler(new SqlNullableUlidTypeHandler());
        //SqlMapper.AddTypeHandler(new SqlGuidTypeHandler());
        //SqlMapper.AddTypeHandler(new SqlNullableGuidTypeHandler());
        //SqlMapper.RemoveTypeMap(typeof(Guid));
        //SqlMapper.RemoveTypeMap(typeof(Guid?));
        //SqlMapper.RemoveTypeMap(typeof(Ulid));
        //SqlMapper.RemoveTypeMap(typeof(Ulid?));

        // await DapperDataContext.InitiateDatabaseAsync(connectionString);
    }
}
