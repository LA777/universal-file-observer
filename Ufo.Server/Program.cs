using Cysharp.Serialization.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.OpenApi;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;
using Ufo.Database.Extensions;
using Ufo.Database.Repositories;
using Ufo.DataProviders;
using Ufo.Server.SchemaFilters;
using Ufo.Server.Services;

Console.WriteLine("App started. Version: 0.0.2");

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

builder.Services.Configure<DatabaseOptions>(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.Configure<ApplicationSettings>(builder.Configuration.GetSection("ApplicationSettings"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JWT"));

builder.Services.AddTransient<ISystemInfoProvider, SystemInfoProvider>();
builder.Services.AddTransient<IFileSystemRepository, FileSystemRepository>();
builder.Services.AddTransient<ILabelsRepository, LabelsRepository>();
builder.Services.AddTransient<ISearchRepository, SearchRepository>();
builder.Services.AddTransient<IUserRepository>(provider =>
    new UserRepository(connectionString, provider.GetRequiredService<ILogger<UserRepository>>()));

await DependencyExtension.AddDataLayerAsync(builder.Services, connectionString);

builder.Services.AddTransient<IJwtTokenService, JwtTokenService>();

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

builder.Services.AddControllers(options =>
{
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
})
    .AddJsonOptions((options) => {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new UlidJsonConverter());
    });

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "UFO API",
        Version = "v1" // This is the crucial part
    });
    c.SchemaFilter<UlidSchemaFilter>();
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        //options.SerializeAsV2 = true;
    });
    app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "v1"); });
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

var appEndpointUrl = app.Configuration["Kestrel:Endpoints:App:Url"] ?? "https://localhost:55000";

OpenBrowser(appEndpointUrl);

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
