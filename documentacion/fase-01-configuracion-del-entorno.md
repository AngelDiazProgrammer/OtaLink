# Fase 1: configuración del entorno

> Corresponde a las fases 0 a 4 del plan de trabajo original: diagnóstico del equipo, máquina virtual, administración de Ubuntu, red y entorno de servidor.
> Fecha de realización: 15 de octubre de 2026.
> Estado: **completada**, con una observación abierta sobre la estabilidad de la red (ver sección 10).

## Índice

1. [Objetivo y resultado](#1-objetivo-y-resultado)
2. [Mapa general](#2-mapa-general)
3. [Cómo leer este documento](#3-cómo-leer-este-documento)
4. [El equipo anfitrión y la máquina virtual](#4-el-equipo-anfitrión-y-la-máquina-virtual)
5. [Administración básica de Ubuntu](#5-administración-básica-de-ubuntu)
6. [Red](#6-red)
7. [Entorno de servidor](#7-entorno-de-servidor)
8. [Copias de seguridad](#8-copias-de-seguridad)
9. [Qué hay en el repositorio](#9-qué-hay-en-el-repositorio)
10. [Problemas encontrados y lecciones](#10-problemas-encontrados-y-lecciones)
11. [Decisiones y alternativas descartadas](#11-decisiones-y-alternativas-descartadas)
12. [Estado final y pendientes](#12-estado-final-y-pendientes)
13. [Chuleta de comandos usados](#13-chuleta-de-comandos-usados)
14. [Cómo recuperarse si algo se rompe](#14-cómo-recuperarse-si-algo-se-rompe)

---

## 1. Objetivo y resultado

**Objetivo:** tener un servidor Linux propio, seguro y reproducible donde luego correrá OtaLink, antes de escribir una sola línea de la aplicación.

**Por qué empezar por el servidor y no por el código:** una aplicación se prueba y se despliega en un entorno. Si el entorno es inestable o inseguro, cada problema posterior es ambiguo: no sabrás si falla tu código o el servidor. Además, el desarrollador quería aprender a administrar Ubuntu mientras se construye el producto.

**Resultado al terminar esta fase:**

- Una máquina virtual Ubuntu 26.04 LTS funcionando dentro de Windows.
- Acceso remoto seguro por SSH con llave (sin contraseñas).
- Firewall activo con solo lo imprescindible abierto.
- Red con IP fija conocida.
- Runtime de .NET 10 instalado y comprobado.
- PostgreSQL 18 con una base de datos y dos usuarios de permisos limitados.
- Copias de seguridad diarias automáticas, con restauración **probada**.
- Nginx funcionando como proxy inverso para la web y la API.

**Qué NO existe todavía:** la aplicación OtaLink (no hay solución .NET), los servicios `systemd` de la web y la API, HTTPS, dominio público ni copia de seguridad fuera de la VM.

---

## 2. Mapa general

```
                         INTERNET
                             |
                 Router del proveedor 192.168.1.1
              (no se puede configurar: es del proveedor)
                             |  Wi-Fi
   +-------------------------+---------------------------------------+
   |  PORTÁTIL (anfitrión)  Windows 11 Pro                          |
   |  Wi-Fi 192.168.1.9                                             |
   |                                                                 |
   |   Adaptador virtual "OtaLink-NAT"  192.168.50.1                |
   |        |   (NAT de Windows: deja salir a la VM a Internet)      |
   |   +----+-----------------------------------------------------+ |
   |   |  Hyper-V: VM "OtaLink-Server"   192.168.50.10            | |
   |   |  Ubuntu 26.04.1 LTS                                       | |
   |   |                                                           | |
   |   |  :22   SSH            (UFW: solo desde 192.168.50.0/24)   | |
   |   |  :80   Nginx ---+---> :5000  Web Blazor   (futuro)        | |
   |   |                 +---> :5100  API .NET      (futuro)        | |
   |   |  :5432 PostgreSQL     (solo local, nunca expuesto)        | |
   |   +-----------------------------------------------------------+ |
   +-----------------------------------------------------------------+
```

Idea clave: **el único punto de entrada es Nginx**. La web, la API y la base de datos escuchan solo en `127.0.0.1` (dentro de la VM), así que nadie puede llegar a ellas sin pasar por Nginx, y la base de datos ni siquiera es accesible desde Nginx.

---

## 3. Cómo leer este documento

Cada paso indica **dónde se ejecuta**, porque confundirlo fue el error más repetido de la fase:

- **WINDOWS**: el computador físico. Se usa PowerShell (a veces "como administrador").
- **VM**: el servidor Ubuntu. Se entra por SSH desde PowerShell: `ssh angel-diaz@192.168.50.10`.

Cada sección sigue la misma estructura: **qué es**, **para qué se usa**, **cómo se aplicó** y **cómo se verificó**.

Convenciones de Linux que verás:

- `sudo` ejecuta **un** comando con privilegios de administrador y pide la contraseña.
- Las opciones largas llevan dos guiones (`--debug`) y las cortas uno (`-h`).
- `|` (tubería) pasa la salida de un comando como entrada del siguiente.
- `<` hace que un comando lea un archivo como entrada.
- Mientras escribes una contraseña en la terminal **no se ve nada**: es normal.

---

## 4. El equipo anfitrión y la máquina virtual

### 4.1 Diagnóstico del equipo

**Qué es:** revisar el hardware antes de decidir cuántos recursos darle a la VM.

**Datos obtenidos (reales, consultados con PowerShell):**

| Dato | Valor |
|---|---|
| Sistema | Windows 11 Pro, build 26200 |
| Equipo | Portátil |
| CPU | Intel i7-9750H, 6 núcleos / 12 hilos |
| RAM | 47,8 GB |
| Disco C: | NVMe 477 GB (113 GB libres) |
| Disco D: | SSD 447 GB (282 GB libres) |
| Red | Solo Wi-Fi |
| Router | Del proveedor, sin acceso a su configuración |

**Cómo se verifica la virtualización:** que el Administrador de tareas (pestaña Rendimiento, CPU) diga "Virtualización: Habilitada" y que Hyper-V esté activo. En PowerShell, el campo `VirtualizationFirmwareEnabled` puede mostrar `False` aunque todo funcione, cuando Windows ya está ejecutando el hipervisor; lo que confirma el funcionamiento es `HypervisorPresent = True`.

**Consecuencia de ser un portátil:** si se suspende o se apaga, la VM y todo OtaLink se detienen. Debe usarse conectado a la corriente.

### 4.2 Hipervisor: Hyper-V

**Qué es:** el programa que permite ejecutar un computador virtual dentro de otro.

**Por qué Hyper-V:** ya venía activo en Windows 11 Pro. Instalar VirtualBox habría puesto dos hipervisores a competir por el mismo hardware.

### 4.3 La máquina virtual

| Campo | Valor | Por qué |
|---|---|---|
| Nombre | `OtaLink-Server` | |
| Ubicación | `D:\VMs\OtaLink` | El disco D: tiene espacio y deja libre el SSD del sistema |
| Generación | 2 | Usa UEFI, lo moderno |
| CPU | 4 vCPU | Quedan 8 hilos para Windows |
| RAM | 12 GB fijos | Sobra memoria en el anfitrión; fija evita que la VM compita |
| Disco | 100 GB dinámico (`.vhdx`) | Solo ocupa lo que se usa |
| Secure Boot | Plantilla "Microsoft UEFI Certificate Authority" | La plantilla de Windows no arranca Ubuntu |

**Disco dinámico vs fijo:** el dinámico crece según se usa (cómodo, pero puede llegar al máximo); el fijo reserva todo desde el inicio (algo más rápido y predecible). En un SSD la diferencia es pequeña, y se eligió el dinámico.

**Instantánea (checkpoint) vs copia de seguridad:**

| | Checkpoint de Hyper-V | Copia de seguridad real |
|---|---|---|
| Qué guarda | Diferencias respecto a un momento | Un archivo independiente |
| Dónde vive | Junto al disco de la VM | En otro lugar |
| Si el disco falla | Se pierde con la VM | Sobrevive |
| Sirve para | Volver atrás tras una prueba fallida | Recuperar tras un desastre |

Un checkpoint **no es** un respaldo.

### 4.4 La ISO de Ubuntu

**Versión elegida:** Ubuntu 26.04.1 LTS ("Resolute Raccoon"). LTS significa soporte de largo plazo.

**Por qué esta y no la 24.04:** la 26.04 es la LTS vigente y, según la documentación de Microsoft (página actualizada en abril de 2026), .NET 10 está soportado en ella y se instala desde los repositorios de Ubuntu.

**Verificación de integridad (se hizo):** se calculó el SHA256 de la ISO descargada y se comparó con el publicado en `https://releases.ubuntu.com/26.04/SHA256SUMS`. Coincidió:

```
601e30fbf5d97759367c632e2c33630665039b7e2158fd068403da3ccf1bda1f  ubuntu-26.04.1-desktop-amd64.iso
```

Por qué importa: garantiza que el archivo no está corrupto ni alterado. En Windows: `Get-FileHash <archivo> -Algorithm SHA256`.

### 4.5 Instalación

Se instaló Ubuntu Desktop (con escritorio gráfico, para poder administrar visualmente), con el nombre de equipo `otalink-server`, el usuario administrador `angel-diaz` y la zona horaria de Bogotá. Luego:

```bash
sudo apt update && sudo apt upgrade -y
```

- `apt` es el gestor de paquetes de Ubuntu: instala y actualiza software desde repositorios oficiales.
- `update` actualiza la lista de lo disponible; `upgrade` instala las actualizaciones.

**Checkpoints creados:**

| Nombre | Momento |
|---|---|
| `01-ubuntu-limpio` | Recién instalado y actualizado |
| `02-ssh-ufw` | SSH por llave y firewall activos |
| `03-red-fija` | IP fija configurada |

Recomendados y pendientes de confirmar por el desarrollador: `04-postgresql-respaldos` y `05-nginx-proxy`.

**Nota:** con una base de datos instalada, los checkpoints conviene hacerlos con la VM **apagada** (o de tipo "Producción"), para no restaurar una base a medias.

---

## 5. Administración básica de Ubuntu

### 5.1 Hora y zona horaria

**Para qué:** los registros (logs), las marcas de tiempo de la base y las tareas programadas dependen de la hora.

```bash
timedatectl                                   # ver estado
sudo timedatectl set-timezone America/Bogota  # fijar zona
```

Verificación: `Time zone: America/Bogota` y `System clock synchronized: yes`.

### 5.2 Actualizaciones automáticas de seguridad

**Qué es:** el paquete `unattended-upgrades` instala solo los parches de **seguridad**, sin tocar el resto.

**Para qué:** un servidor sin parches es la causa más común de incidentes.

Ya venía instalado y activo (`systemctl status unattended-upgrades`).

### 5.3 SSH: administración remota

**Qué es:** un canal cifrado para dar órdenes al servidor desde otro equipo. Aquí, desde PowerShell de Windows.

**Para qué:** copiar y pegar comandos cómodamente, transferir archivos (`scp`) y hacer túneles (como el de DBeaver).

**Cómo se aplicó:**

```bash
sudo apt install -y openssh-server        # VM
ssh angel-diaz@192.168.50.10              # WINDOWS
```

Dato técnico: en Ubuntu 26.04 el servicio usa *activación por socket*: `ssh.socket` escucha en el puerto 22 y arranca `ssh.service` con la primera conexión. Por eso `ssh.service` puede verse "inactive" y aun así funcionar.

**Autenticación con llave (en lugar de contraseña):**

Una llave SSH son dos archivos: la **privada** (nunca sale de Windows, se protege con una frase de paso) y la **pública** (se instala en el servidor). El servidor comprueba que tienes la privada sin que esta viaje por la red. No se puede adivinar por fuerza bruta como una contraseña.

```powershell
# WINDOWS: crear la llave
ssh-keygen -t ed25519 -C "angel-otalink"

# WINDOWS: instalar la pública en la VM (PowerShell no trae ssh-copy-id)
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh angel-diaz@192.168.50.10 "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

Los permisos `700` y `600` importan: SSH ignora la llave si otros usuarios pueden leerla.

**Regla de oro seguida:** primero se instaló la llave y se probó que entraba, **y solo después** se desactivó la contraseña. Al revés, el desarrollador se habría quedado sin acceso por SSH.

**Endurecimiento** (archivo versionado: `deploy/ssh/00-otalink.conf`, instalado en `/etc/ssh/sshd_config.d/00-otalink.conf`):

```
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
MaxAuthTries 3
```

El archivo se llama `00-...` porque en SSH **gana el primer valor que encuentra** y los archivos se leen en orden alfabético. Se validó con `sudo sshd -t` antes de aplicar. Verificación: entrar con llave funciona y `ssh -o PubkeyAuthentication=no ...` responde `Permission denied`.

**Respaldo de la llave privada:** el archivo `C:\Users\Angel\.ssh\id_ed25519` es la única forma de entrar por SSH. Debe guardarse en un lugar seguro. Si se pierde, se entra por la consola de Hyper-V y se instala una llave nueva.

### 5.4 Firewall UFW

**Qué es:** un firewall sencillo que decide qué conexiones **entrantes** acepta la VM.

**Política:** negar todo lo entrante por defecto y abrir solo lo necesario. Lo saliente (apt, descargas) sigue permitido.

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw limit from 192.168.50.0/24 to any port 22 proto tcp comment 'SSH desde red OtaLink'
sudo ufw allow from 192.168.50.0/24 to any port 80 proto tcp comment 'HTTP desde red OtaLink'
sudo ufw enable
sudo ufw status verbose
```

Estado final de las reglas:

| Puerto | Acción | Desde | Para qué |
|---|---|---|---|
| 22/tcp | LIMIT | 192.168.50.0/24 | SSH (limit bloquea IPs con demasiadas conexiones seguidas) |
| 80/tcp | ALLOW | 192.168.50.0/24 | Nginx |

**Orden crítico:** la regla de SSH se añadió **antes** de activar el firewall; activarlo sin ella corta la sesión. Después, cuando la red tuvo IP fija, se creó primero la regla restringida a la subred y **luego** se borró la regla general.

**Nunca se abren:** 5432 (PostgreSQL), 5000 y 5100 (servicios .NET).

**Rescate:** desde la consola de Hyper-V, `sudo ufw disable`.

---

## 6. Red

### 6.1 Conceptos

| Término | Significado |
|---|---|
| NAT | El anfitrión "presta" su conexión a la VM, que queda oculta detrás de él |
| Red en puente | La VM aparece como un dispositivo más de tu red doméstica |
| IP privada | Direcciones internas (192.168.x.x), no accesibles desde Internet |
| IP pública | La que ve Internet; la tiene el router, no la VM |
| DNS | Traduce nombres (ubuntu.com) a IPs |
| Redirección de puertos | Regla del router que envía tráfico externo a un equipo interno |

### 6.2 El problema y la solución

**Problema:** el *Default Switch* de Hyper-V cambia de subred cada vez que Windows reinicia, así que la IP de la VM no era fiable. Tampoco se podía reservar una IP en el router, porque es del proveedor.

**Solución:** crear un switch virtual **propio** con subred fija y darle NAT. La red doméstica del desarrollador es `192.168.1.0/24`; se eligió `192.168.50.0/24` para no chocar.

| Elemento | Valor |
|---|---|
| Switch de Hyper-V | `OtaLink-NAT` (interno) |
| IP de Windows en ese switch | `192.168.50.1` (puerta de enlace de la VM) |
| NAT de Windows | `OtaLinkNAT`, `192.168.50.0/24` |
| IP fija de la VM | `192.168.50.10/24` |
| DNS de la VM | `1.1.1.1`, `8.8.8.8` (el anfitrión no ofrece DNS) |

Comandos (**WINDOWS**, PowerShell como administrador):

```powershell
New-VMSwitch -SwitchName "OtaLink-NAT" -SwitchType Internal
New-NetIPAddress -IPAddress 192.168.50.1 -PrefixLength 24 -InterfaceAlias "vEthernet (OtaLink-NAT)"
New-NetNat -Name "OtaLinkNAT" -InternalIPInterfaceAddressPrefix 192.168.50.0/24
```

Luego se apagó la VM (`sudo shutdown now`), se conectó al switch nuevo (`Connect-VMNetworkAdapter -VMName "OtaLink-Server" -SwitchName "OtaLink-NAT"`) y se fijó la IP desde Configuración → Red del escritorio de Ubuntu.

**Verificación:** `ip -4 addr show eth0` muestra `192.168.50.10/24`, `ip route` muestra `default via 192.168.50.1`, y `ping ubuntu.com` responde. Al entrar por SSH con la IP nueva se comparó la huella de la VM con la anterior: coincidía, así que era la misma máquina.

### 6.3 Consecuencia importante

Esta red **no es alcanzable desde otros dispositivos** (celulares, otros PCs), ni desde Internet. Es seguro por defecto, pero implica que para mostrar OtaLink a otras personas hará falta, más adelante, un **túnel** (el router no se puede configurar para redirigir puertos). Se evaluará con sus condiciones y costos antes de elegir uno.

### 6.4 Nombres de desarrollo

Para probar con nombres en lugar de IP se añadió una línea al archivo `hosts` de Windows (`C:\Windows\System32\drivers\etc\hosts`):

```
192.168.50.10 otalink.test api.otalink.test
```

`.test` es un dominio reservado que nunca existirá en Internet: ideal para desarrollo. Cuando haya un dominio real, el nombre se cambia en un solo sitio (la configuración de Nginx).

---

## 7. Entorno de servidor

### 7.1 .NET

**Decisión:** en la VM solo se instaló el **runtime de ASP.NET Core 10**, no el SDK.

| | Runtime | SDK |
|---|---|---|
| Para qué | Ejecutar apps ya publicadas | Compilar, crear proyectos |
| Tamaño y superficie de ataque | Menores | Mayores |

El flujo es: **compilar y publicar en Windows → copiar a la VM → ejecutar con el runtime**. Windows ya tiene el SDK 10.0.400.

```bash
sudo apt-get install -y aspnetcore-runtime-10.0     # VM
dotnet --list-runtimes                              # comprobar
```

Resultado: ASP.NET Core 10.0.12 y .NET 10.0.12, desde el repositorio de Ubuntu (Microsoft ya no publica .NET para Ubuntu en su propio repositorio, según su documentación).

**Prueba del ciclo completo:** se creó una aplicación mínima, se publicó en Windows para Linux (`dotnet publish -c Release -r linux-x64 --self-contained false`), se copió con `scp -r`, se le dio permiso de ejecución (`chmod +x`) y se ejecutó con `ASPNETCORE_URLS=http://127.0.0.1:5099`. Un `curl` desde la VM devolvió `Hola desde .NET 10.0.12 en Ubuntu 26.04.1 LTS`. Luego se borró.

`127.0.0.1` significa "solo esta máquina": nada de fuera puede llegar a ese puerto.

### 7.2 PostgreSQL

**Versión:** PostgreSQL 18.6, paquete `postgresql` del repositorio de Ubuntu. Se prefirió sobre el repositorio oficial de PostgreSQL (PGDG) por simplicidad: una fuente menos en la que confiar. Verificado que escucha solo en `127.0.0.1:5432` (`ss -tln | grep 5432`).

**Conceptos:**

- **Rol:** un usuario de la base de datos (distinto de un usuario de Linux).
- **`postgres`:** el superusuario. La aplicación **nunca** lo usa; solo se emplea para administrar con `sudo -u postgres`.
- **Autenticación `peer`:** por el socket local, PostgreSQL confía en el usuario de Linux.

**Diseño de acceso (dos roles, no uno):**

| Rol | Uso | Permisos |
|---|---|---|
| `otalink_migrator` | Aplicar migraciones de EF Core, manualmente y con revisión | Dueño de la base y del esquema `public` |
| `otalink_app` | La API en ejecución | Solo `SELECT/INSERT/UPDATE/DELETE` en filas y `USAGE/SELECT` en secuencias |

**Por qué dos:** si alguien explotara una vulnerabilidad de la API, no podría ejecutar `DROP TABLE` ni alterar el esquema. Ninguno es superusuario. La base se llama `otalink_dev`.

**Contraseñas:** se generaron aleatorias en la VM y se guardaron en `/etc/otalink/db-migrator-password` y `/etc/otalink/db-app-password`, con permisos `600` y propietario `root`, **fuera de Git**. El script SQL no contiene contraseñas: las recibe como variables de `psql`.

Script versionado: `deploy/postgres/01-create-database.sql`.

**Comprobación de permisos (se hizo):**

| Prueba | Resultado |
|---|---|
| `otalink_app` se autentica | `current_user = otalink_app` |
| `otalink_app` crea una tabla | `permission denied for schema public` |
| `otalink_migrator` crea una tabla | Correcto |
| `otalink_app` inserta y lee en esa tabla | Correcto |
| `otalink_app` borra esa tabla | `must be owner of table` |

**Visualización:** se usa **DBeaver** en Windows con un **túnel SSH**: el puerto 5432 no se abre; DBeaver habla con la base a través del canal cifrado de SSH hacia la VM, y desde allí a `127.0.0.1:5432`.

### 7.3 Nginx (proxy inverso)

**Qué es:** un servidor web que recibe las peticiones y las reenvía a los servicios .NET.

**Para qué:** es la única puerta de entrada; los servicios .NET no quedan expuestos; Nginx se encarga de HTTPS (cuando llegue), límites de tamaño y WebSockets de SignalR.

| Nombre | Destino |
|---|---|
| `otalink.test` | Nginx :80 → web en `127.0.0.1:5000` |
| `api.otalink.test` | Nginx :80 → API en `127.0.0.1:5100` (SignalR en `/hubs/`) |
| Cualquier otro host o acceso por IP | Descartado (código 444) |

Archivos versionados en `deploy/nginx/`:

- `conf.d/00-otalink-upgrade-map.conf`: permite WebSockets y conexiones normales.
- `snippets/otalink-proxy.conf`: cabeceras comunes del proxy.
- `sites-available/otalink-web.conf` y `otalink-api.conf`: los dos sitios.
- `sites-available/00-otalink-default.conf`: rechaza lo desconocido.

Detalles de seguridad aplicados:

- `X-Forwarded-For` se **sobrescribe** con la IP que ve Nginx, para que un cliente no pueda falsear la suya. (La aplicación .NET deberá confiar solo en el proxy local; se hará al crearla.)
- Límites de tamaño: 1 MB en la web, 10 MB en la API (fotos de perfil).
- `server_tokens off` para no anunciar la versión.
- Registros separados por sitio en `/var/log/nginx/`.

**Cómo se aplica (script `deploy/scripts/apply-nginx.sh`):**

```bash
sudo ./apply-nginx.sh ~/nginx
```

Hace una copia de `/etc/nginx`, instala los archivos, valida con `nginx -t` y **solo si valida** recarga; si la validación falla, **restaura la copia anterior y no recarga**. Esto funcionó de verdad: el primer intento falló por una directiva duplicada (error del propio proyecto) y la configuración vieja quedó intacta.

**Verificación:**

- Backend de prueba temporal en el puerto 5000.
- `curl -H "Host: otalink.test" http://127.0.0.1/` devolvió el contenido del backend.
- Pedir por IP sin nombre de host: sin respuesta (444).
- `api.otalink.test` sin API devolvió `503` con `{"error":"service_unavailable"}`.
- El navegador de Windows mostró ambas direcciones.

**HTTPS:** no se configuró todavía. Un certificado autofirmado para desarrollo no aporta; se hará con un dominio real y un certificado válido.

---

## 8. Copias de seguridad

### 8.1 Qué existe

| Elemento | Detalle |
|---|---|
| Qué copia | La base `otalink_dev` en formato *custom* de `pg_dump` |
| Cuándo | Todos los días ~03:00 (`otalink-db-backup.timer`) |
| Si la VM estaba apagada | Se ejecuta al encenderla (`Persistent=true`) |
| Dónde | `/var/backups/otalink/` dentro de la VM (solo el usuario `postgres`, permisos `600`) |
| Retención | 14 días |
| Integridad | Cada `.dump` tiene su `.sha256`; el script valida con `pg_restore --list` antes de aceptar la copia |

**Por qué un temporizador de systemd y no `cron`:** systemd también ejecutará la API y la web; así hay un solo lugar para estado, registros (`journalctl`) y reintentos.

Archivos: `deploy/scripts/otalink-db-backup.sh` (instalado como `/usr/local/sbin/otalink-db-backup`) y `deploy/systemd/otalink-db-backup.{service,timer}`.

Consultar: `systemctl list-timers otalink-db-backup.timer`, `journalctl -u otalink-db-backup.service -n 10`.

### 8.2 La restauración se probó

Una copia que nunca se ha restaurado es una esperanza, no un respaldo. Se hizo así:

1. Crear una tabla con 3 filas en `otalink_dev`.
2. Hacer una copia y comprobar las huellas SHA256: `OK`.
3. Crear una base **aparte** (`otalink_restore_test`) y restaurar en ella.
4. Consultar la tabla restaurada: las 3 filas volvieron.
5. Borrar la base de pruebas y la tabla.

Se restaura en una base nueva, **nunca encima** de la que está en uso.

### 8.3 Limitación (importante)

Estas copias están **en el mismo disco** que la base. Protegen contra un error humano (una migración mala, un `DROP` accidental), **no** contra la pérdida del disco, de la VM o del portátil.

**Decisión pendiente:** la copia externa irá a un **Google Drive de una cuenta de Gmail dedicada a la app**, nunca a carpetas del Windows local. Deberá cifrarse antes de subirse (contiene datos de usuarios), con el token de acceso guardado fuera de Git. También deberán respaldarse, cifradas, las contraseñas de `/etc/otalink/`.

---

## 9. Qué hay en el repositorio

```
OtaLink/
├── documentacion/                    ← esta guía narrativa
│   ├── README.md
│   └── fase-01-configuracion-del-entorno.md
├── docs/                             ← referencia técnica viva
│   ├── UBUNTU-GUIDE.md               estado real del servidor
│   ├── NETWORK.md                    red, IP, puertos, incidente
│   ├── DATABASE.md                   base, roles, permisos
│   └── BACKUP-RESTORE.md             copias y restauración
└── deploy/                           ← configuración versionada del servidor
    ├── ssh/00-otalink.conf           SSH endurecido
    ├── postgres/01-create-database.sql
    ├── nginx/                        conf.d, snippets, sites-available
    ├── systemd/                      servicio y temporizador de respaldos
    └── scripts/
        ├── otalink-db-backup.sh
        └── apply-nginx.sh
```

**Lo que NO está en Git, a propósito:** las contraseñas de la base (`/etc/otalink/` en la VM) y la llave SSH privada (en `~/.ssh` de Windows).

---

## 10. Problemas encontrados y lecciones

Esta sección es parte de lo que enseña la fase. Cada problema tuvo una causa concreta.

| # | Síntoma | Causa real | Lección |
|---|---|---|---|
| 1 | SSH pedía contraseña tras "instalar" la llave | La llave nunca se había creado (el `dir` mostró que faltaba) | Verificar cada paso antes de pasar al siguiente |
| 2 | `ssh` no aceptaba `-debug` | Opciones largas llevan dos guiones (`--debug`) | `--largo`, `-c` corto |
| 3 | `tail-5: orden no encontrada` | Faltaba un espacio | Los espacios importan |
| 4 | `psql: error: ... Permiso denegado` al ejecutar el script | `psql -f` lo lee el usuario `postgres`, que no puede entrar en `/home/angel-diaz` | Usar `< archivo`: lo lee **tu** terminal. No aflojar permisos de tu carpeta |
| 5 | Una contraseña de la base apareció en el chat | `sudo cat` la imprimió en pantalla y se pegó | Una credencial pegada en un chat se considera expuesta: se **rotó** (`ALTER ROLE ... PASSWORD`). No mostrar secretos en pantalla |
| 6 | `curl: orden no encontrada` | Ubuntu 26.04 no lo trae | `sudo apt install -y curl` |
| 7 | Comando de la VM ejecutado en Windows (`head: no se reconoce`) | Confusión de terminal | Cada bloque indica **dónde** se ejecuta |
| 8 | **Cortes intermitentes de red** | Ver abajo | Diagnosticar con registros antes de suponer |
| 9 | `nginx -t`: `"proxy_read_timeout" directive is duplicate` | La directiva estaba en el snippet y otra vez en `/hubs/` | El script de aplicación detectó el fallo y restauró la configuración vieja sin recargar |

### Detalle del problema 8 (el más instructivo)

**Síntomas:** `Connection timed out`, `Connection reset` durante `apt`, pings que fallaban y luego volvían.

**Primera hipótesis (incorrecta):** la VM se suspendía. Se revisaron los registros y **no había ninguna suspensión real**. La hipótesis se descartó con evidencia.

**Causa real:** el registro de NetworkManager mostraba un bucle: `dhcp4 ... no lease` → `ip-config-unavailable` → `DISCONNECTED` → reintento cada 45 s. `eth0` tenía **IP fija y DHCP a la vez**:

- `/etc/netplan/00-installer-config.yaml` (del instalador) tenía `dhcp4: true`.
- `/etc/netplan/90-NM-*.yaml` (generado al fijar la IP desde la interfaz gráfica) no decía `dhcp4: false`.
- Netplan **combina** los archivos, así que el resultado era IP fija más DHCP. El switch `OtaLink-NAT` no tiene servidor DHCP, así que NetworkManager esperaba 45 s en vano, fallaba y tiraba la conexión.

**Corrección:**

```bash
sudo mv /etc/netplan/00-installer-config.yaml /etc/netplan/00-installer-config.yaml.disabled
sudo netplan generate
sudo netplan apply
```

Netplan solo lee archivos que acaban en `.yaml`, así que renombrar es reversible.

**Estado: observación abierta.** Tras la corrección la IP y la ruta eran correctas, pero **no se completó** la prueba limpia (reinicio y conteo de fallos de NetworkManager desde el arranque). El conteo desde el arranque incluía fallos anteriores al arreglo y no permitió concluir. El desarrollador decidió continuar y avisar si los cortes reaparecen. Si vuelve a pasar:

```bash
journalctl -u NetworkManager -b --no-pager | grep -E "failed|no lease" | tail -5
date +%H:%M:%S
```

Comparar la hora del último fallo con la hora actual: si es reciente, el problema sigue.

---

## 11. Decisiones y alternativas descartadas

| Decisión | Alternativa descartada | Por qué |
|---|---|---|
| Hyper-V | VirtualBox | Ya activo; evita dos hipervisores |
| Ubuntu 26.04 LTS | 24.04 LTS | Es la LTS vigente y .NET 10 está soportado |
| Switch propio con NAT e IP fija | Default Switch; red en puente | El Default Switch cambia de subred; el puente por Wi-Fi es incómodo y el router no se puede configurar |
| Runtime en la VM, SDK en Windows | SDK en la VM | Servidor más limpio; flujo de despliegue realista |
| PostgreSQL del repositorio de Ubuntu | Repositorio PGDG | Una fuente menos que mantener |
| Dos roles de base de datos | Un solo rol | Si la API es comprometida, no puede alterar el esquema |
| SSH solo con llave | Contraseña | No se puede adivinar por fuerza bruta |
| Temporizador de systemd | `cron` | Un solo lugar para estado y registros |
| **No instalar Cockpit (por ahora)** | Instalarlo | Entra con la contraseña del usuario (contradice SSH solo con llave) y abre el puerto 9090. Se reevaluará con servicios .NET; si se instala, solo accesible desde `192.168.50.0/24`, nunca desde Internet |
| Sin Docker | Docker | No aporta una ventaja real para este escenario; sumaría una capa más que aprender y asegurar |

---

## 12. Estado final y pendientes

**Verificado y funcionando:**

- [x] VM Ubuntu 26.04.1 LTS con checkpoints 01 a 03
- [x] Hora y actualizaciones automáticas de seguridad
- [x] SSH solo con llave; contraseña y `root` desactivados
- [x] UFW activo, con SSH y HTTP restringidos a `192.168.50.0/24`
- [x] IP fija `192.168.50.10`
- [x] Runtime .NET 10.0.12 probado con una aplicación real
- [x] PostgreSQL 18.6 con roles limitados, permisos comprobados
- [x] Copia diaria automática, restauración probada
- [x] Nginx como proxy inverso, probado desde el navegador

**Pendiente o no verificado del todo:**

- [ ] Confirmar la estabilidad de la red (sección 10)
- [ ] Checkpoints 04 y 05 (recomendados)
- [ ] Servicios `systemd` de la web y la API (se harán con el primer despliegue real)
- [ ] Copia externa cifrada a Google Drive (cuenta dedicada)
- [ ] Documentar la recuperación en otra máquina
- [ ] HTTPS, dominio y acceso desde Internet (túnel; el router no es configurable)
- [ ] Cockpit (reevaluar)
- [ ] Base de datos de producción separada de `otalink_dev`

---

## 13. Chuleta de comandos usados

**Linux (en la VM)**

| Comando | Para qué |
|---|---|
| `pwd`, `ls -la`, `cd` | Dónde estoy, qué hay, moverme |
| `mkdir`, `cp`, `mv`, `rm -r` | Crear, copiar, mover, borrar (**`rm` no tiene papelera**) |
| `cat`, `nano` | Ver y editar archivos |
| `sudo` | Ejecutar un comando como administrador |
| `apt update / install / upgrade` | Gestionar software |
| `systemctl status / start / enable` | Gestionar servicios |
| `journalctl -u <servicio> -n 20` | Ver los registros de un servicio |
| `ip -4 addr show`, `ip route` | Ver IP y rutas |
| `ss -tln` | Ver puertos abiertos |
| `curl -si <url>` | Probar HTTP |
| `df -h`, `free -h` | Espacio en disco y memoria |
| `ufw status verbose` | Estado del firewall |
| `nginx -t` | Validar la configuración de Nginx |

**Windows (PowerShell)**

| Comando | Para qué |
|---|---|
| `ssh angel-diaz@192.168.50.10` | Entrar a la VM |
| `scp -r <origen> angel-diaz@192.168.50.10:~/` | Copiar archivos a la VM |
| `Get-FileHash <archivo> -Algorithm SHA256` | Verificar una descarga |
| `Test-NetConnection 192.168.50.10 -Port 80` | Probar un puerto |
| `Get-VM`, `Start-VM`, `Connect-VMNetworkAdapter` | Gestionar la VM |

---

## 14. Cómo recuperarse si algo se rompe

| Situación | Qué hacer |
|---|---|
| No entro por SSH | Abre la consola de Hyper-V (`vmconnect localhost OtaLink-Server`) y revisa/ajusta desde allí |
| Firewall me bloquea | Consola de Hyper-V: `sudo ufw disable`, corregir, `sudo ufw enable` |
| SSH mal configurado | Consola: `sudo rm /etc/ssh/sshd_config.d/00-otalink.conf && sudo systemctl restart ssh` |
| Perdí la llave SSH | Consola: instalar una nueva en `~/.ssh/authorized_keys` |
| La VM sin red | Consola: revisar Configuración → Red; o `Connect-VMNetworkAdapter ... -SwitchName "Default Switch"` y pasar IPv4 a Automático |
| Nginx rechaza la configuración | `apply-nginx.sh` ya restaura la copia; las previas están en `/var/backups/nginx-config-*` |
| Una prueba rompió el sistema | Restaurar el último checkpoint de Hyper-V (solo cubre cambios recientes; no es un respaldo) |
| Se perdió la base | Restaurar la última copia de `/var/backups/otalink/` en una base nueva (sección 8.2) |
| Se perdió el disco o el portátil | **Sin copia externa todavía, no hay recuperación.** Por eso la copia a Drive es prioritaria |

---

*Siguiente fase: solución .NET por capas y primer flujo vertical (registro, perfil e intereses). Antes de escribir código se decide el modo de renderizado de Blazor.*
