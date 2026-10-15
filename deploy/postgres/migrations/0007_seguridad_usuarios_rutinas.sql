-- ============================================================================
-- 0007 - Esquema seguridad: procedimientos y funciones de usuarios
-- ============================================================================
-- Ejecutar como:  otalink_migrator   sobre la base:  otalink_dev
-- Requiere:       0006_seguridad_usuarios.sql
--
-- CONVENCIONES (valen para todos los módulos):
--   sp_*   procedimiento: ESCRIBE (transacción). Devuelve el resultado en dos
--          parámetros de salida: p_codigo ('ok' o un código de error estable) y
--          p_mensaje (texto para la persona usuaria, NULL si todo salió bien).
--   fn_*   función: CONSULTA. No modifica nada.
--   tgf_*  función de trigger.
--
-- Todas las rutinas son SECURITY DEFINER con search_path fijo (pg_catalog, pg_temp):
-- se ejecutan con los permisos de su dueño y no pueden ser "engañadas" creando
-- objetos con el mismo nombre en otro esquema. Todo nombre va calificado con su esquema.
--
-- Los permisos de ejecución para otalink_app los concede el ALTER DEFAULT
-- PRIVILEGES de 0001: no hace falta ningún GRANT aquí.
-- ============================================================================

BEGIN;

