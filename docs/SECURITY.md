# SECURITY

Decisiones y controles de seguridad de OtaLink. Solo se documenta lo **implementado y verificado con pruebas**; lo pendiente está en su propia sección. Cada control tiene una prueba que falla si deja de cumplirse.

## 1. Cuentas y contraseñas

| Control | Detalle |
|---|---|
| Sistema de contraseñas | **ASP.NET Core Identity**. No se inventó ninguno: el hash (PBKDF2, formato v3) y la verificación son de Microsoft |
| Almacenamiento | Solo el **hash** (`seguridad.usuarios.hash_contrasena`). Hay una prueba que comprueba que la contraseña nunca se guarda en claro |
| Política | Mínimo **10 caracteres** (máximo 128) y al menos 4 caracteres distintos. Se prioriza la longitud sobre la complejidad (recomendación de NIST) |
| Bloqueo | **5 intentos fallidos bloquean la cuenta 15 minutos**; un inicio correcto reinicia el contador |
| Confirmación de correo | Obligatoria: sin confirmar no se puede iniciar sesión |
| Tokens de enlace | Confirmar cuenta y restablecer contraseña usan tokens de Identity: **un solo uso**, vencen en 1 día y quedan ligados al sello de seguridad |
| Cambio de contraseña | Cambia el sello de seguridad: invalida los enlaces pendientes y **todas las sesiones anteriores** (el token de refresco viejo deja de servir) |

## 2. Anti-enumeración de cuentas

Nadie debe poder averiguar qué correos tienen cuenta mirando las respuestas.

- **Registro, reenvío de confirmación y "olvidé mi contraseña" responden exactamente lo mismo** (mismo estado y mismo texto) exista o no el correo, y solo envían mensaje cuando corresponde. Un fallo al enviar el correo se registra pero no cambia la respuesta.
- **Inicio de sesión:** un correo inexistente y una contraseña incorrecta devuelven la misma respuesta. Para un correo inexistente se calcula igualmente un hash de relleno, para que el **tiempo** de respuesta no delate la diferencia.
- La contraseña se comprueba **antes** de revelar que falta confirmar el correo: solo quien la conoce lo sabe.

Limitación conocida y aceptada: la respuesta de "cuenta bloqueada" sí revela que el correo existe, y alguien podría bloquear la cuenta de otra persona con intentos fallidos. Es la compensación habitual del bloqueo temporal.

## 3. Sesiones

- Tokens de portador (*bearer*) de ASP.NET Core: token de acceso de vida corta y token de refresco.
- El refresco valida que el token sea auténtico y vigente, que la cuenta siga confirmada y sin bloqueo, y que el **sello de seguridad** no haya cambiado.
- Una persona se identifica **siempre por su sesión**: ningún endpoint recibe un identificador de usuario. Hay pruebas del aislamiento entre cuentas, incluido el intento de pedir `/api/perfiles/{id-de-otra-persona}`.

## 4. Límite de peticiones

Los endpoints públicos de autenticación aceptan **20 peticiones por minuto por dirección IP** (configurable en `RateLimit:AutenticacionPorMinuto`); pasado el límite responden `429`. Detrás de Nginx la IP real viene de `X-Forwarded-For`, que la API solo acepta de la propia máquina (`127.0.0.1`).

## 5. Base de datos

- La aplicación (`otalink_app`) **no tiene permiso sobre ninguna tabla**: solo ejecuta procedimientos y funciones `SECURITY DEFINER` con `search_path` fijo. Hay pruebas que intentan a propósito un `SELECT` y un `UPDATE` directos y comprueban que PostgreSQL los rechaza (error 42501), y que la aplicación no ve los esquemas `log` ni `configuracion`.
- **Las reglas de negocio las impone la base**, no solo el código: un cliente que se salte la validación del DTO igualmente es rechazado. Hay pruebas que llaman al servicio sin pasar por los DTOs.
- **Concurrencia:** las actualizaciones de cuenta usan control optimista (sello de concurrencia); dos peticiones simultáneas de crear perfil no pueden duplicarlo.
- **Escrituras bloqueadas en EF:** `SaveChanges` lanza una excepción; solo se escribe con procedimientos.

## 6. Auditoría y privacidad

- `log.auditoria` registra los cambios por trigger, **omitiendo datos sensibles**: nunca se copian el hash de contraseña, los sellos de seguridad ni la fecha de nacimiento.
- Los registros de la aplicación (Serilog) no incluyen cuerpos de petición ni cabeceras. El detalle de un error inesperado queda en el servidor; el cliente solo recibe un mensaje genérico.
- Los correos de desarrollo (`Correo:Modo = Registro`) escriben el enlace en el registro: **solo para desarrollo**. En cualquier otro modo, si el envío no está configurado, falla de forma explícita en lugar de registrar códigos secretos.

## 7. Secretos

- La cadena de conexión de desarrollo vive en *User Secrets*; en el servidor, en una variable de entorno. **Nunca en Git.**
- Las contraseñas de los roles de la base están en `/etc/otalink/` (permisos `600`, solo `root`).
- Una credencial que se muestra en pantalla y se pega en un chat se considera expuesta y se rota (ya ocurrió una vez: se rotó).

## 8. Pendiente (aún NO implementado)

| Tema | Estado |
|---|---|
| **Llaves de Data Protection persistentes** | Sin ellas, reiniciar el servicio en el servidor cerraría todas las sesiones y invalidaría los enlaces pendientes. Se resuelve al desplegar |
| **Envío real de correo (SMTP)** | Hoy solo existe el modo de desarrollo. Falta elegir proveedor |
| **HTTPS** | Pendiente de dominio y certificado. Hasta entonces los tokens viajan sin cifrar: **no exponer fuera de la red de desarrollo** |
| Cookie de sesión de la Web (patrón BFF) y protección contra CSRF | La Web aún no tiene login. La API usa tokens de portador, que no sufren CSRF; la cookie de la Web sí necesitará protección |
| CORS | Cerrado por defecto; se abrirá solo lo necesario si un cliente lo requiere |
| Verificación de contraseñas filtradas | No implementada (recomendación de NIST) |
| Segundo factor (2FA) | No implementado |
| Cabeceras de seguridad HTTP (HSTS, CSP...) | No implementadas |
| Autorización por recurso en los módulos futuros (chat, citas...) | Se aplicará con cada módulo, siguiendo el principio de "la sesión decide, nunca un identificador del cliente" |
| Política de tratamiento de datos personales y condiciones de uso | Debe definirse antes del lanzamiento en Colombia (Ley 1581 de 2012) |
| Revisión de seguridad por una persona externa | Pendiente antes de la beta pública |
