using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Database.Handlers;
using Ufo.Database.Repositories;

namespace Ufo.Database.Extensions
{
    public static class DependencyExtension
    {
        public static void AddDataLayer(IServiceCollection services)
        {
            //services.AddScoped<IFileSystemSqLiteRepository>(serviceProvider =>
            //{
            //    var logger = serviceProvider.GetService<ILogger<FileSystemSqLiteRepository>>();
            //    var dapperDataContext = new FileSystemSqLiteRepository(connectionString, logger);

            //    return dapperDataContext;
            //});

            services.AddScoped<IFileSystemSqLiteRepository, FileSystemSqLiteRepository>();

            SqlMapper.AddTypeHandler(new SqlGuidTypeHandler());
            SqlMapper.AddTypeHandler(new SqlNullableGuidTypeHandler());
            SqlMapper.RemoveTypeMap(typeof(Guid));
            SqlMapper.RemoveTypeMap(typeof(Guid?));
        }
    }
}
