using System.Text.Json;

using Confluent.Kafka;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using QuickPatch.Ranking.Application.Events;
using QuickPatch.Ranking.Application.Reputation;
using QuickPatch.Transversal.Messaging;

namespace QuickPatch.Ranking.Infrastructure.Messaging;

/// <summary>
/// Consumidor de <c>service-request.rated</c> v1 (ADR-027): suma la calificación a la reputación del técnico. Confirma el
/// offset solo después de aplicar el evento, descarta el mensaje que no cumple el contrato y reintenta los errores de lectura
/// no fatales, igual que los consumidores de ServiceRequest y Payments.
/// </summary>
public sealed partial class ServiceRequestRatedConsumer(
    IServiceScopeFactory scopes,
    IConsumer<string, string> consumer,
    ILogger<ServiceRequestRatedConsumer> logger)
    : KafkaConsumerService(consumer, [EventTypes.ServiceRequestRated], logger)
{
    protected override Task<bool> ProcessAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken) =>
        TryProcessAsync(result.Message.Value, cancellationToken);

    /// <summary>Aplica un mensaje; devuelve false si hay que reintentarlo.</summary>
    public async Task<bool> TryProcessAsync(string value, CancellationToken cancellationToken)
    {
        EventEnvelope<ServiceRequestRatedData>? envelope;
        try
        {
            envelope = EventSerializer.Deserialize<EventEnvelope<ServiceRequestRatedData>>(value);
        }
        catch (JsonException ex)
        {
            LogInvalidMessage(logger, ex);
            return true;
        }

        if (!ServiceRequestRatedContract.IsWellFormed(envelope))
        {
            LogInvalidMessage(logger, null);
            return true;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ServiceRequestRatedHandler>();
            await handler.HandleAsync(envelope, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Incluye la carrera de dos calificaciones nuevas del mismo técnico: la que pierde viola la clave y se reintenta.
            LogProcessingFailed(logger, envelope.EventId, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Mensaje de service-request.rated que no cumple el contrato; se descarta.")]
    private static partial void LogInvalidMessage(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falló el procesamiento del evento {EventId}; se reintentará.")]
    private static partial void LogProcessingFailed(ILogger logger, Guid eventId, Exception exception);
}