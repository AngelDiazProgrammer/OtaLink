-- ============================================================================
-- 0008 - Esquema proceso: perfiles y sus intereses
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       0004_smart_catalogos.sql  y  0006_seguridad_usuarios.sql
--
-- Estas tablas son de "proceso" porque DEPENDEN de otras (usuarios, ciudades,
-- intereses). Las reglas de negocio se aplican en los procedimientos de 0009;
-- aquí solo hay estructura y restricciones físicas.
--
--   proceso.perfiles             un perfil por usuario (comparte su identificador)
--   proceso.perfiles_intereses   intereses elegidos por cada perfil
-- ============================================================================

BEGIN;

CREATE TABLE proceso.perfiles (
    usuario_id          uuid         PRIMARY KEY,
    nombre_publico      varchar(40)  NOT NULL,
    fecha_nacimiento    date         NOT NULL,
    biografia           varchar(500),
    ciudad_id           smallint     NOT NULL,
    modo_amistad        boolean      NOT NULL DEFAULT false,
    modo_citas          boolean      NOT NULL DEFAULT false,
    fecha_creacion      timestamptz  NOT NULL DEFAULT now(),
    fecha_actualizacion timestamptz  NOT NULL DEFAULT now(),
    -- Si se borra la cuenta, se borra su perfil.
    CONSTRAINT fk_perfiles_usuarios FOREIGN KEY (usuario_id)
        REFERENCES seguridad.usuarios (id) ON DELETE CASCADE,
    CONSTRAINT fk_perfiles_ciudades FOREIGN KEY (ciudad_id)
        REFERENCES smart.ciudades (id)
);

COMMENT ON TABLE  proceso.perfiles                  IS 'Perfil público. Relación 1 a 1 con seguridad.usuarios.';
COMMENT ON COLUMN proceso.perfiles.modo_amistad     IS 'Modo Amistad. Independiente del modo Citas.';
COMMENT ON COLUMN proceso.perfiles.modo_citas       IS 'Modo Citas. Independiente del modo Amistad.';

CREATE INDEX ix_perfiles_ciudad ON proceso.perfiles (ciudad_id);

CREATE TABLE proceso.perfiles_intereses (
    usuario_id     uuid        NOT NULL,
    interes_id     integer     NOT NULL,
    fecha_creacion timestamptz NOT NULL DEFAULT now(),
    -- La clave primaria impide repetir un interés en un mismo perfil.
    CONSTRAINT pk_perfiles_intereses PRIMARY KEY (usuario_id, interes_id),
    CONSTRAINT fk_perfiles_intereses_perfiles FOREIGN KEY (usuario_id)
        REFERENCES proceso.perfiles (usuario_id) ON DELETE CASCADE,
    -- Un interés del catálogo con perfiles asociados no se puede borrar.
    CONSTRAINT fk_perfiles_intereses_intereses FOREIGN KEY (interes_id)
        REFERENCES smart.intereses (id) ON DELETE RESTRICT
);

-- Para buscar "quién comparte este interés" (descubrimiento de personas).
CREATE INDEX ix_perfiles_intereses_interes ON proceso.perfiles_intereses (interes_id);

CREATE TRIGGER trg_perfiles_fecha
    BEFORE UPDATE ON proceso.perfiles
    FOR EACH ROW EXECUTE FUNCTION configuracion.tgf_actualizar_fecha();

-- Auditoría. PRIVACIDAD: la fecha de nacimiento es un dato personal y no se copia a log.
CREATE TRIGGER trg_perfiles_auditoria
    AFTER INSERT OR UPDATE OR DELETE ON proceso.perfiles
    FOR EACH ROW EXECUTE FUNCTION log.tgf_auditar('fecha_nacimiento', 'usuario_id');

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte). Deben aparecer las dos tablas:
--   SELECT table_schema, table_name FROM information_schema.tables
--   WHERE table_schema = 'proceso' ORDER BY table_name;
-- ----------------------------------------------------------------------------
