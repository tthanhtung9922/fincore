using System.Text;
using FinCore.EventBus.Abstractions;
using FinCore.Identity.Application.Commands.RegisterUser;
using FinCore.Identity.Infrastructure.DependencyInjection;
using FinCore.Observability.HealthChecks;
using FinCore.Observability.Logging;
using FinCore.Observability.Middleware;
using FinCore.Observability.Tracing;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using FinCore.Identity.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddFinCoreLogging("identity");
builder.Services.AddFinCoreTracing("identity");

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(RegisterUserCommand).Assembly));

builder.Services.AddValidatorsFromAssembly(typeof(RegisterUserCommand).Assembly);

builder.Services.AddIdentityInfrastructure(builder.Configuration);

var jwtSecret = Environment.GetEnvironmentVariable("JWT__SECRET") ?? "dev-secret-must-be-at-least-32-chars!!";
var jwtIssuer = Environment.GetEnvironmentVariable("JWT__ISSUER") ?? "fincore-identity";
var jwtAudience = Environment.GetEnvironmentVariable("JWT__AUDIENCE") ?? "fincore";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddFinCoreHealthChecks();

var dbConnStr = Environment.GetEnvironmentVariable("DB__IDENTITY")
    ?? "Host=localhost;Port=5433;Database=fincore_identity;Username=fincore;Password=fincore_dev";

builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnStr, name: "postgres", tags: ["ready"]);

builder.Services.AddScoped<IEventBus, NoOpEventBus>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});

app.MapControllers();

app.Run();
