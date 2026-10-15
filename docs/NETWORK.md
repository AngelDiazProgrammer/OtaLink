# NETWORK

Estado real de la red del entorno. Solo se documenta lo verificado.

## Topología actual (verificada el 2026-10-15)

```
Internet -- Router del proveedor (192.168.1.1, sin acceso de configuración)
              |
           Wi-Fi del portátil (192.168.1.9/24)   <- Windows 11 (anfitrión)
              |
        vEthernet (OtaLink-NAT) 192.168.50.1/24  <- puerta de enlace de la VM
              |   NAT de Windows (OtaLinkNAT, 192.168.50.0/24)
        OtaLink-Server (Ubuntu) 192.168.50.10/24
```

| Elemento | Valor |
|---|---|
| Switch de Hyper-V | `OtaLink-NAT` (interno) |
| NAT de Windows | `OtaLinkNAT`, `192.168.50.0/24` |
| IP del anfitrión en el switch | `192.168.50.1` |
| IP fija de la VM | `192.168.50.10` (manual, desde Configuración → Red) |
| Puerta de enlace de la VM | `192.168.50.1` |
| DNS de la VM | `1.1.1.1`, `8.8.8.8` (el anfitrión no ofrece DNS) |

Se dejó de usar el *Default Switch* porque cambia de subred al reiniciar Windows.

## Verificación

- La VM muestra `192.168.50.10/24` y `default via 192.168.50.1`.
- `ping ubuntu.com` desde la VM responde.
- `ssh angel-diaz@192.168.50.10` desde Windows entra; la huella ED25519 coincide con la de la IP anterior.

## Incidente: cortes intermitentes (2026-10-15)

Síntoma: `ssh` con `Connection timed out`, `Connection reset` durante `apt`, pings que fallan y luego vuelven.

Causa (diagnosticada con `journalctl -u NetworkManager`): `eth0` tenía DHCP y a la vez IP fija.
- `/etc/netplan/00-installer-config.yaml` (instalador) tenía `dhcp4: true`.
- `/etc/netplan/90-NM-*.yaml` (generado al fijar la IP desde la interfaz gráfica) no decía `dhcp4: false`.
- Netplan combina ambos: el switch `OtaLink-NAT` no tiene servidor DHCP, así que NetworkManager esperaba 45 s, fallaba (`ip-config-unavailable`), tiraba la conexión y repetía.

Corrección: se renombró el archivo del instalador a `00-installer-config.yaml.disabled` (Netplan solo lee `*.yaml`) y se aplicó `netplan apply`.
Revertir: renombrarlo de vuelta a `.yaml` y `sudo netplan apply`.

Estado: IP y ruta correctas tras la corrección. **Observación pendiente**: no se ha confirmado con un reinicio y un conteo limpio de fallos de NetworkManager; el usuario avisará si los cortes reaparecen.

## Nombres y puertos (desarrollo)

| Nombre | Destino | Notas |
|---|---|---|
| `otalink.test` | Nginx :80 → web `127.0.0.1:5000` | Solo en el `hosts` de Windows |
| `api.otalink.test` | Nginx :80 → API `127.0.0.1:5100` | Ídem; SignalR en `/hubs/` |
| Cualquier otro host / IP | Descartado (444) | `default_server` |

- Entrada añadida en `C:\Windows\System32\drivers\etc\hosts`: `192.168.50.10 otalink.test api.otalink.test`.
- UFW: `22/tcp` (limitado) y `80/tcp` solo desde `192.168.50.0/24`. 5000, 5100 y 5432 no se abren.

## Acceso desde fuera

- La VM no es alcanzable desde otros dispositivos de la red doméstica ni desde Internet.
- No hay acceso al router, así que no se pueden hacer redirecciones de puertos.
- Para acceso público se evaluará un túnel (Fase 7).

## Revertir

- Volver al switch anterior: `Connect-VMNetworkAdapter -VMName "OtaLink-Server" -SwitchName "Default Switch"` (la VM tendría IP manual: pasar IPv4 a Automático).
- Eliminar lo creado: `Remove-NetNat -Name "OtaLinkNAT"` y `Remove-VMSwitch -Name "OtaLink-NAT"`.
