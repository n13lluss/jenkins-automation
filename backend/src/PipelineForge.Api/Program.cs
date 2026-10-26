using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PipelineForge.Api.Auth;
using PipelineForge.Api.Infrastructure;
using PipelineForge.Api.Services;
using PipelineForge.Infrastructure;
using PipelineForge.Infrastructure.Persistence;

// Every regex in the process gets a timeout, so crafted input cannot cause catastrophic backtracking.
AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(2));

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 1024 * 1024;
    k.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
});

builder.Services.AddPipelineForgeInfrastructure(config, addDatabase: !builder.Environment.IsEnvironment("Testing"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var authority = config["Auth:Authority"] ?? throw new InvalidOperationException("Auth:Authority is required.");
        o.Authority = authority;
        // Inside docker the API reaches the identity provider on an internal address; tokens keep the public issuer.
        if (config["Auth:MetadataAddress"] is { Length: > 0 } metadata) o.MetadataAddress = metadata;
        o.RequireHttpsMetadata = config.GetValue("Auth:RequireHttpsMetadata", true);
        o.MapInboundClaims = false;
        o.TokenValidationParameters.ValidIssuer = authority;
        o.TokenValidationParameters.ValidAudience = config["Auth:Audience"] ?? "pipelineforge-api";
        o.TokenValidationParameters.ValidateAudience = true;
        o.TokenValidationParameters.ValidateLifetime = true;
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
        o.TokenValidationParameters.NameClaimType = "preferred_username";
    });
builder.Services.AddAuthorization(Policies.Add);
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceRoleHandler>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<Auditor>();
builder.Services.AddScoped<ConnectionResolver>();
builder.Services.AddScoped<GenerationService>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Key(HttpContext c) => c.User.FindFirstValue("sub") ?? c.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Key(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("expensive", c =>
        RateLimitPartition.GetFixedWindowLimiter(Key(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.JsonSerializerOptions.MaxDepth = 32;
});
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var proxy in config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHsts();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/healthz").AllowAnonymous();
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.MapControllers();

if (config.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();

public partial class Program;
