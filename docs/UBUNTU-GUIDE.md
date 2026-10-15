# UBUNTU-GUIDE

Estado real del servidor de OtaLink. Solo se documenta lo que ya se hizo y se verificó.

## Anfitrión (verificado el 2026-10-15)

- Windows 11 Pro (build 26200), portátil.
- Intel i7-9750H (6 núcleos / 12 hilos), 47,8 GB de RAM.
- Disco C: NVMe 477 GB, disco D: SSD 447 GB (la VM vive en D:).
- Red: solo Wi-Fi. Router del proveedor, sin acceso de configuración.
- Hipervisor: Hyper-V (ya activo en el anfitrión).

## Máquina virtual

| Campo | Valor |
|---|---|
| Nombre en Hyper-V | `OtaLink-Server` |
| Ubicación | `D:\VMs\OtaLink` |
| Generación | 2 (Secure Boot: Microsoft UEFI Certificate Authority) |
| CPU / RAM | 4 vCPU / 12 GB fijos |
| Disco | 100 GB dinámico (`.vhdx`) |
| Red | Default Switch (NAT) por ahora |
| Hostname | `otalink-server` |
| Usuario administrador | `angel-diaz` |

## Sistema operativo

- Ubuntu 26.04.1 LTS (Resolute Raccoon), ISO oficial.
- SHA256 de la ISO verificado contra `releases.ubuntu.com/26.04/SHA256SUMS`:
  `601e30fbf5d97759367c632e2c33630665039b7e2158fd068403da3ccf1bda1f`.
- .NET 10 se instala desde los repositorios de Ubuntu (`dotnet-sdk-10.0`), no desde el repositorio de Microsoft.
- Espacio tras instalar y actualizar: 97 GB de disco, 82 GB libres.

## Checkpoints

- `01-ubuntu-limpio`: Ubuntu recién instalado y actualizado.
  Un checkpoint no es una copia de seguridad: vive junto al disco de la VM.

## Configuración aplicada y verificada

- Zona horaria `America/Bogota`, reloj sincronizado por NTP.
- `unattended-upgrades` activo (parches de seguridad automáticos).
- SSH (`openssh-server`, activación por socket `ssh.socket`).
  - IP de la VM al configurar: `172.29.209.104` (dinámica, Default Switch).
  - Acceso solo con llave ED25519 (con passphrase) desde Windows.
  - Contraseña desactivada y `root` bloqueado: ver [deploy/ssh/00-otalink.conf](../deploy/ssh/00-otalink.conf).
  - Verificado: entra con llave; `ssh -o PubkeyAuthentication=no` responde `Permission denied`.
  - Recuperación si se pierde el acceso: desde la consola de Hyper-V,
    `sudo rm /etc/ssh/sshd_config.d/00-otalink.conf && sudo systemctl restart ssh`.

- Firewall UFW activo y habilitado en el arranque:
  - Entrante: denegado por defecto. Saliente: permitido.
  - Única regla: `22/tcp LIMIT IN` (SSH con limitación de intentos), IPv4 e IPv6.
  - La regla no se restringe por subred porque el Default Switch cambia de subred al reiniciar Windows; se restringirá en la Fase 3 con IP fija.
  - Verificado: nueva sesión SSH entra y hay salida a Internet (`ping ubuntu.com`).
  - Recuperación: desde la consola de Hyper-V, `sudo ufw disable`.

## Pendiente

- IP estable (Fase 3) y restringir la regla de SSH a la subred fija.
- Abrir 80/443 solo cuando exista Nginx.
