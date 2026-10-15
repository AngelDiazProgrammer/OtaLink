-- ============================================================================
-- 0009 - Esquema proceso: reglas de negocio y rutinas de perfiles
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       0002_configuracion.sql, 0007_seguridad_usuarios_rutinas.sql
--                 y 0008_proceso_perfiles.sql
--
-- AQUÍ vive la regla de negocio autoritativa de los perfiles: mayoría de edad,
-- longitud del nombre y la biografía, máximo de intereses y ciudad válida. Los
-- valores numéricos salen de configuracion.parametros.
-- La aplicación hace una validación previa para no viajar a la base con datos
-- obviamente mal diligenciados, pero esta es la que no se puede saltar.
--
-- Códigos de error (estables; la aplicación los traduce a respuestas HTTP):
--   perfil.usuario_invalido  perfil.nombre_invalido      perfil.biografia_larga
--   perfil.fecha_nacimiento_invalida   perfil.menor_de_edad   perfil.ciudad_invalida
--   perfil.ya_existe         perfil.no_encontrado
--   perfil.interes_invalido  perfil.interes_inexistente  perfil.demasiados_intereses
-- ============================================================================

BEGIN;

-- ------------------------------------------------------- utilidades de fecha --
-- Fecha de hoy en Colombia (UTC-5 todo el año). La mayoría de edad se calcula con
-- ella y no con la hora UTC: de lo contrario, a las 8 p. m. en Bogotá ya sería "mañana".
CREATE FUNCTION proceso.fn_hoy_colombia()
RETURNS date
LANGUAGE sql
STABLE
AS $$
    SELECT (now() AT TIME ZONE 'America/Bogota')::date;
$$;

-- Día en que se cumplen p_anios años. Quien nació un 29 de febrero cumple, en años
-- no bisiestos, el 1 de marzo: criterio prudente, para no admitir a nadie un día antes.
CREATE FUNCTION proceso.fn_cumpleanos_en_anio(p_fecha_nacimiento date, p_anios integer)
RETURNS date
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    v_fecha date;
BEGIN
    v_fecha := (p_fecha_nacimiento + make_interval(years => p_anios))::date;

    -- PostgreSQL resuelve 29-feb + N años como 28-feb cuando el año no es bisiesto.
    IF extract(month FROM p_fecha_nacimiento) = 2 AND extract(day FROM p_fecha_nacimiento) = 29
       AND extract(month FROM v_fecha) = 2 AND extract(day FROM v_fecha) = 28 THEN
        v_fecha := v_fecha + 1;
    END IF;

    RETURN v_fecha;
END;
$$;

-- Edad cumplida en la fecha p_hoy.
CREATE FUNCTION proceso.fn_edad_cumplida(p_fecha_nacimiento date, p_hoy date)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    v_edad integer;
BEGIN
    v_edad := extract(year FROM p_hoy)::integer - extract(year FROM p_fecha_nacimiento)::integer;

    IF p_hoy < proceso.fn_cumpleanos_en_anio(p_fecha_nacimiento, v_edad) THEN
        v_edad := v_edad - 1;
    END IF;

    RETURN v_edad;
END;
$$;

