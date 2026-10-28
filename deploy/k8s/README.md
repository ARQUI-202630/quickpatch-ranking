# Despliegue de Ranking en k3s

`ranking.yaml` crea el ConfigMap, el Deployment (con las migraciones como init container) y el Service en el namespace `quickpatch`. Es igual en QA y en producción; solo cambian los secretos y la dirección de Kafka. Ranking no tiene `IngressRoute`: no publica endpoints de negocio, solo consume `service-request.rated` y publica `ranking.technician-reputation-changed` (ADR-027).

## Secreto `ranking-secretos`

Lo crea DevOps desde Ansible Vault. Nunca se guarda en el repositorio. La plantilla común de Ansible (`secreto-servicio.yaml.j2`) agrega también `jwt-public-key`, pero Ranking no valida tokens y no la lee.

|Clave|Contenido|
|---|---|
|`migrator-connection`|Cadena de conexión con `ranking_migrator` (dueño de las tablas; solo para las migraciones)|
|`app-connection`|Cadena de conexión con `ranking_app` (sin `BYPASSRLS`), con el tope de pool del inventario de Ansible|

```bash
kubectl -n quickpatch create secret generic ranking-secretos \
  --from-literal=migrator-connection='Host=<vm-datos>;Port=5432;Database=db_ranking;Username=ranking_migrator;Password=<...>' \
  --from-literal=app-connection='Host=<vm-datos>;Port=5432;Database=db_ranking;Username=ranking_app;Password=<...>;Maximum Pool Size=<tope>'
```

## Primer despliegue

1. Base: `db_ranking` con dueño `ranking_migrator` y el rol `ranking_app` (Ansible). Topics `service-request.rated` y `ranking.technician-reputation-changed` creados con quickpatch-kafka v0.9.0.
2. Reemplazar `<vm-mensajeria>` en el ConfigMap por la IP de Kafka del ambiente y `kubectl apply -f deploy/k8s/ranking.yaml`. El pod queda esperando la imagen (`:pendiente`).
3. Push a `release/*`: el pipeline publica la imagen y la fija en el Deployment; el init container aplica las migraciones.
4. Solo la primera vez, un administrador de la base ejecuta `db/roles.sql` (crea `ranking_app` y `ranking_outbox`) y reinicia el servicio con `kubectl -n quickpatch rollout restart deployment/ranking`.
