using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Monitoring.Api;
using Monitoring.Api.Swagger;
using Monitoring.Api.Auth;
using Monitoring.Api.Live;
using Monitoring.Core;
using Monitoring.Core.Live;
using Monitoring.Infrastructure;
using Monitoring.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILiveNotifier, SignalRLiveNotifier>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Задайте Jwt:Key.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key должен быть не короче 32 байт.");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/live"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                if (context.Principal?.FindFirst("token_use")?.Value != JwtTokenService.AccessUse)
                {
                    context.Fail("Нужен access token.");
                }

                return Task.CompletedTask;
            },
        };
    });
// Manager — только менеджер. Kitchen — менеджер и кухня. Orders — менеджер и официант. Staff — все три.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Manager", policy => policy.RequireRole(nameof(UserRole.Manager)));
    options.AddPolicy("Kitchen", policy => policy.RequireRole(nameof(UserRole.Manager), nameof(UserRole.Kitchen)));
    options.AddPolicy("Orders", policy => policy.RequireRole(nameof(UserRole.Manager), nameof(UserRole.Waiter)));
    options.AddPolicy("Staff", policy => policy.RequireRole(nameof(UserRole.Manager), nameof(UserRole.Waiter), nameof(UserRole.Kitchen)));
});
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddMonitoringSwagger();
builder.Services.AddCors(options =>
{
    options.AddPolicy("clients", policy =>
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host is "localhost" or "127.0.0.1")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
if (builder.Configuration.GetValue("Expiry:Enabled", true))
{
    builder.Services.AddHostedService<ExpiryWorker>();
}

var app = builder.Build();
await app.Services.MigrateAndSeedAsync();

app.UseMonitoringSwagger();
app.UseCors("clients");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
    .Docs("Служебное", "Жив ли процесс", "Без токена. Выход: status = ok. Базу не проверяет.")
    .Ok<HealthResponse>();
app.MapAuthEndpoints();
app.MapCatalogEndpoints();
app.MapDeliveryEndpoints();
app.MapStockEndpoints();
app.MapOrderEndpoints();
app.MapKitchenEndpoints();
app.MapShiftEndpoints();
app.MapWriteOffEndpoints();
app.MapAnalyticsEndpoints();
app.MapReportEndpoints();
app.MapSimulatorEndpoints();
app.MapHub<LiveHub>("/hubs/live");

app.Run();

public partial class Program;
