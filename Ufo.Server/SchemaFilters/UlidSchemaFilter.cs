using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace Ufo.Server.SchemaFilters;

public class UlidSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(Ulid))
        {
            if (schema is OpenApiSchema concreteSchema)
            {
                concreteSchema.Type = JsonSchemaType.String;
                // Clear existing properties (ULID is a primitive string type)
                concreteSchema.Properties = null;
                concreteSchema.Format = "ulid";
                // Add a pattern/regex for validation (26 chars, specific Base32 alphabet)
                concreteSchema.Pattern = "^[0-9A-HJKMNP-TV-Z]{26}$";


                // Add an example for the Swagger UI using JsonValue (JsonNode-based, compatible with v3.3.1)
                //concreteSchema.Example = JsonValue.Create(Ulid.NewUlid().ToString().ToUpperInvariant());
                // Add an example for the Swagger UI


                // Ensure the example is cast to an IOpenApiPrimitive (like OpenApiString)
                concreteSchema.Example = JsonNode.Parse($"\"{Ulid.NewUlid().ToString().ToUpperInvariant()}\"");
            }
        }
    }
}
