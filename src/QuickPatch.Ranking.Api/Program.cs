using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using QuickPatch.Ranking.Application;
using QuickPatch.Ranking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Logs en JSON con scopes, para Loki (SCRUM-323). Nunca registran el comentario de una calificación (ADR-027).
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Probes de k3s (Documento de Infraestructura, sección 5.8).
// live: el proceso responde; ready: además, PostgreSQL responde.
// Ranking no publica endpoints de negocio: la reputación vuelve a Identity por evento (ADR-025, ADR-027).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(QuickPatch.Ranking.Infrastructure.DependencyInjection.ReadyTag),
});

app.Run();

/// <summary>Punto de entrada; público para las pruebas con WebApplicationFactory.</summary>
public partial class Program;