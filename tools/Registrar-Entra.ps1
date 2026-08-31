<#
.SYNOPSIS
    Crea en Entra ID el registro de aplicacion que necesita Task Manager.

.DESCRIPTION
    Se ejecuta UNA SOLA VEZ, y solo lo ejecuta quien publica la aplicacion.

    Por que hace falta
    ------------------
    Un client_id no se puede inventar: tiene que corresponder a un registro real.
    Thunderbird no crea un registro en cada empresa —eso es cierto— pero SI tiene
    uno, en el tenant de Mozilla, marcado como multi-tenant. Lo que se ahorra con
    el consentimiento del administrador es crear uno POR CLIENTE, no crear el
    primero.

    Lo que pasa despues, en cada empresa que use la aplicacion, es automatico: al
    entrar por primera vez, Entra crea alli el service principal (la "Enterprise
    Application"). Eso no lo hace este script y no hay que hacerlo a mano.

    Como entra
    ----------
    Por codigo de dispositivo, no con az login: sales a
    https://microsoft.com/devicelogin, escribes un codigo y apruebas con tu
    cuenta. Se usa el cliente publico de Azure CLI, que viene preconsentido en
    todos los tenants; es el mismo camino que usa az por dentro, pero sin
    necesidad de tener az instalado.

    Hace falta ser administrador del directorio (o tener permiso para registrar
    aplicaciones).

.EXAMPLE
    .\Registrar-Entra.ps1
    .\Registrar-Entra.ps1 -Nombre "Task Manager" -Tenant contoso.onmicrosoft.com
#>

[CmdletBinding()]
param(
    [string]$Nombre = 'Task Manager',

    # 'organizations' sirve para cualquier cuenta de empresa. Se puede pasar el
    # dominio o el id del tenant si se quiere fijar donde se crea.
    [string]$Tenant = 'organizations',

    # Redirecciones. La del movil es el esquema propio de la app; la de escritorio
    # es el servidor local de un solo uso que levanta la aplicacion.
    [string[]]$Redirecciones = @('com.socratic.taskmanager://auth', 'http://localhost')
)

$ErrorActionPreference = 'Stop'

# Cliente publico de Azure CLI: preconsentido en todos los tenants, sin secreto.
$ClienteCli = '04b07795-8ddb-461a-bbee-02f9e1bf7b46'
$Autoridad  = "https://login.microsoftonline.com/$Tenant/oauth2/v2.0"

# ---------------------------------------------------------------- 1. Codigo de dispositivo
Write-Host 'Pidiendo codigo de acceso...' -ForegroundColor Cyan

$codigo = Invoke-RestMethod -Method Post -Uri "$Autoridad/devicecode" -Body @{
    client_id = $ClienteCli
    scope     = 'https://graph.microsoft.com/.default offline_access'
}

Write-Host ''
Write-Host '  1. Abre: ' -NoNewline; Write-Host $codigo.verification_uri -ForegroundColor Yellow
Write-Host '  2. Codigo: ' -NoNewline; Write-Host $codigo.user_code -ForegroundColor Yellow
Write-Host '  3. Entra con una cuenta que pueda registrar aplicaciones.'
Write-Host ''
Write-Host 'Esperando a que apruebes...' -ForegroundColor Cyan

# ---------------------------------------------------------------- 2. Esperar el token
$limite = (Get-Date).AddSeconds([int]$codigo.expires_in)
$token = $null

while ($null -eq $token -and (Get-Date) -lt $limite) {
    Start-Sleep -Seconds ([int]$codigo.interval)

    try {
        $respuesta = Invoke-RestMethod -Method Post -Uri "$Autoridad/token" -Body @{
            grant_type  = 'urn:ietf:params:oauth:grant-type:device_code'
            client_id   = $ClienteCli
            device_code = $codigo.device_code
        }
        $token = $respuesta.access_token
    }
    catch {
        # authorization_pending es lo normal mientras no has aprobado todavia.
        # Cualquier otro error si es de verdad y se para aqui.
        $detalle = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue

        if ($detalle -and $detalle.error -ne 'authorization_pending' -and $detalle.error -ne 'slow_down') {
            throw "Entra ha rechazado la entrada: $($detalle.error_description)"
        }
    }
}

if (-not $token) { throw 'Se agoto el tiempo sin que se aprobara la entrada.' }

Write-Host 'Dentro.' -ForegroundColor Green

# ---------------------------------------------------------------- 3. Crear el registro
$cabeceras = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }

$cuerpo = @{
    displayName = $Nombre

    # AzureADMultipleOrgs = cualquier organizacion. Es lo que permite que la use
    # una empresa distinta sin registrar nada alli.
    signInAudience = 'AzureADMultipleOrgs'

    # Cliente publico: la aplicacion vive en el dispositivo del usuario y no puede
    # guardar un secreto, asi que se apoya en PKCE.
    publicClient = @{ redirectUris = $Redirecciones }

    requiredResourceAccess = @(
        @{
            # Microsoft Graph
            resourceAppId = '00000003-0000-0000-c000-000000000000'
            resourceAccess = @(
                @{ id = 'e1fe6dd8-ba31-4d61-89e7-88639da4683d'; type = 'Scope' }  # User.Read
                @{ id = '7427e0e9-2fba-42fe-b0c0-848c9e6a8182'; type = 'Scope' }  # offline_access
            )
        },
        @{
            # Office 365 Exchange Online, para IMAP con XOAUTH2
            resourceAppId = '00000002-0000-0ff1-ce00-000000000000'
            resourceAccess = @(
                @{ id = 'a4b5e23c-3e5b-4f28-9d4a-1a6b0a2fd0a5'; type = 'Scope' }  # IMAP.AccessAsUser.All
            )
        }
    )
} | ConvertTo-Json -Depth 8

Write-Host 'Creando el registro...' -ForegroundColor Cyan

$app = Invoke-RestMethod -Method Post -Uri 'https://graph.microsoft.com/v1.0/applications' `
                         -Headers $cabeceras -Body $cuerpo

Write-Host ''
Write-Host 'Registro creado.' -ForegroundColor Green
Write-Host ''
Write-Host "  Nombre        : $($app.displayName)"
Write-Host "  Client ID     : " -NoNewline; Write-Host $app.appId -ForegroundColor Yellow
Write-Host "  Audiencia     : $($app.signInAudience)"
Write-Host ''
Write-Host 'Pegalo en oauth.local.props:' -ForegroundColor Cyan
Write-Host "  <TmMicrosoftClientId>$($app.appId)</TmMicrosoftClientId>"
Write-Host ''
Write-Host 'A partir de aqui, cada organizacion que use la aplicacion crea su propio'
Write-Host 'service principal al consentir. No hay que registrar nada mas en ningun sitio.'
