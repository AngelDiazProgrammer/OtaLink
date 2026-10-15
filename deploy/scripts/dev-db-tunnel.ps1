# Abre un tunel SSH desde WINDOWS hasta la PostgreSQL de la VM, SOLO para desarrollo.
#
#   localhost:15432 (Windows)  --SSH-->  127.0.0.1:5432 (dentro de la VM)
#
# El puerto 5432 de la VM sigue cerrado para todo lo demas: la base nunca se expone a la red.
# Deja esta ventana de PowerShell abierta mientras trabajas; Ctrl+C cierra el tunel.
#
# Uso:  powershell -File deploy\scripts\dev-db-tunnel.ps1

$ErrorActionPreference = 'Stop'

$vm        = 'angel-diaz@192.168.50.10'
$localPort = 15432

Write-Host "Abriendo tunel: localhost:$localPort -> $vm (PostgreSQL de la VM)"
Write-Host "Te pedira la frase de paso de tu llave SSH. Ctrl+C para cerrar el tunel."

# -N: no ejecutar comandos, solo reenviar el puerto.
# ExitOnForwardFailure: si el puerto local esta ocupado, falla en vez de quedarse "colgado".
ssh -N -L "127.0.0.1:${localPort}:127.0.0.1:5432" -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 $vm
