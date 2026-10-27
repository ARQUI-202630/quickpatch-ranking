# ranking

**Tecnología:** ASP.NET Core

## Responsabilidad

Reputación y ranking derivados de resultados/calificaciones.

## Reglas

- Mantener el ownership definido en DD/SDD.
- No escribir directamente en tablas de otros servicios.
- Publicar/consumir eventos únicamente mediante contratos versionados.
- Mantener aislamiento multi-tenant cuando corresponda.

## Qué hace (ADR-027, SCRUM-498)

| Funcionalidad | Contrato | Jira |
|---|---|---|
|Consume `service-request.rated` y suma la calificación a la reputación del técnico en su tenant: cantidad, suma y promedio redondeado a un decimal (`NUMERIC(2,1)`). Idempotente por `eventId` (`processed_events`) y por solicitud (`applied_ratings`): un evento repetido, aunque llegue con otro `eventId`, no vuelve a sumar|`events/service-request.rated.v1.json` (DD 8.4.2)|SCRUM-32 / SCRUM-498|
|Marca reputación baja con 5 o más calificaciones y promedio menor a 3.0 (AC9-E2), comparando el promedio exacto. La marca no suspende al técnico: decide el Admin (RF-20)|ADR-027|SCRUM-498|
|Publica `ranking.technician-reputation-changed` (`technicianId`, `averageRating`, `ratingCount`, `lowReputation`, `changedAt`) por Outbox en la misma transacción; la clave del mensaje es el técnico. Identity lo proyecta en `technician_profiles.average_rating`|`events/ranking.technician-reputation-changed.v1.json`|SCRUM-498|

Ranking no expone endpoints de negocio: el gateway no le enruta nada (ADR-025) y la reputación vuelve a Identity por evento (ADR-027). Si Ranking está caído, la calificación igual se guarda en ServiceRequest y la reputación se pone al día cuando vuelve (AC5-E3).

**Datos (`db_ranking`).** `technician_reputations` (clave tenant y técnico), `applied_ratings` (clave tenant y solicitud), `processed_events` y `outbox_events`, todas con RLS forzado por `app.current_tenant`. Permisos en `db/roles.sql`: `ranking_app` sin BYPASSRLS y `ranking_outbox` solo para el publicador. Las migraciones van en la imagen como bundle de EF Core (`/app/efbundle`), para el init container con `ranking_migrator`; el manifiesto de despliegue es SCRUM-512.

**Configuración.** `ConnectionStrings:Ranking`, `Kafka:BootstrapServers` (vacío: no arrancan ni el consumidor ni el Outbox), `Kafka:ConsumerGroupId` y `Outbox:PublisherRole`.

## Estructura

Cuatro capas, según el SDD (secciones 6.3 y 6.5):

```text
QuickPatch.Ranking.slnx
src/
  QuickPatch.Ranking.Api/              Endpoints, validación de entrada y raíz de composición
  QuickPatch.Ranking.Application/      Casos de uso, comandos y consultas
  QuickPatch.Ranking.Domain/           Entidades, value objects y reglas de negocio (sin dependencias externas)
  QuickPatch.Ranking.Infrastructure/   PostgreSQL, Kafka, Redis y adaptadores externos
tests/
  unit/QuickPatch.Ranking.UnitTests/
  integration/QuickPatch.Ranking.IntegrationTests/
```

Dependencias permitidas: `Api → Application → Domain`; `Infrastructure → Application, Domain`. `Api` referencia `Infrastructure` solo para registrar sus servicios.

## Desarrollo local

Requiere el SDK de .NET indicado en `global.json`.

```bash
dotnet restore
dotnet format --verify-no-changes   # lint, igual que el CI
dotnet build -c Release
dotnet test tests/unit/QuickPatch.Ranking.UnitTests
dotnet test tests/integration/QuickPatch.Ranking.IntegrationTests
dotnet run --project src/QuickPatch.Ranking.Api
```

## Contenedor

- Imagen: `Dockerfile` en la raíz (multi-stage, usuario sin privilegios).
- Puerto: `8080`.
- Probes para k3s: `GET /health/live` (el proceso responde) y `GET /health/ready` (el servicio y sus dependencias están listos).
