using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Database.Contexts;
using Ufo.Database.Handlers;
using Ufo.Database.Repositories;

namespace Ufo.Database.Extensions;

public static class DependencyExtension
{
    public static async Task AddDataLayerAsync(IServiceCollection services, string? connectionString)
    {
        services.AddScoped<IFileSystemSqLiteRepository, FileSystemSqLiteRepository>();

        SqlMapper.AddTypeHandler(new SqlUlidTypeHandler());
        SqlMapper.AddTypeHandler(new SqlNullableUlidTypeHandler());
        //SqlMapper.AddTypeHandler(new SqlGuidTypeHandler());
        //SqlMapper.AddTypeHandler(new SqlNullableGuidTypeHandler());
        //SqlMapper.RemoveTypeMap(typeof(Guid));
        //SqlMapper.RemoveTypeMap(typeof(Guid?));
        SqlMapper.RemoveTypeMap(typeof(Ulid));
        SqlMapper.RemoveTypeMap(typeof(Ulid?));


        var Tim = TimeProvider.System.GetUtcNow();



        await DapperDataContext.InitiateDatabaseAsync(connectionString);
    }
}
