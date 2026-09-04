<#
.SYNOPSIS
    Pone al dia el registro de Entra de Task Manager: cuentas personales y la
    redireccion de loopback que usa la aplicacion de verdad.

.DESCRIPTION
    Se ejecuta cuando hace falta, no en cada entrega. Arregla las dos cosas que
    impedian entrar con Microsoft el 2026-09-03:

    1. AUDIENCIA. Registrar-Entra.ps1 creo el registro como 'AzureADMultipleOrgs',
       que es "cualquier empresa" pero NINGUNA cuenta personal. Entrando con una
       cuenta de Outlook/Hotmail/Live, Entra responde:

           unauthorized_client: The client does not exist or is not enabled for
           consumers.

       Se pasa a 'AzureADandPersonalMicrosoftAccount', que es el superconjunto:
       cualquier organizacion Y las cuentas personales.

    2. REDIRECCION. La aplicacion vuelve a http://127.0.0.1:<puerto>/auth/ (el
       servidor local de un solo uso, igual en Windows y en Android), y el
       registro solo tenia http://localhost, sin ruta. En las direcciones de
       loopback Entra IGNORA EL PUERTO, pero la RUTA la compara byte a byte: sin
       /auth/ el siguiente error habria sido AADSTS50011.

       Ademas, una redireccion http con 127.0.0.1 NO se puede añadir desde el
       portal de Azure (solo por Graph o editando el manifiesto), que es la otra
       razon de que esto sea un script.

    Como entra
    ----------
    Por codigo de dispositivo, igual que Registrar-Entra.ps1: sales a
    https://microsoft.com/devicelogin, escribes el codigo y apruebas con una
    cuenta que pueda modificar el registro (su dueño o un administrador del
    directorio donde se creo).

.EXAMPLE
    .\Actualizar-Entra.ps1
    .\Actualizar-Entra.ps1 -ClientId b5ceecca-0000-0000-0000-000000000000
#>

[CmdletBinding()]
param(
    # Por defecto se lee de oauth.local.props, que es donde vive el valor real y
    # que no esta en el repositorio.
    [string]$ClientId,

    # Donde se creo el registro. 'organizations' vale para cualquier cuenta de
    # empresa; se puede fijar el dominio o el id del tenant.
    [string]$Tenant = 'organizations',

    # La que faltaba. El puerto se ignora en loopback, la ruta no.
    [string[]]$Redirecciones = @('http://127.0.0.1/auth/'),

    [string]$Audiencia = 'AzureADandPersonalMicrosoftAccount'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- 0. El client_id
if (-not $ClientId) {
    $props = Join-Path (Split-Path $PSScriptRoot -Parent) 'oauth.local.props'
    if (-not (Test-Path $props)) {
        throw "No hay oauth.local.props; pasa el registro con -ClientId."
    }

    $ClientId = ([xml](Get-Content $props -Raw)).Project.PropertyGroup.TmMicrosoftClientId
    if (-not $ClientId) {
        throw "oauth.local.props no trae TmMicrosoftClientId; pasalo con -ClientId."
    }
}

Write-Host "Registro: $ClientId" -ForegroundColor Cyan

# ---------------------------------------------------------------- 1. Codigo de dispositivo
$ClienteCli = '04b07795-8ddb-461a-bbee-02f9e1bf7b46'   # Azure CLI, preconsentido
$Autoridad  = "https://login.microsoftonline.com/$Tenant/oauth2/v2.0"

$codigo = Invoke-RestMethod -Method Post -Uri "$Autoridad/devicecode" -Body @{
    client_id = $ClienteCli
    scope     = 'https://graph.microsoft.com/.default offline_access'
}

Write-Host ''
Write-Host '  1. Abre: ' -NoNewline; Write-Host $codigo.verification_uri -ForegroundColor Yellow
Write-Host '  2. Codigo: ' -NoNewline; Write-Host $codigo.user_code -ForegroundColor Yellow
Write-Host '  3. Entra con la cuenta dueña del registro (o un administrador).'
Write-Host ''
Write-Host 'Esperando a que apruebes...' -ForegroundColor Cyan

$limite = (Get-Date).AddSeconds([int]$codigo.expires_in)
$token = $null

while ($null -eq $token -and (Get-Date) -lt $limite) {
    Start-Sleep -Seconds ([int]$codigo.interval)

    try {
        $token = (Invoke-RestMethod -Method Post -Uri "$Autoridad/token" -Body @{
            grant_type  = 'urn:ietf:params:oauth:grant-type:device_code'
            client_id   = $ClienteCli
            device_code = $codigo.device_code
        }).access_token
    }
    catch {
        # El cuerpo se mira COMO TEXTO, sin ConvertFrom-Json: ese cmdlet revienta de forma
        # terminante —y -ErrorAction no lo tapa— en cuanto la respuesta no es JSON, y entonces la
        # excepcion sale del propio catch y mata el sondeo. Paso: el script se caia en el primer
        # intento y la aprobacion del usuario no la recogia nadie.
        $texto = ''
        try { $texto = [string]$_.ErrorDetails.Message } catch { }
        if (-not $texto) { $texto = [string]$_.Exception.Message }

        # Los dos unicos «todavia no» del flujo de codigo de dispositivo.
        if ($texto -notmatch 'authorization_pending' -and $texto -notmatch 'slow_down') {
            throw "Entra ha rechazado la entrada: $texto"
        }
    }
}

if (-not $token) { throw 'Se agoto el tiempo sin que se aprobara la entrada.' }

Write-Host 'Dentro.' -ForegroundColor Green
$cabeceras = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }

