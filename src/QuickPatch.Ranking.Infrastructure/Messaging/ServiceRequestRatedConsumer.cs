using System.Text.Json;

using Confluent.Kafka;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using QuickPatch.Ranking.Application.Events;
using QuickPatch.Ranking.Application.Reputation;

namespace QuickPatch.Ranking.Infrastructure.Messaging;

/// <summary>
/// Consumidor de <c>service-request.rated</c> v1 (ADR-027): suma la calificación a la reputación del técnico. Confirma el
/// offset solo después de aplicar el evento, descarta el mensaje que no cumple el contrato y reintenta los errores de lectura
/// no fatales, igual que los consumidores de ServiceRequest y Payments.
/// </summary>
public sealed partial class ServiceRequestRatedConsumer(
    IServiceScopeFactory scopes,
    IConsumer<string, string> consumer,
    ILogger<ServiceRequestRatedConsumer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => ConsumeLoopAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(EventTypes.ServiceRequestRated);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromSeconds(1));
                }
                catch (ConsumeException ex) when (!ex.Error.IsFatal)
                {
                    LogConsumeFailed(logger, ex.Error.Reason, ex);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                if (result is null)
                {
                    continue;
                }

                if (await TryProcessAsync(result.Message.Value, stoppingToken))
                {
                    consumer.Commit(result);
                }
                else
                {
                    consumer.Seek(result.TopicPartitionOffset);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            consumer.Close();
        }
    }

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

        if (envelope?.Data is not { } data || envelope.EventType != EventTypes.ServiceRequestRated || envelope.EventVersion != 1
            || envelope.EventId == Guid.Empty || envelope.TenantId == Guid.Empty || data.ServiceRequestId == Guid.Empty
            || data.TechnicianId == Guid.Empty)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer service-request.rated ({Reason}); se reintentará.")]
    private static partial void LogConsumeFailed(ILogger logger, string reason, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falló el procesamiento del evento {EventId}; se reintentará.")]
    private static partial void LogProcessingFailed(ILogger logger, Guid eventId, Exception exception);
}