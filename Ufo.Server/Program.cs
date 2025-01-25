using System.Diagnostics;
using System.Runtime.InteropServices;
using Ufo.Abstractions.Database.Repositories;
using Ufo.Abstractions.DataProviders;
using Ufo.Abstractions.Options;
using Ufo.Database.Extensions;
using Ufo.Database.Repositories;
using Ufo.DataProviders;
using Microsoft.AspNetCore.Mvc.NewtonsoftJson;
using Newtonsoft.Json;

Console.WriteLine("App started. Version: 0.0.1");

var builder = WebApplication.CreateBuilder(args);
var environment = builder.Configuration.GetSection("ASPNETCORE_ENVIRONMENT").Value ?? "Production";
builder.Configuration.AddJsonFile("appsettings.json", optional: false);
builder.Configuration.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);

// Add services to the container.
var applicationSettings = builder.Configuration.Get<ApplicationSettings>();
if (applicationSettings == null)
{
    throw new ArgumentNullException("ApplicationSettings is null.", nameof(ApplicationSettings));
}

builder.Services.Configure<ApplicationSettings>(builder.Configuration.GetSection("ApplicationSettings"));

builder.Services.AddTransient<ISystemInfoProvider, SystemInfoProvider>();
builder.Services.AddTransient<IFileSystemSqLiteRepository, FileSystemSqLiteRepository>();
DependencyExtension.AddDataLayer(builder.Services);



builder.Services.AddControllers()
    .AddNewtonsoftJson((options) => {
        options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
    });
    //.AddJsonOptions(options =>
    //{
    //   // options.JsonSerializerOptions.Loop
    //});


// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("/index.html");

if (app.Environment.IsProduction())
{
    app.UseHsts();
}

var appEndpointUrl = app.Configuration["Kestrel:Endpoints:App:Url"] ?? "https://localhost:55000";

OpenBrowser(appEndpointUrl);

app.Run();

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
