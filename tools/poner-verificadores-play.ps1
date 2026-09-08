# Pone los cuatro grupos de verificadores de siempre en la pista de prueba cerrada.
# La API REEMPLAZA la lista entera, asi que primero se lee la que hay y se fusiona: escribir solo
# los cuatro se llevaria por delante cualquier otro grupo que estuviera puesto a mano.
$ErrorActionPreference = 'Stop'

$habituales = @(
    '12testers14day@googlegroups.com',
    'swaptest-testers@googlegroups.com',
    'testers-community@googlegroups.com',
    '12-testers-app@googlegroups.com'
)

$cuenta = Get-Content 'D:\sOCProjects\Mobile\Hiker\Hiker\hiker-433118-98861f2881fa.json' -Raw | ConvertFrom-Json

function ToBase64Url ([byte[]] $b) { [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_') }

$ahora = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$cabecera = @{ alg = 'RS256'; typ = 'JWT' } | ConvertTo-Json -Compress
$cuerpo = @{
    iss = $cuenta.client_email; scope = 'https://www.googleapis.com/auth/androidpublisher'
    aud = 'https://oauth2.googleapis.com/token'; exp = $ahora + 3600; iat = $ahora
} | ConvertTo-Json -Compress

$sinFirma = (ToBase64Url ([Text.Encoding]::UTF8.GetBytes($cabecera))) + '.' + (ToBase64Url ([Text.Encoding]::UTF8.GetBytes($cuerpo)))
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem($cuenta.private_key)
$firma = $rsa.SignData([Text.Encoding]::UTF8.GetBytes($sinFirma),
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

$token = (Invoke-RestMethod -Method Post -Uri 'https://oauth2.googleapis.com/token' -Body @{
        grant_type = 'urn:ietf:params:oauth:grant-type:jwt-bearer'
        assertion  = $sinFirma + '.' + (ToBase64Url $firma)
    }).access_token

$cabeceras = @{ Authorization = "Bearer $token" }

foreach ($paquete in @('com.socratic.taskmanager', 'com.socratic.musicplayer')) {
    Write-Host "=== $paquete ==="
    $api = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$paquete"
    $edit = Invoke-RestMethod -Method Post -Uri "$api/edits" -Headers $cabeceras

    try {
        $actuales = @()
        try {
            $lista = Invoke-RestMethod -Method Get -Headers $cabeceras -Uri "$api/edits/$($edit.id)/testers/alpha"
            if ($lista.googleGroups) { $actuales = @($lista.googleGroups) }
        }
        catch {
            # Sin verificadores todavia: la API responde con un 404 en vez de una lista vacia.
        }

        Write-Host "  tenia: $(if ($actuales.Count) { $actuales -join ', ' } else { '(ninguno)' })"

        $final = @($actuales + $habituales | Select-Object -Unique)
        $faltaban = @($habituales | Where-Object { $_ -notin $actuales })

        if ($faltaban.Count -eq 0) {
            Write-Host "  ya estaban los cuatro; no se toca"
            Invoke-RestMethod -Method Delete -Uri "$api/edits/$($edit.id)" -Headers $cabeceras | Out-Null
            continue
        }

        $cuerpoTesters = @{ googleGroups = $final } | ConvertTo-Json -Depth 5
        Invoke-RestMethod -Method Put -Headers $cabeceras -ContentType 'application/json' `
            -Uri "$api/edits/$($edit.id)/testers/alpha" -Body $cuerpoTesters | Out-Null

        Invoke-RestMethod -Method Post -Uri "$api/edits/$($edit.id):commit" -Headers $cabeceras -Body '' | Out-Null

        Write-Host "  añadidos: $($faltaban -join ', ')"
        Write-Host "  ahora: $($final -join ', ')"
    }
    catch {
        Write-Host "  fallo: $($_.Exception.Message)"
        if ($_.ErrorDetails) { Write-Host "  $($_.ErrorDetails.Message)" }
        try { Invoke-RestMethod -Method Delete -Uri "$api/edits/$($edit.id)" -Headers $cabeceras | Out-Null } catch { }
    }
}
