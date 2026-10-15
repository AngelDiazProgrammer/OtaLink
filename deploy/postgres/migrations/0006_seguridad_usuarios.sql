-- ============================================================================
-- 0006 - Esquema seguridad: tabla de usuarios
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       0002_configuracion.sql  y  0003_log_auditoria.sql
--
-- Cuentas de acceso (correo y contraseña). ASP.NET Core Identity calcula el hash
-- de la contraseña, el bloqueo de cuenta y los sellos; esta tabla solo los guarda.
-- El nombre de usuario es el correo electrónico.
--
-- Las rutinas que leen y escriben esta tabla están en 0007.
-- ============================================================================

BEGIN;

CREATE TABLE seguridad.usuarios (
    id                          uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
    nombre_usuario              varchar(256) NOT NULL,
    nombre_usuario_normalizado  varchar(256) NOT NULL,
    correo                      varchar(256) NOT NULL,
    correo_normalizado          varchar(256) NOT NULL,
    correo_confirmado           boolean      NOT NULL DEFAULT false,
    hash_contrasena             text,
    sello_seguridad             varchar(64)  NOT NULL,   -- cambia al modificar credenciales: invalida sesiones y enlaces viejos
    sello_concurrencia          varchar(64)  NOT NULL,   -- control de concurrencia optimista
    fin_bloqueo                 timestamptz,             -- hasta cuándo está bloqueada la cuenta (NULL = no bloqueada)
    bloqueo_habilitado          boolean      NOT NULL DEFAULT true,
    intentos_fallidos           integer      NOT NULL DEFAULT 0,
    fecha_creacion              timestamptz  NOT NULL DEFAULT now(),
    fecha_actualizacion         timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT uq_usuarios_nombre_normalizado UNIQUE (nombre_usuario_normalizado),
    CONSTRAINT uq_usuarios_correo_normalizado UNIQUE (correo_normalizado),
    CONSTRAINT ck_usuarios_intentos_fallidos  CHECK (intentos_fallidos >= 0)
);

COMMENT ON TABLE  seguridad.usuarios                 IS 'Cuentas de acceso gestionadas por ASP.NET Core Identity.';
COMMENT ON COLUMN seguridad.usuarios.hash_contrasena IS 'Hash PBKDF2 calculado por Identity. Nunca la contraseña en claro.';

-- Mantiene fecha_actualizacion al día en cada UPDATE.
CREATE TRIGGER trg_usuarios_fecha
    BEFORE UPDATE ON seguridad.usuarios
    FOR EACH ROW EXECUTE FUNCTION configuracion.tgf_actualizar_fecha();

-- Auditoría de cambios. Se omiten el hash y los sellos: nunca deben copiarse a otra tabla.
CREATE TRIGGER trg_usuarios_auditoria
    AFTER INSERT OR UPDATE OR DELETE ON seguridad.usuarios
    FOR EACH ROW EXECUTE FUNCTION log.tgf_auditar('hash_contrasena,sello_seguridad,sello_concurrencia', 'id');

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte): la tabla existe y los dos triggers también.
--   SELECT tgname FROM pg_trigger
--   WHERE tgrelid = 'seguridad.usuarios'::regclass AND NOT tgisinternal ORDER BY tgname;
--   -- trg_usuarios_auditoria, trg_usuarios_fecha
-- ----------------------------------------------------------------------------
