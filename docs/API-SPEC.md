# API-SPEC

API REST con controladores (solo JSON). Base: `/api`. Todos los endpoints exigen sesión (`401` sin ella), salvo los de autenticación, que son públicos pero con límite de peticiones.
En desarrollo, la documentación interactiva (OpenAPI + Scalar) está en `/scalar`; no se publica en producción.

## Convenciones

- **Autenticación:** token de portador (*bearer*): `Authorization: Bearer <token de acceso>`. Se obtiene en `iniciar-sesion`. La persona se identifica **siempre por su sesión**: ningún endpoint recibe un identificador de usuario en la URL o el cuerpo, así nadie puede acceder a datos de otra persona manipulando identificadores.
- **JSON:** `camelCase`; fechas `yyyy-MM-dd` (`DateOnly`); enumeraciones como texto (por ejemplo `"GenerosDeAnime"`).
- **Errores:** `ProblemDetails` (`application/problem+json`). Los de negocio traen la extensión **`codigo`**, estable, igual al que devuelve el procedimiento de la base.

| Estado | Cuándo | `codigo` (ejemplos) |
|---|---|---|
| 400 | Dato inválido (validación primaria del DTO o regla de la base) | `perfil.menor_de_edad`, `perfil.nombre_invalido`, `perfil.demasiados_intereses`, `autenticacion.codigo_invalido` |
| 401 | Sin sesión, token inválido o credenciales incorrectas | `autenticacion.credenciales_invalidas`, `autenticacion.sesion_invalida` |
| 403 | Correo sin confirmar | `autenticacion.correo_no_confirmado` |
| 404 | No existe | `perfil.no_encontrado` |
| 409 | Ya existe o hay conflicto | `perfil.ya_existe` |
| 423 | Cuenta bloqueada por demasiados intentos fallidos | `autenticacion.cuenta_bloqueada` |
| 429 | Demasiadas peticiones desde la misma IP (endpoints de autenticación) | — |
| 500 | Error inesperado (sin detalles internos; el detalle queda en el registro del servidor) | — |

## Autenticación (`/api/autenticacion`, público, 20 peticiones por minuto por IP)

| Método y ruta | Cuerpo | Respuesta |
|---|---|---|
| `POST /api/autenticacion/registro` | `{ correo, contrasena }` (contraseña de 10 a 128 caracteres) | `202` con un mensaje **idéntico exista o no el correo**. La cuenta no sirve hasta confirmar el correo |
| `POST /api/autenticacion/confirmar-correo` | `{ usuarioId, codigo }` (vienen en el enlace del correo) | `200`. `400` con `autenticacion.codigo_invalido` |
| `POST /api/autenticacion/reenviar-confirmacion` | `{ correo }` | `202` idéntico exista o no el correo |
| `POST /api/autenticacion/iniciar-sesion` | `{ correo, contrasena }` | `200` `{ tokenType, accessToken, expiresIn, refreshToken }`. `401`, `403` (sin confirmar), `423` (bloqueada) |
| `POST /api/autenticacion/refrescar` | `{ tokenRefresco }` | `200` con un par de tokens nuevo. `401` si es inválido, venció o la contraseña cambió desde entonces |
| `POST /api/autenticacion/olvide-contrasena` | `{ correo }` | `202` idéntico exista o no el correo |
| `POST /api/autenticacion/restablecer-contrasena` | `{ correo, codigo, nuevaContrasena }` | `200`. `400` si el código es inválido, ya se usó o la contraseña no cumple |

Notas: los enlaces del correo valen 1 día y son de un solo uso. Tras 5 intentos fallidos la cuenta se bloquea 15 minutos. Cambiar la contraseña invalida las sesiones anteriores. Detalle de seguridad en [SECURITY.md](SECURITY.md).

## Catálogos

| Método y ruta | Descripción | Respuesta |
|---|---|---|
| `GET /api/ciudades` | Ciudades disponibles (hoy solo Bogotá) | `200` `[{ id, codigo, nombre }]` |
| `GET /api/intereses` | Catálogo cerrado de intereses de la comunidad otaku (172), ordenado por categoría y nombre | `200` `[{ id, slug, nombre, categoria }]` |

Categorías: `GenerosDeAnime`, `MangaYLectura`, `SeriesYFranquicias`, `Videojuegos`, `Cosplay`, `Musica`, `CulturaJaponesa`, `Coleccionismo`, `ComunidadYActividades`.

## Perfil de la persona autenticada (`/api/perfiles/me`)

| Método y ruta | Cuerpo | Respuesta |
|---|---|---|
| `POST /api/perfiles/me` | `{ nombrePublico, fechaNacimiento, biografia?, ciudadCodigo }` | `201` + `Location` + perfil. `409` si ya tiene |
| `GET /api/perfiles/me` | — | `200` perfil. `404` si no lo ha creado |
| `PUT /api/perfiles/me` | `{ nombrePublico, biografia? }` | `200` perfil |
| `PUT /api/perfiles/me/modos` | `{ modoAmistad, modoCitas }` (independientes: uno, ambos o ninguno) | `200` perfil |
| `PUT /api/perfiles/me/intereses` | `{ interesIds: [int] }` (reemplaza la selección; hasta 20 distintos; `[]` la vacía) | `200` perfil |

Perfil: `{ usuarioId, nombrePublico, fechaNacimiento, edad, biografia, ciudadCodigo, modoAmistad, modoCitas, interesIds, fechaCreacion, fechaActualizacion }`.

Reglas (las impone la base; los DTOs hacen una validación previa): mayor de 18 años (se calcula con la hora de Colombia; quien nació un 29 de febrero cumple en años no bisiestos el 1 de marzo), nombre público de 2 a 40 caracteres sin caracteres de control, biografía de hasta 500, ciudad disponible, hasta 20 intereses distintos del catálogo.

## Salud

| Ruta | Qué comprueba |
|---|---|
| `GET /health` | La aplicación está viva (no consulta nada externo) |
| `GET /health/ready` | Además, la base de datos responde. Si falla: `503` con cuerpo `Unhealthy`, sin detalles |

## Pendiente

Descubrimiento y amistad, citas, chat (SignalR), comunidad y eventos.
