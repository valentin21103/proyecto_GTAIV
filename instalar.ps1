# Copia la carpeta juego/ encima de la instalacion de GTA IV, haciendo backup de lo que reemplaza.
# Uso: .\instalar.ps1 -Juego "D:\SteamLibrary\steamapps\common\Grand Theft Auto IV\GTAIV"
param(
    [Parameter(Mandatory = $true)][string]$Juego
)

$origen = Join-Path $PSScriptRoot "juego"
if (-not (Test-Path (Join-Path $Juego "GTAIV.exe"))) { throw "No encuentro GTAIV.exe en $Juego" }

$backup = Join-Path $Juego ("_backup_repo_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
$archivos = Get-ChildItem -Path $origen -Recurse -File
foreach ($f in $archivos) {
    $rel = $f.FullName.Substring($origen.Length + 1)
    $dest = Join-Path $Juego $rel
    if (Test-Path $dest) {
        $bk = Join-Path $backup $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $bk) | Out-Null
        Copy-Item $dest $bk -Force
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Copy-Item $f.FullName $dest -Force
    Write-Host "OK  $rel"
}
Write-Host "`nListo. Backup de lo reemplazado en: $backup"
