using Cysharp.Serialization.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ufo.Abstractions.Database;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;
using Ufo.Database.Contexts;
using Ufo.Database.Repositories;
using Ufo.DataProviders;
using Ufo.Server.Extensions;
using Ufo.Server.Services;

Console.WriteLine("App started. Version: 0.0.3");

var builder = WebApplication.CreateBuilder(args);
var environment = builder.Configuration.GetSection("ASPNETCORE_ENVIRONMENT").Value ?? "Production";
builder.Configuration.AddJsonFile("appsettings.json", optional: false);
builder.Configuration.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);

// Add services to the container.
var applicationSettings = builder.Configuration.Get<ApplicationSettings>();
if (applicationSettings == null)
{
    throw new ArgumentNullException(nameof(ApplicationSettings), "ApplicationSettings is null.");
}

var jwtOptions = builder.Configuration.GetSection("JWT").Get<JwtOptions>();
if (jwtOptions == null)
{
    throw new ArgumentNullException(nameof(JwtOptions), "JwtOptions is null.");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

builder.Services.Configure<DatabaseOptions>(options =>
{
    options.ConnectionString = connectionString;
});
builder.Services.Configure<ApplicationSettings>(builder.Configuration.GetSection("ApplicationSettings"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JWT"));

builder.Services.AddScoped<ILabelsService, LabelsService>();

builder.Services.AddScoped<IDbConnectionFactory, SqliteConnectionFactory>();
builder.Services.AddTransient<ISystemInfoProvider, SystemInfoProvider>();
builder.Services.AddScoped<IFileSystemRepository, FileSystemRepository>();
builder.Services.AddScoped<ILabelsRepository, LabelsRepository>();
builder.Services.AddScoped<ISearchRepository, SearchRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

// TODO LA - Get sqliteConnection and Init Database (refactor)
if (!builder.Environment.IsFunctionalTesting())
{
    var sqliteConnection = new SqliteConnection(connectionString);
    await DapperDataContext.InitiateDatabaseAsync(sqliteConnection);
}

builder.Services.AddTransient<IJwtTokenService, JwtTokenService>();
builder.Services.AddTransient<IJwtClaimsService, JwtClaimsService>();
builder.Services.AddHttpContextAccessor();

// Add CORS policy for Angular development server
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policyBuilder =>
    {
        policyBuilder
            .WithOrigins("http://localhost:4200", "https://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Add JWT Authentication
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

builder.Services.AddControllers(options =>
{
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
})
    .AddJsonOptions((options) =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new UlidJsonConverter());
    });

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        // Ensure instances exist
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        // Add Bearer security scheme (Authorization Code flow only)
        document.Components.SecuritySchemes.Add("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JWT",
            Description = "Enter 'Bearer {token}'"
        });

        // Apply security requirement globally
        document.Security = [
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer", document),
                    []
                }
            }
        ];

        // Set the host document for all elements
        // including the security scheme references
        document.SetReferenceHostDocument();

        return Task.CompletedTask;
    });
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// maps to /openapi/v1.json
app.MapOpenApi();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // add Swagger UI and point to the OpenAPI document
    // also enable PKCE for OAuth2
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}

app.UseRouting();
app.UseCors("AllowAngularDev");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("/index.html");

if (app.Environment.IsProduction())
{
    app.UseHsts();
}

if (!app.Environment.IsFunctionalTesting())
{
    var appEndpointUrl = app.Configuration["Kestrel:Endpoints:App:Url"] ?? "https://localhost:55000";
    OpenBrowser(appEndpointUrl);
}

await app.RunAsync();

return;

static void OpenBrowser(string url)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        Process.Start("xdg-open", url);
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        Process.Start("open", url);
    }
    else
    {
        throw new NotImplementedException("Unknown OS type.");
    }
}
