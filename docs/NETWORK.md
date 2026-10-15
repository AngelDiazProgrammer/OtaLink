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

## Acceso desde fuera

- La VM no es alcanzable desde otros dispositivos de la red doméstica ni desde Internet.
- No hay acceso al router, así que no se pueden hacer redirecciones de puertos.
- Para acceso público se evaluará un túnel (Fase 7).

## Revertir

- Volver al switch anterior: `Connect-VMNetworkAdapter -VMName "OtaLink-Server" -SwitchName "Default Switch"` (la VM tendría IP manual: pasar IPv4 a Automático).
- Eliminar lo creado: `Remove-NetNat -Name "OtaLinkNAT"` y `Remove-VMSwitch -Name "OtaLink-NAT"`.
