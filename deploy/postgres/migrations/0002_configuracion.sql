-- ============================================================================
-- 0002 - Esquema configuracion: parámetros del sistema
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       0001_esquemas_y_permisos.sql
--
-- Las reglas de negocio con números (edad mínima, máximo de intereses...) viven
-- aquí y NO dentro del código de cada procedimiento. Para cambiar una regla se
-- actualiza una fila, sin reescribir ninguna rutina.
--
-- Las longitudes máximas de las columnas (por ejemplo varchar(40) del nombre
-- público) son un límite físico: un parámetro nunca debe superarlas.
-- ============================================================================

BEGIN;

CREATE TABLE configuracion.parametros (
    clave               varchar(80)  PRIMARY KEY,
    valor               varchar(200) NOT NULL,
    descripcion         varchar(300),
    fecha_actualizacion timestamptz  NOT NULL DEFAULT now()
);

COMMENT ON TABLE configuracion.parametros IS 'Parámetros del sistema. Clave con formato modulo.nombre.';

INSERT INTO configuracion.parametros (clave, valor, descripcion) VALUES
    ('perfil.edad_minima',           '18',  'Edad mínima para usar OtaLink.'),
    ('perfil.edad_maxima_plausible', '120', 'Edad máxima aceptada: descarta fechas de nacimiento absurdas.'),
    ('perfil.nombre_publico_min',    '2',   'Longitud mínima del nombre público.'),
    ('perfil.nombre_publico_max',    '40',  'Longitud máxima del nombre público (no puede superar varchar(40)).'),
    ('perfil.biografia_max',         '500', 'Longitud máxima de la biografía (no puede superar varchar(500)).'),
    ('perfil.intereses_max',         '20',  'Máximo de intereses que puede elegir un perfil.');

-- Trigger reutilizable: mantiene fecha_actualizacion al día en cualquier tabla que la tenga.
CREATE FUNCTION configuracion.tgf_actualizar_fecha()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.fecha_actualizacion := now();
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_parametros_fecha
    BEFORE UPDATE ON configuracion.parametros
    FOR EACH ROW EXECUTE FUNCTION configuracion.tgf_actualizar_fecha();

-- Lee un parámetro numérico. Si la clave no existe o no es un número, falla con un
-- error claro: una regla mal configurada nunca debe pasar en silencio.
CREATE FUNCTION configuracion.fn_parametro_entero(p_clave varchar)
RETURNS integer
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_valor integer;
BEGIN
    SELECT p.valor::integer INTO STRICT v_valor
    FROM configuracion.parametros p
    WHERE p.clave = p_clave;

    RETURN v_valor;
END;
$$;

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte):
--   SELECT clave, valor FROM configuracion.parametros ORDER BY clave;      -- 6 filas
--   SELECT configuracion.fn_parametro_entero('perfil.edad_minima');        -- 18
-- ----------------------------------------------------------------------------