-- --------------------------------------------------------------- crear ------
-- Un código de error por cada restricción de unicidad que puede violarse, incluso si
-- dos peticiones llegan a la vez: la base de datos es quien decide, no la aplicación.
CREATE PROCEDURE seguridad.sp_usuario_crear(
    IN  p_id                         uuid,
    IN  p_nombre_usuario             varchar,
    IN  p_nombre_usuario_normalizado varchar,
    IN  p_correo                     varchar,
    IN  p_correo_normalizado         varchar,
    IN  p_hash_contrasena            text,
    IN  p_sello_seguridad            varchar,
    IN  p_sello_concurrencia         varchar,
    IN  p_bloqueo_habilitado         boolean,
    OUT p_codigo                     varchar,
    OUT p_mensaje                    varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_restriccion text;
BEGIN
    INSERT INTO seguridad.usuarios (
        id, nombre_usuario, nombre_usuario_normalizado, correo, correo_normalizado,
        hash_contrasena, sello_seguridad, sello_concurrencia, bloqueo_habilitado)
    VALUES (
        coalesce(p_id, gen_random_uuid()), p_nombre_usuario, p_nombre_usuario_normalizado,
        p_correo, p_correo_normalizado, p_hash_contrasena, p_sello_seguridad,
        p_sello_concurrencia, p_bloqueo_habilitado);

    p_codigo  := 'ok';
    p_mensaje := NULL;

EXCEPTION
    WHEN unique_violation THEN
        GET STACKED DIAGNOSTICS v_restriccion = CONSTRAINT_NAME;

        IF v_restriccion = 'uq_usuarios_correo_normalizado' THEN
            p_codigo := 'usuario.correo_duplicado';
            p_mensaje := 'Ya existe una cuenta con ese correo.';
        ELSIF v_restriccion = 'uq_usuarios_nombre_normalizado' THEN
            p_codigo := 'usuario.nombre_duplicado';
            p_mensaje := 'Ya existe una cuenta con ese nombre de usuario.';
        ELSE
            RAISE;   -- cualquier otra violación de unicidad es inesperada: que falle con su error real
        END IF;
END;
$$;

-- ------------------------------------------------------------ actualizar ----
-- Concurrencia optimista: solo actualiza si el sello de concurrencia que trae la
-- aplicación coincide con el guardado. Si otra petición modificó la cuenta antes,
-- devuelve usuario.conflicto_concurrencia en vez de pisar sus cambios.
CREATE PROCEDURE seguridad.sp_usuario_actualizar(
    IN  p_id                         uuid,
    IN  p_sello_concurrencia_actual  varchar,
    IN  p_nombre_usuario             varchar,
    IN  p_nombre_usuario_normalizado varchar,
    IN  p_correo                     varchar,
    IN  p_correo_normalizado         varchar,
    IN  p_correo_confirmado          boolean,
    IN  p_hash_contrasena            text,
    IN  p_sello_seguridad            varchar,
    IN  p_sello_concurrencia_nuevo   varchar,
    IN  p_fin_bloqueo                timestamptz,
    IN  p_bloqueo_habilitado         boolean,
    IN  p_intentos_fallidos          integer,
    OUT p_codigo                     varchar,
    OUT p_mensaje                    varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
DECLARE
    v_restriccion text;
BEGIN
    UPDATE seguridad.usuarios
    SET nombre_usuario             = p_nombre_usuario,
        nombre_usuario_normalizado = p_nombre_usuario_normalizado,
        correo                     = p_correo,
        correo_normalizado         = p_correo_normalizado,
        correo_confirmado          = p_correo_confirmado,
        hash_contrasena            = p_hash_contrasena,
        sello_seguridad            = p_sello_seguridad,
        sello_concurrencia         = p_sello_concurrencia_nuevo,
        fin_bloqueo                = p_fin_bloqueo,
        bloqueo_habilitado         = p_bloqueo_habilitado,
        intentos_fallidos          = p_intentos_fallidos
    WHERE id = p_id
      AND sello_concurrencia = p_sello_concurrencia_actual;

    IF FOUND THEN
        p_codigo  := 'ok';
        p_mensaje := NULL;
    ELSIF EXISTS (SELECT 1 FROM seguridad.usuarios WHERE id = p_id) THEN
        p_codigo  := 'usuario.conflicto_concurrencia';
        p_mensaje := 'La cuenta fue modificada por otra operación. Intenta de nuevo.';
    ELSE
        p_codigo  := 'usuario.no_encontrado';
        p_mensaje := 'La cuenta no existe.';
    END IF;

EXCEPTION
    WHEN unique_violation THEN
        GET STACKED DIAGNOSTICS v_restriccion = CONSTRAINT_NAME;

        IF v_restriccion = 'uq_usuarios_correo_normalizado' THEN
            p_codigo := 'usuario.correo_duplicado';
            p_mensaje := 'Ya existe una cuenta con ese correo.';
        ELSIF v_restriccion = 'uq_usuarios_nombre_normalizado' THEN
            p_codigo := 'usuario.nombre_duplicado';
            p_mensaje := 'Ya existe una cuenta con ese nombre de usuario.';
        ELSE
            RAISE;
        END IF;
END;
$$;

-- --------------------------------------------------------------- eliminar ---
CREATE PROCEDURE seguridad.sp_usuario_eliminar(
    IN  p_id       uuid,
    OUT p_codigo   varchar,
    OUT p_mensaje  varchar
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    DELETE FROM seguridad.usuarios WHERE id = p_id;

    IF FOUND THEN
        p_codigo  := 'ok';
        p_mensaje := NULL;
    ELSE
        p_codigo  := 'usuario.no_encontrado';
        p_mensaje := 'La cuenta no existe.';
    END IF;
END;
$$;

-- ---------------------------------------------------------------- consultas -
CREATE FUNCTION seguridad.fn_usuario_por_id(p_id uuid)
RETURNS SETOF seguridad.usuarios
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT * FROM seguridad.usuarios WHERE id = p_id;
$$;

CREATE FUNCTION seguridad.fn_usuario_por_nombre_normalizado(p_nombre_normalizado varchar)
RETURNS SETOF seguridad.usuarios
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT * FROM seguridad.usuarios WHERE nombre_usuario_normalizado = p_nombre_normalizado;
$$;

CREATE FUNCTION seguridad.fn_usuario_por_correo_normalizado(p_correo_normalizado varchar)
RETURNS SETOF seguridad.usuarios
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $$
    SELECT * FROM seguridad.usuarios WHERE correo_normalizado = p_correo_normalizado;
$$;

COMMIT;

-- ----------------------------------------------------------------------------
-- Comprobación (ejecutar aparte). Debe listar 3 procedimientos (p) y 3 funciones (f)
-- y mostrar que otalink_app puede ejecutarlas:
--   SELECT p.proname, p.prokind,
--          has_function_privilege('otalink_app', p.oid, 'EXECUTE') AS app_puede_ejecutar
--   FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
--   WHERE n.nspname = 'seguridad' ORDER BY p.proname;
-- ----------------------------------------------------------------------------
