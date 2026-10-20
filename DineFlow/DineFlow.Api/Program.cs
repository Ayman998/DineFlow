using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DineFlow.Api.Common;
using DineFlow.Api.Tenancy;
using DineFlow.Application;
using DineFlow.Application.Abstractions;
using DineFlow.Infrastructure;
using DineFlow.Infrastructure.Persistence;
using DineFlow.Infrastructure.Security;
using DineFlow.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, ClaimsTenantContext>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.Value.SigningKeyBytes()),
            NameClaimType = "sub",
            RoleClaimType = DineFlowClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Merchant, p => p
        .RequireClaim(DineFlowClaims.Scope, DineFlowClaims.MerchantScope)
        .RequireClaim(DineFlowClaims.TenantId))
    .AddPolicy(Policies.MerchantManager, p => p
        .RequireClaim(DineFlowClaims.Scope, DineFlowClaims.MerchantScope)
        .RequireClaim(DineFlowClaims.TenantId)
        .RequireRole("Owner", "Manager"))
    .AddPolicy(Policies.Guest, p => p
        .RequireClaim(DineFlowClaims.Scope, DineFlowClaims.GuestScope)
        .RequireClaim(DineFlowClaims.SessionId));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(Policies.PublicRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(Policies.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddControllers(o => o.Filters.Add<AppExceptionFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DineFlow API", Version = "v1" });
    var bearer = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", bearer);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    try
    {
        await app.Services.InitialiseDatabaseAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database initialisation failed. Is SQL Server running and reachable?");
    }
}

// Request logging wraps the exception handler so it records the status code the client actually got.
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Uploaded menu images. File names are random and never reused, so they can be cached for a year.
var media = app.Services.GetRequiredService<LocalFileStorage>();
Directory.CreateDirectory(media.RootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(media.RootPath),
    RequestPath = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value.PublicPath,
    OnPrepareResponse = file =>
    {
        file.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        file.Context.Response.Headers.XContentTypeOptions = "nosniff";
    }
});

app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();
