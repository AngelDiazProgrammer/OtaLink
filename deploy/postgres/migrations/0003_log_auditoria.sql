-- ============================================================================
-- 0003 - Esquema log: auditoría automática
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       0001_esquemas_y_permisos.sql
--
-- Una tabla de auditoría y UN trigger genérico que se engancha a cualquier tabla
-- (los scripts de cada módulo lo activan en sus tablas). Guarda qué cambió, cuándo
-- y con qué rol de base de datos.
--
-- PRIVACIDAD: el trigger recibe la lista de columnas sensibles a omitir
-- (contraseñas, sellos de seguridad, fecha de nacimiento...). Esos datos nunca
-- llegan a la tabla de auditoría.
-- ============================================================================

BEGIN;

CREATE TABLE log.auditoria (
    id            bigint       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    fecha         timestamptz  NOT NULL DEFAULT now(),
    esquema       varchar(63)  NOT NULL,
    tabla         varchar(63)  NOT NULL,
    operacion     varchar(10)  NOT NULL,
    registro_id   varchar(100),
    usuario_bd    varchar(63)  NOT NULL,
    datos_antes   jsonb,
    datos_despues jsonb,
    CONSTRAINT ck_auditoria_operacion CHECK (operacion IN ('INSERT', 'UPDATE', 'DELETE'))
);

COMMENT ON TABLE log.auditoria IS 'Cambios en tablas auditadas. Las columnas sensibles se omiten.';

CREATE INDEX ix_auditoria_tabla_fecha ON log.auditoria (esquema, tabla, fecha DESC);
CREATE INDEX ix_auditoria_registro    ON log.auditoria (registro_id);

-- Trigger genérico. Argumentos al crearlo:
--   TG_ARGV[0]  columnas a omitir, separadas por coma ('' si ninguna)
--   TG_ARGV[1]  nombre de la columna que identifica el registro (por defecto 'id')
-- Se ejecuta como AFTER: si la operación falla, no queda rastro de ella.
CREATE FUNCTION log.tgf_auditar()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_omitir     text[] := string_to_array(coalesce(TG_ARGV[0], ''), ',');
    v_columna_id text   := coalesce(TG_ARGV[1], 'id');
    v_antes      jsonb;
    v_despues    jsonb;
BEGIN
    IF TG_OP IN ('UPDATE', 'DELETE') THEN
        v_antes := to_jsonb(OLD) - v_omitir;
    END IF;

    IF TG_OP IN ('INSERT', 'UPDATE') THEN
        v_despues := to_jsonb(NEW) - v_omitir;
    END IF;

    INSERT INTO log.auditoria (esquema, tabla, operacion, registro_id, usuario_bd, datos_antes, datos_despues)
    VALUES (
        TG_TABLE_SCHEMA,
        TG_TABLE_NAME,
        TG_OP,
        coalesce(v_despues, v_antes) ->> v_columna_id,
        session_user,           -- el rol que abrió la sesión (otalink_app), no el dueño de la rutina
        v_antes,
        v_despues
    );

    RETURN NULL;
END;
$$;

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte). La tabla existe y está vacía hasta que otros
-- scripts activen el trigger:
--   SELECT count(*) FROM log.auditoria;     -- 0
-- ----------------------------------------------------------------------------
