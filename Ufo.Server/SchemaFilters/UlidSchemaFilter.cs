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
            schema.Format = "uuid"; // Or a custom format like "ulid"
            schema.Example = new OpenApiString(Ulid.NewUlid().ToString()); // Add an example
        }
    }
}
