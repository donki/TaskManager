<#
.SYNOPSIS
    Sube a Google Play la ficha de Task Manager: textos e imagenes.

.DESCRIPTION
    Los textos viven en Mobile\GooglePlayConsole\TaskManager\ficha.md y se leen de ahi, de los
    bloques de codigo, para que no haya dos versiones que puedan separarse: la del repositorio y la
    de la consola. Las imagenes (icono y grafico destacado) salen de esa misma carpeta.

    Lo que NO hace, porque la API no lo permite: la clasificacion del contenido, el formulario de
    seguridad de los datos, el publico objetivo y la politica de privacidad. Eso es formulario de la
    consola.

.EXAMPLE
    pwsh .\tools\publicar-ficha-play.ps1
#>
[CmdletBinding()]
param(
    [string] $ServiceAccountJson = 'D:\sOCProjects\Mobile\Hiker\Hiker\hiker-433118-98861f2881fa.json',
    [string] $PackageName = 'com.socratic.taskmanager',
    [string] $FichaPath = 'D:\sOCProjects\Mobile\GooglePlayConsole\TaskManager\ficha.md',
    [string] $ImagenesPath = 'D:\sOCProjects\Mobile\GooglePlayConsole\TaskManager',
    [switch] $SoloTextos
)

$ErrorActionPreference = 'Stop'

# --- Token de la cuenta de servicio (JWT firmado, como pide Google) --------------------------

function Get-AccessToken {
    param([string] $JsonPath)

    $cuenta = Get-Content $JsonPath -Raw | ConvertFrom-Json

    function ToBase64Url ([byte[]] $bytes) {
        [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }

    $ahora = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $cabecera = @{ alg = 'RS256'; typ = 'JWT' } | ConvertTo-Json -Compress
    $cuerpo = @{
        iss   = $cuenta.client_email
        scope = 'https://www.googleapis.com/auth/androidpublisher'
        aud   = 'https://oauth2.googleapis.com/token'
        exp   = $ahora + 3600
        iat   = $ahora
    } | ConvertTo-Json -Compress

    $sinFirma = (ToBase64Url ([Text.Encoding]::UTF8.GetBytes($cabecera))) + '.' +
                (ToBase64Url ([Text.Encoding]::UTF8.GetBytes($cuerpo)))

    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem($cuenta.private_key)
    $firma = $rsa.SignData([Text.Encoding]::UTF8.GetBytes($sinFirma),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

    $respuesta = Invoke-RestMethod -Method Post -Uri 'https://oauth2.googleapis.com/token' -Body @{
        grant_type = 'urn:ietf:params:oauth:grant-type:jwt-bearer'
        assertion  = $sinFirma + '.' + (ToBase64Url $firma)
    }

    return $respuesta.access_token
}

# --- Los textos, sacados de la ficha ---------------------------------------------------------

function Get-Ficha {
    param([string] $Path)

    $texto = Get-Content $Path -Raw

    # Cada idioma es una seccion "## xx-XX" con tres bloques de codigo dentro, en este orden:
    # titulo, descripcion breve y descripcion completa.
    $fichas = @{}
    foreach ($idioma in 'es-ES', 'en-US') {
        $seccion = [regex]::Match($texto, "(?ms)^## $idioma.*?(?=^---|\z)")
        if (-not $seccion.Success) {
            throw "No hay seccion '## $idioma' en $Path"
        }

        $bloques = [regex]::Matches($seccion.Value, '(?ms)^```\r?\n(.*?)\r?\n```')
        if ($bloques.Count -lt 3) {
            throw "La seccion '$idioma' tiene $($bloques.Count) bloques y hacen falta 3"
        }

        $fichas[$idioma] = @{
            title            = $bloques[0].Groups[1].Value.Trim()
            shortDescription = $bloques[1].Groups[1].Value.Trim()
            fullDescription  = $bloques[2].Groups[1].Value.Trim()
        }
    }

    # Los limites de Play. Mejor reventar aqui que recibir un 400 a media subida.
    foreach ($idioma in $fichas.Keys) {
        $f = $fichas[$idioma]
        if ($f.title.Length -gt 30) { throw "$idioma - titulo de $($f.title.Length) caracteres (max 30)" }
        if ($f.shortDescription.Length -gt 80) { throw "$idioma - descripcion breve de $($f.shortDescription.Length) (max 80)" }
        if ($f.fullDescription.Length -gt 4000) { throw "$idioma - descripcion completa de $($f.fullDescription.Length) (max 4000)" }
    }

    return $fichas
}

# --- A la consola ----------------------------------------------------------------------------

$fichas = Get-Ficha $FichaPath
foreach ($idioma in $fichas.Keys) {
    $f = $fichas[$idioma]
    Write-Host "$idioma - titulo $($f.title.Length), breve $($f.shortDescription.Length), completa $($f.fullDescription.Length)"
}

$token = Get-AccessToken $ServiceAccountJson
$cabeceras = @{ Authorization = "Bearer $token" }
$api = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$PackageName"
$apiSubida = "https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/$PackageName"

$edit = Invoke-RestMethod -Method Post -Uri "$api/edits" -Headers $cabeceras
Write-Host "Edit: $($edit.id)"

try {
    foreach ($idioma in 'es-ES', 'en-US') {
        $f = $fichas[$idioma]
        $cuerpo = @{
            language         = $idioma
            title            = $f.title
            shortDescription = $f.shortDescription
            fullDescription  = $f.fullDescription
        } | ConvertTo-Json -Depth 5

        Invoke-RestMethod -Method Put -Uri "$api/edits/$($edit.id)/listings/$idioma" `
            -Headers $cabeceras -ContentType 'application/json; charset=utf-8' `
            -Body ([Text.Encoding]::UTF8.GetBytes($cuerpo)) | Out-Null

        Write-Host "Textos de $idioma subidos"

        if ($SoloTextos) { continue }

        foreach ($imagen in @(
                @{ Tipo = 'icon'; Fichero = 'icon_512.png' },
                @{ Tipo = 'featureGraphic'; Fichero = 'feature_graphic.png' })) {

            $ruta = Join-Path $ImagenesPath $imagen.Fichero
            if (-not (Test-Path $ruta)) {
                Write-Host "  (falta $($imagen.Fichero), se salta)"
                continue
            }

            # Se borra lo que hubiera de ese tipo: la API acumula, y un icono viejo al lado del
            # nuevo deja la ficha con dos.
            Invoke-RestMethod -Method Delete -Headers $cabeceras `
                -Uri "$api/edits/$($edit.id)/listings/$idioma/$($imagen.Tipo)" | Out-Null

            Invoke-RestMethod -Method Post -Headers $cabeceras -ContentType 'image/png' `
                -Uri "$apiSubida/edits/$($edit.id)/listings/$idioma/$($imagen.Tipo)?uploadType=media" `
                -InFile $ruta | Out-Null

            Write-Host "  $($imagen.Tipo) de $idioma subido"
        }
    }

    Invoke-RestMethod -Method Post -Uri "$api/edits/$($edit.id):commit" -Headers $cabeceras -Body '' | Out-Null
    Write-Host "Commit hecho: la ficha esta en Play Console"
}
catch {
    Write-Host "Fallo: $($_.Exception.Message)"
    if ($_.ErrorDetails) { Write-Host $_.ErrorDetails.Message }
    try { Invoke-RestMethod -Method Delete -Uri "$api/edits/$($edit.id)" -Headers $cabeceras | Out-Null } catch { }
    throw
}
