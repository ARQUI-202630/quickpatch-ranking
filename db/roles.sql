-- Roles y permisos de Ranking Service (DD, sección 10.2; ADR-027).
-- Lo ejecuta un administrador de la base DESPUÉS de las migraciones (que crean las tablas y las políticas RLS).
-- Las contraseñas no van aquí: se asignan aparte con ALTER ROLE ... LOGIN PASSWORD, desde el vault.
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ranking_app') THEN
        CREATE ROLE ranking_app NOLOGIN NOBYPASSRLS;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ranking_outbox') THEN
        CREATE ROLE ranking_outbox NOLOGIN BYPASSRLS;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO ranking_app, ranking_outbox;

-- Rol del servicio: todo bajo RLS. La reputación se actualiza; las calificaciones sumadas y los eventos aplicados solo se
-- insertan y se leen.
GRANT SELECT, INSERT, UPDATE ON technician_reputations TO ranking_app;
GRANT SELECT, INSERT ON applied_ratings TO ranking_app;
GRANT SELECT, INSERT ON processed_events TO ranking_app;
GRANT INSERT ON outbox_events TO ranking_app;

-- Rol del publicador del Outbox: solo lee y marca eventos; lo adopta el servicio con SET LOCAL ROLE.
GRANT SELECT, UPDATE ON outbox_events TO ranking_outbox;
GRANT ranking_outbox TO ranking_app WITH INHERIT FALSE, SET TRUE;
