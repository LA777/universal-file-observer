using Cysharp.Serialization.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

builder.Services.Configure<DatabaseOptions>(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}
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
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    //options.Authority = "https://demo.duendesoftware.com";
    //options.Audience = "api";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtOptions.Key)),
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "name",
        RoleClaimType = "role"
    };
});


/*
builder.Services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Authority = "https://demo.duendesoftware.com";
        options.Audience = "api";
        options.TokenValidationParameters = new()
        {
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
*/

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

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen(c =>
//{
//    c.SwaggerDoc("v1", new OpenApiInfo
//    {
//        Title = "UFO API",
//        Version = "v1" // This is the crucial part
//    });
//    c.SchemaFilter<UlidSchemaFilter>();

//    // Add JWT Security Definition
//    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
//    {
//        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
//        Name = "Authorization",
//        In = ParameterLocation.Header,
//        Type = SecuritySchemeType.ApiKey, // SecuritySchemeType.Http,
//        Scheme = "Bearer",
//        BearerFormat = "JWT",
//    });

//    c.AddSecurityRequirement(new OpenApiSecurityRequirement
//    {
//        //{
//        //    new OpenApiSecuritySchemeReference("oauth2"),
//        //    ["api", "profile", "email", "openid"]
//        //}

//        //{
//        //    new OpenApiSecurityScheme
//        //    {
//        //        Reference = new OpenApiReference
//        //        {
//        //            Type = ReferenceType.SecurityScheme,
//        //            Id = "Bearer"
//        //        },
//        //        In = ParameterLocation.Header,
//        //    },
//        //    new string[] { }
//        //}
//    });
//});


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
            //Flows = new OpenApiOAuthFlows
            //{
            //    AuthorizationCode = new OpenApiOAuthFlow
            //    {
            //        AuthorizationUrl = new Uri("https://demo.duendesoftware.com/connect/authorize"),
            //        TokenUrl = new Uri("https://demo.duendesoftware.com/connect/token"),
            //        Scopes = new Dictionary<string, string>
            //        {
            //            { "api", "Access the Weather API" },
            //            { "openid", "Access the OpenID Connect user profile" },
            //            { "email", "Access the user's email address" },
            //            { "profile", "Access the user's profile" }
            //        }
            //    }
            //}


        });

        // Apply security requirement globally
        //document.Security = [
        //    new OpenApiSecurityRequirement
        //    {
        //        {
        //            new OpenApiSecuritySchemeReference("oauth2"),
        //            ["api", "profile", "email", "openid"]
        //        }
        //    }
        //];

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
    //app.UseSwagger(options =>
    //{
    //    //options.SerializeAsV2 = true;
    //});
    //app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "v1"); });


    //app.UseSwagger();
    // add Swagger UI and point to the OpenAPI document
    // also enable PKCE for OAuth2
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
        //options.OAuthUsePkce();
    });

    //app.UseSwagger();
    //app.UseSwaggerUI();
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
