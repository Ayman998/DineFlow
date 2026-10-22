using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DineFlow.Api.Common;
using DineFlow.Api.Hubs;
using DineFlow.Api.Tenancy;
using DineFlow.Application;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Reviews;
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
builder.Services.AddScoped<IOrderNotifier, SignalROrderNotifier>();
builder.Services.AddScoped<IPaymentNotifier, SignalRPaymentNotifier>();
builder.Services.AddScoped<IReviewNotifier, SignalRReviewNotifier>();
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

// The sandbox provider accepts test cards and marks bills paid without moving money, so it must be
// switched on deliberately anywhere but a developer's machine (e.g. for a demo environment).
if (!builder.Environment.IsDevelopment()
    && builder.Configuration["Payments:Provider"] is null or "sandbox"
    && !builder.Configuration.GetValue<bool>("Payments:Sandbox:AllowOutsideDevelopment"))
{
    throw new InvalidOperationException(
        "The sandbox payment provider only runs in Development. Configure a real provider, or set Payments:Sandbox:AllowOutsideDevelopment for a demo.");
}

// Every error response carries the request's correlation id, so a support ticket can be matched to the logs.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
    c.ProblemDetails.Extensions["correlationId"] = c.HttpContext.TraceIdentifier);

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
        // Browsers can't set headers on WebSocket requests, so SignalR sends the token in the query string.
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/hubs")
                    && context.Request.Query["access_token"] is { Count: > 0 } token)
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
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

// The correlation id comes first so every log line for the request carries it, request logging included.
app.UseMiddleware<CorrelationIdMiddleware>();
// Request logging wraps the exception handler so it records the status code the client actually got.
app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (diagnostics, http) =>
    diagnostics.Set("CorrelationId", http.TraceIdentifier));
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// In development the Angular dev servers proxy to the API over plain http on the same machine.
// Redirecting those calls to https would send the browser to another origin, which drops the
// Authorization header (every signed-in call then fails with 401), so only redirect elsewhere.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

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
app.MapHub<MerchantHub>(MerchantHub.Path);
app.MapHub<GuestHub>(GuestHub.Path);
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
// Readiness: the database, and whether queued WhatsApp and e-mail messages are going out (see NotificationHealthCheck).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = (http, report) => http.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), e.Value.Description, e.Value.Data })
    })
});

app.Run();
