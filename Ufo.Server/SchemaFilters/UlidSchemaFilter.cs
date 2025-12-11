using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ufo.Server.SchemaFilters;

public class UlidSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(Ulid))
        {
            schema.Type = "string";
            schema.Format = "ulid";
            schema.Pattern = @"^[0-7][0-9A-HJKMNP-TV-Z]{25}$";
            schema.Description = "A ULID (Universally Unique Lexicographically Sortable Identifier)";
            schema.Example = new OpenApiString(Ulid.NewUlid().ToString());
        }
    }
}
