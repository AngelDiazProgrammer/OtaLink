-- ============================================================================
-- 0001 - Esquemas y permisos
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       01-create-database.sql ya ejecutado (roles y base creados).
--
-- Crea los 5 esquemas de OtaLink y deja listo el modelo de permisos:
--
--   configuracion  parámetros del sistema (reglas de negocio configurables)
--   proceso        tablas transaccionales que dependen de otras (perfiles...)
--   log            auditoría y registros
--   seguridad      cuentas y datos de acceso (usuarios...)
--   smart          tablas fuertes: no dependen de ninguna otra (catálogos)
--
-- MODELO DE PERMISOS (importante):
--   La aplicación (otalink_app) NO tiene permiso sobre ninguna tabla.
--   Solo puede ejecutar procedimientos y funciones. Cada rutina se crea con
--   SECURITY DEFINER: se ejecuta con los permisos de su dueño (otalink_migrator),
--   así la transacción y la regla de negocio viven en la base y no se pueden
--   saltar con un SELECT/INSERT directo.
-- ============================================================================

BEGIN;

CREATE SCHEMA IF NOT EXISTS configuracion AUTHORIZATION otalink_migrator;
CREATE SCHEMA IF NOT EXISTS proceso       AUTHORIZATION otalink_migrator;
CREATE SCHEMA IF NOT EXISTS log           AUTHORIZATION otalink_migrator;
CREATE SCHEMA IF NOT EXISTS seguridad     AUTHORIZATION otalink_migrator;
CREATE SCHEMA IF NOT EXISTS smart         AUTHORIZATION otalink_migrator;

COMMENT ON SCHEMA configuracion IS 'Parámetros del sistema y reglas de negocio configurables.';
COMMENT ON SCHEMA proceso       IS 'Tablas transaccionales que dependen de otras tablas (perfiles, ...).';
COMMENT ON SCHEMA log           IS 'Auditoría y registros.';
COMMENT ON SCHEMA seguridad     IS 'Cuentas y datos de acceso.';
COMMENT ON SCHEMA smart         IS 'Tablas fuertes: no dependen de ninguna otra tabla (catálogos).';

-- Por defecto, PostgreSQL permite que CUALQUIERA ejecute una función nueva.
-- Se revoca: a partir de ahora, toda rutina creada por otalink_migrator nace sin
-- permiso de ejecución para nadie, y se concede explícitamente a continuación.
ALTER DEFAULT PRIVILEGES FOR ROLE otalink_migrator REVOKE EXECUTE ON ROUTINES FROM PUBLIC;

-- La aplicación solo "ve" los esquemas cuyas rutinas invoca. No ve log ni configuracion.
GRANT USAGE ON SCHEMA proceso, seguridad, smart TO otalink_app;

-- Toda rutina nueva en estos tres esquemas es ejecutable por la aplicación.
ALTER DEFAULT PRIVILEGES FOR ROLE otalink_migrator IN SCHEMA proceso   GRANT EXECUTE ON ROUTINES TO otalink_app;
ALTER DEFAULT PRIVILEGES FOR ROLE otalink_migrator IN SCHEMA seguridad GRANT EXECUTE ON ROUTINES TO otalink_app;
ALTER DEFAULT PRIVILEGES FOR ROLE otalink_migrator IN SCHEMA smart     GRANT EXECUTE ON ROUTINES TO otalink_app;

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte). Debe listar los 5 esquemas, todos de otalink_migrator:
--   SELECT nspname, pg_get_userbyid(nspowner) AS dueno
--   FROM pg_namespace
--   WHERE nspname IN ('configuracion','proceso','log','seguridad','smart')
--   ORDER BY nspname;
-- ----------------------------------------------------------------------------
