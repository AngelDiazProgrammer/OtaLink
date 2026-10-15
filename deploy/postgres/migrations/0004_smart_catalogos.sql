-- ============================================================================
-- 0004 - Esquema smart: catálogos (tablas fuertes)
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       0001_esquemas_y_permisos.sql
--
-- Las tablas de "smart" son FUERTES: no tienen clave foránea hacia ninguna otra
-- tabla. Por eso la categoría de un interés es un número validado con CHECK y no
-- una relación con otra tabla.
--
--   smart.ciudades   ciudades disponibles (empieza solo con Bogotá)
--   smart.intereses  catálogo cerrado de intereses de la comunidad otaku
--                    (los datos se cargan en 0005)
--
-- Categorías de interés (no se renumeran; solo se añaden nuevas al final):
--   1 Géneros de anime     4 Videojuegos      7 Cultura japonesa
--   2 Manga y lectura      5 Cosplay          8 Coleccionismo
--   3 Series y franquicias 6 Música           9 Comunidad y actividades
-- ============================================================================

BEGIN;

-- ---------------------------------------------------------------- ciudades --
CREATE TABLE smart.ciudades (
    id     smallint    GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    codigo varchar(30) NOT NULL,
    nombre varchar(80) NOT NULL,
    activa boolean     NOT NULL DEFAULT true,
    CONSTRAINT uq_ciudades_codigo UNIQUE (codigo),
    CONSTRAINT ck_ciudades_codigo_minuscula CHECK (codigo = lower(codigo))
);

COMMENT ON TABLE smart.ciudades IS 'Ciudades disponibles. Para abrir una ciudad nueva basta insertar una fila.';

INSERT INTO smart.ciudades (codigo, nombre) VALUES ('bogota', 'Bogotá');

-- --------------------------------------------------------------- intereses --
CREATE TABLE smart.intereses (
    id        integer     GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    slug      varchar(60) NOT NULL,
    nombre    varchar(80) NOT NULL,
    categoria smallint    NOT NULL,
    activo    boolean     NOT NULL DEFAULT true,
    CONSTRAINT uq_intereses_slug UNIQUE (slug),
    CONSTRAINT ck_intereses_slug_minuscula CHECK (slug = lower(slug)),
    CONSTRAINT ck_intereses_categoria CHECK (categoria BETWEEN 1 AND 9)
);

COMMENT ON TABLE  smart.intereses      IS 'Catálogo cerrado de intereses. Lo administra el equipo, no las personas usuarias.';
COMMENT ON COLUMN smart.intereses.slug IS 'Identificador estable en minúsculas, sin espacios.';

CREATE INDEX ix_intereses_categoria ON smart.intereses (categoria);

-- ----------------------------------------------------------------- consultas --
CREATE FUNCTION smart.fn_ciudades_listar()
RETURNS SETOF smart.ciudades
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT *
    FROM smart.ciudades c
    WHERE c.activa
    ORDER BY c.nombre;
$$;

CREATE FUNCTION smart.fn_intereses_listar()
RETURNS SETOF smart.intereses
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT *
    FROM smart.intereses i
    WHERE i.activo
    ORDER BY i.categoria, i.nombre;
$$;

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte):
--   SELECT * FROM smart.fn_ciudades_listar();     -- 1 fila: bogota
--   SELECT * FROM smart.fn_intereses_listar();    -- vacío hasta ejecutar 0005
-- ----------------------------------------------------------------------------