-- ------------------------------------------------------------------ consultas --
CREATE FUNCTION proceso.fn_perfil_existe(p_usuario_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT EXISTS (SELECT 1 FROM proceso.perfiles p WHERE p.usuario_id = p_usuario_id);
$$;

-- Perfil completo con la edad ya calculada y los identificadores de sus intereses.
-- No devuelve filas si el perfil no existe.
CREATE FUNCTION proceso.fn_perfil_obtener(p_usuario_id uuid)
RETURNS TABLE (
    usuario_id          uuid,
    nombre_publico      varchar,
    fecha_nacimiento    date,
    edad                integer,
    biografia           varchar,
    ciudad_codigo       varchar,
    modo_amistad        boolean,
    modo_citas          boolean,
    interes_ids         integer[],
    fecha_creacion      timestamptz,
    fecha_actualizacion timestamptz
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT p.usuario_id,
           p.nombre_publico,
           p.fecha_nacimiento,
           proceso.fn_edad_cumplida(p.fecha_nacimiento, proceso.fn_hoy_colombia()),
           p.biografia,
           c.codigo,
           p.modo_amistad,
           p.modo_citas,
           coalesce(
               (SELECT array_agg(pint.interes_id ORDER BY pint.interes_id)
                FROM proceso.perfiles_intereses pint
                WHERE pint.usuario_id = p.usuario_id),
               ARRAY[]::integer[]),
           p.fecha_creacion,
           p.fecha_actualizacion
    FROM proceso.perfiles p
    JOIN smart.ciudades c ON c.id = p.ciudad_id
    WHERE p.usuario_id = p_usuario_id;
$$;

-- ------------------------------------------------------------ crear perfil ---
CREATE PROCEDURE proceso.sp_perfil_crear(
    IN  p_usuario_id       uuid,
    IN  p_nombre_publico   varchar,
    IN  p_fecha_nacimiento date,
    IN  p_biografia        varchar,
    IN  p_ciudad_codigo    varchar,
    OUT p_codigo           varchar,
    OUT p_mensaje          varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_hoy        date    := proceso.fn_hoy_colombia();
    v_edad_min   integer := configuracion.fn_parametro_entero('perfil.edad_minima');
    v_edad_max   integer := configuracion.fn_parametro_entero('perfil.edad_maxima_plausible');
    v_nombre_min integer := configuracion.fn_parametro_entero('perfil.nombre_publico_min');
    v_nombre_max integer := configuracion.fn_parametro_entero('perfil.nombre_publico_max');
    v_bio_max    integer := configuracion.fn_parametro_entero('perfil.biografia_max');
    v_nombre     varchar;
    v_bio        varchar;
    v_ciudad_id  smallint;
BEGIN
    IF p_usuario_id IS NULL OR NOT EXISTS (SELECT 1 FROM seguridad.usuarios u WHERE u.id = p_usuario_id) THEN
        p_codigo := 'perfil.usuario_invalido'; p_mensaje := 'La persona usuaria no es válida.';
        RETURN;
    END IF;

    v_nombre := btrim(p_nombre_publico);
    IF v_nombre IS NULL
       OR char_length(v_nombre) < v_nombre_min
       OR char_length(v_nombre) > v_nombre_max
       OR v_nombre ~ '[[:cntrl:]]' THEN
        p_codigo := 'perfil.nombre_invalido';
        p_mensaje := format('El nombre público debe tener entre %s y %s caracteres.', v_nombre_min, v_nombre_max);
        RETURN;
    END IF;

    v_bio := nullif(btrim(coalesce(p_biografia, '')), '');
    IF v_bio IS NOT NULL AND char_length(v_bio) > v_bio_max THEN
        p_codigo := 'perfil.biografia_larga';
        p_mensaje := format('La biografía no puede superar %s caracteres.', v_bio_max);
        RETURN;
    END IF;

    -- Fecha imposible: vacía, en el futuro o de hace más de la edad máxima plausible.
    IF p_fecha_nacimiento IS NULL
       OR p_fecha_nacimiento > v_hoy
       OR proceso.fn_cumpleanos_en_anio(p_fecha_nacimiento, v_edad_max) <= v_hoy THEN
        p_codigo := 'perfil.fecha_nacimiento_invalida';
        p_mensaje := 'La fecha de nacimiento no es válida.';
        RETURN;
    END IF;

    IF proceso.fn_cumpleanos_en_anio(p_fecha_nacimiento, v_edad_min) > v_hoy THEN
        p_codigo := 'perfil.menor_de_edad';
        p_mensaje := format('OtaLink es solo para mayores de %s años.', v_edad_min);
        RETURN;
    END IF;

    SELECT c.id INTO v_ciudad_id
    FROM smart.ciudades c
    WHERE c.codigo = lower(btrim(coalesce(p_ciudad_codigo, ''))) AND c.activa;

    IF v_ciudad_id IS NULL THEN
        p_codigo := 'perfil.ciudad_invalida'; p_mensaje := 'La ciudad no está disponible.';
        RETURN;
    END IF;

    INSERT INTO proceso.perfiles (usuario_id, nombre_publico, fecha_nacimiento, biografia, ciudad_id)
    VALUES (p_usuario_id, v_nombre, p_fecha_nacimiento, v_bio, v_ciudad_id);

    p_codigo := 'ok';
    p_mensaje := NULL;

EXCEPTION
    -- Dos peticiones simultáneas: la clave primaria garantiza que solo una gana.
    WHEN unique_violation THEN
        p_codigo := 'perfil.ya_existe'; p_mensaje := 'Ya tienes un perfil.';
END;
$$;

-- ------------------------------------------------- actualizar nombre y bio ----
CREATE PROCEDURE proceso.sp_perfil_actualizar_datos(
    IN  p_usuario_id     uuid,
    IN  p_nombre_publico varchar,
    IN  p_biografia      varchar,
    OUT p_codigo         varchar,
    OUT p_mensaje        varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_nombre_min integer := configuracion.fn_parametro_entero('perfil.nombre_publico_min');
    v_nombre_max integer := configuracion.fn_parametro_entero('perfil.nombre_publico_max');
    v_bio_max    integer := configuracion.fn_parametro_entero('perfil.biografia_max');
    v_nombre     varchar;
    v_bio        varchar;
BEGIN
    v_nombre := btrim(p_nombre_publico);
    IF v_nombre IS NULL
       OR char_length(v_nombre) < v_nombre_min
       OR char_length(v_nombre) > v_nombre_max
       OR v_nombre ~ '[[:cntrl:]]' THEN
        p_codigo := 'perfil.nombre_invalido';
        p_mensaje := format('El nombre público debe tener entre %s y %s caracteres.', v_nombre_min, v_nombre_max);
        RETURN;
    END IF;

    v_bio := nullif(btrim(coalesce(p_biografia, '')), '');
    IF v_bio IS NOT NULL AND char_length(v_bio) > v_bio_max THEN
        p_codigo := 'perfil.biografia_larga';
        p_mensaje := format('La biografía no puede superar %s caracteres.', v_bio_max);
        RETURN;
    END IF;

    UPDATE proceso.perfiles
    SET nombre_publico = v_nombre,
        biografia      = v_bio
    WHERE usuario_id = p_usuario_id;

    IF FOUND THEN
        p_codigo := 'ok'; p_mensaje := NULL;
    ELSE
        p_codigo := 'perfil.no_encontrado'; p_mensaje := 'Todavía no has creado tu perfil.';
    END IF;
END;
$$;

-- ------------------------------------------------------------ cambiar modos ---
-- Los modos Amistad y Citas son independientes: se puede usar uno, ambos o ninguno.
CREATE PROCEDURE proceso.sp_perfil_cambiar_modos(
    IN  p_usuario_id    uuid,
    IN  p_modo_amistad  boolean,
    IN  p_modo_citas    boolean,
    OUT p_codigo        varchar,
    OUT p_mensaje       varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    UPDATE proceso.perfiles
    SET modo_amistad = coalesce(p_modo_amistad, false),
        modo_citas   = coalesce(p_modo_citas, false)
    WHERE usuario_id = p_usuario_id;

    IF FOUND THEN
        p_codigo := 'ok'; p_mensaje := NULL;
    ELSE
        p_codigo := 'perfil.no_encontrado'; p_mensaje := 'Todavía no has creado tu perfil.';
    END IF;
END;
$$;

-- ---------------------------------------------------------- establecer intereses
-- Reemplaza TODOS los intereses del perfil en una sola transacción: o queda la
-- selección nueva completa o queda la anterior. Aplica solo la diferencia (borra lo que
-- sobra, inserta lo que falta). Bloquea la fila del perfil para que dos peticiones
-- simultáneas del mismo usuario se ejecuten una detrás de otra.
CREATE PROCEDURE proceso.sp_perfil_establecer_intereses(
    IN  p_usuario_id   uuid,
    IN  p_interes_ids  integer[],
    OUT p_codigo       varchar,
    OUT p_mensaje      varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_max        integer := configuracion.fn_parametro_entero('perfil.intereses_max');
    v_encontrado uuid;
    v_ids        integer[];
BEGIN
    SELECT p.usuario_id INTO v_encontrado
    FROM proceso.perfiles p
    WHERE p.usuario_id = p_usuario_id
    FOR UPDATE;

    IF v_encontrado IS NULL THEN
        p_codigo := 'perfil.no_encontrado'; p_mensaje := 'Todavía no has creado tu perfil.';
        RETURN;
    END IF;

    -- Sin repetidos.
    SELECT coalesce(array_agg(DISTINCT x), ARRAY[]::integer[]) INTO v_ids
    FROM unnest(coalesce(p_interes_ids, ARRAY[]::integer[])) AS x;

    IF EXISTS (SELECT 1 FROM unnest(v_ids) AS x WHERE x IS NULL OR x <= 0) THEN
        p_codigo := 'perfil.interes_invalido'; p_mensaje := 'Alguno de los intereses no es válido.';
        RETURN;
    END IF;

    IF cardinality(v_ids) > v_max THEN
        p_codigo := 'perfil.demasiados_intereses';
        p_mensaje := format('Puedes elegir hasta %s intereses.', v_max);
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1 FROM unnest(v_ids) AS x
        WHERE NOT EXISTS (SELECT 1 FROM smart.intereses i WHERE i.id = x AND i.activo)
    ) THEN
        p_codigo := 'perfil.interes_inexistente';
        p_mensaje := 'Alguno de los intereses no existe en el catálogo.';
        RETURN;
    END IF;

    DELETE FROM proceso.perfiles_intereses pint
    WHERE pint.usuario_id = p_usuario_id
      AND NOT (pint.interes_id = ANY (v_ids));

    INSERT INTO proceso.perfiles_intereses (usuario_id, interes_id)
    SELECT p_usuario_id, x FROM unnest(v_ids) AS x
    ON CONFLICT DO NOTHING;

    UPDATE proceso.perfiles SET fecha_actualizacion = now() WHERE usuario_id = p_usuario_id;

    p_codigo := 'ok';
    p_mensaje := NULL;
END;
$$;

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte):
--  1) Funciones de fecha (edad de alguien nacido el 12/03/1998 en una fecha fija):
--       SELECT proceso.fn_edad_cumplida(DATE '1998-03-12', DATE '2026-10-15');     -- 28
--       SELECT proceso.fn_cumpleanos_en_anio(DATE '2008-02-29', 18);                -- 2026-03-01
--  2) Procedimientos y funciones de proceso, todos ejecutables por otalink_app:
--       SELECT p.proname, p.prokind,
--              has_function_privilege('otalink_app', p.oid, 'EXECUTE') AS app_puede
--       FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
--       WHERE n.nspname = 'proceso' ORDER BY p.proname;
-- ----------------------------------------------------------------------------
