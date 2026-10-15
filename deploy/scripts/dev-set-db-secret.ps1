# Guarda la cadena de conexion de DESARROLLO en "User Secrets" (fuera del repositorio).
#
# Pide la contrasena del rol otalink_app sin mostrarla en pantalla y la guarda cifrada por
# Windows en tu perfil de usuario (%APPDATA%\Microsoft\UserSecrets\otalink-api-dev).
# Nada de esto se sube a Git.
#
# Uso:  powershell -File deploy\scripts\dev-set-db-secret.ps1

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\..\src\OtaLink.Api'

$secure = Read-Host -AsSecureString 'Contrasena del rol otalink_app (la del archivo /etc/otalink/db-app-password en la VM)'
$bstr   = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $plain = $plain.Trim()

    # Las contrasenas de la base son aleatorias, solo letras y numeros, de 40+ caracteres.
    # Si el pegado trae saltos de linea, restos de pantalla o quedo incompleto, no se guarda nada.
    if ($plain -notmatch '^[A-Za-z0-9]{32,}$') {
        throw "El valor pegado no tiene el formato esperado (longitud $($plain.Length); se esperan solo letras y numeros, 32 o mas). Vuelve a copiarla, sin espacios ni saltos de linea."
    }

    $connection = "Host=127.0.0.1;Port=15432;Database=otalink_dev;Username=otalink_app;Password=$plain"
    dotnet user-secrets set 'Database:ConnectionString' $connection --project $project | Out-Null
    Write-Host 'Listo: Database:ConnectionString guardada en User Secrets.'
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}