# ---------------------------------------------------------------- 2. Quien ha entrado
# Se pregunta antes de tocar nada porque el fallo tipico no se entiende solo: entrando con una
# cuenta que no vive en el directorio donde se creo el registro, Graph contesta
# «Authentication_Unauthorized: User was not found» —que suena a que falta el REGISTRO, cuando lo
# que falta es la CUENTA— y ahi se acaba el hilo.
try {
    $yo = Invoke-RestMethod -Method Get -Uri 'https://graph.microsoft.com/v1.0/me' -Headers $cabeceras
    Write-Host "Cuenta: $($yo.userPrincipalName)" -ForegroundColor Cyan
}
catch {
    Write-Host ''
    Write-Host 'Esa cuenta no existe en el directorio contra el que se ha entrado.' -ForegroundColor Red
    Write-Host "Se entro contra «$Tenant». El registro se creo en el directorio de la cuenta que lo"
    Write-Host 'creo: entra con ESA cuenta, o fija el directorio a mano, por ejemplo:'
    Write-Host '    .\Actualizar-Entra.ps1 -Tenant tuempresa.com' -ForegroundColor Yellow
    throw
}

# ---------------------------------------------------------------- 3. Como esta ahora
$url = "https://graph.microsoft.com/v1.0/applications(appId='$ClientId')"

try {
    $app = Invoke-RestMethod -Method Get -Uri $url -Headers $cabeceras
}
catch {
    Write-Host ''
    Write-Host "En este directorio no se ve el registro $ClientId." -ForegroundColor Red
    Write-Host 'O esta en otro directorio (usa -Tenant), o esa cuenta no puede verlo.'
    throw
}

Write-Host ''
Write-Host 'Antes:' -ForegroundColor Cyan
Write-Host "  Nombre        : $($app.displayName)"
Write-Host "  Audiencia     : $($app.signInAudience)"
Write-Host "  Redirecciones : $($app.publicClient.redirectUris -join ', ')"

# ---------------------------------------------------------------- 4. Ponerlo al dia
# Se AÑADEN las que faltan; las que ya estaban se quedan, que puede haber otra
# aplicacion o una prueba colgando de ellas.
$todas = @($app.publicClient.redirectUris) + $Redirecciones | Where-Object { $_ } | Select-Object -Unique

$cambio = @{
    signInAudience = $Audiencia
    publicClient   = @{ redirectUris = @($todas) }
} | ConvertTo-Json -Depth 5

try {
    Invoke-RestMethod -Method Patch -Uri $url -Headers $cabeceras -Body $cambio | Out-Null
}
catch {
    $detalle = $_.ErrorDetails.Message
    Write-Host ''
    Write-Host 'Graph ha rechazado el cambio:' -ForegroundColor Red
    Write-Host $detalle

    # Lo mas probable: un permiso que las cuentas personales no admiten (el de
    # Exchange, que es del correo y esta escondido). Se dice, no se borra solo.
    Write-Host ''
    Write-Host 'Si se queja de los permisos, quita el de Office 365 Exchange Online'
    Write-Host '(00000002-0000-0ff1-ce00-000000000000) en el portal y vuelve a ejecutarlo:'
    Write-Host 'es del lector de correo, que hoy esta oculto.'
    throw
}

$app = Invoke-RestMethod -Method Get -Uri $url -Headers $cabeceras

Write-Host ''
Write-Host 'Despues:' -ForegroundColor Green
Write-Host "  Audiencia     : $($app.signInAudience)"
Write-Host "  Redirecciones : $($app.publicClient.redirectUris -join ', ')"
Write-Host ''
Write-Host 'El cambio tarda un minuto en propagarse. Si la primera prueba falla, espera y repite.'
Write-Host ''
Write-Host 'Ojo con Supabase: el proveedor «azure» tiene que aceptar tambien las cuentas' -ForegroundColor Yellow
Write-Host 'personales (URL del tenant en «common»). Si no, se entra igual pero no sincroniza.' -ForegroundColor Yellow
