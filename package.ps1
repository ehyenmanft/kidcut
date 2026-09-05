# KidCut Windows Packaging Script
Write-Host "==================================================" -ForegroundColor Yellow
Write-Host "  KIDCUT - EMPAQUETADOR DE PRODUCCION WINDOWS" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Yellow

$baseDir = $PSScriptRoot
Set-Location $baseDir

# 1. Compilar Frontend con Vite
Write-Host "`n[1/4] Compilando frontend web (Vite)..." -ForegroundColor Green
& npm run build
if ($LASTEXITCODE -ne 0) {
    Write-Error "Fallo la compilacion de Vite."
    exit 1
}

# 2. Compilar KidCut.exe con csc.exe
Write-Host "`n[2/4] Compilando ejecutable nativo KidCut.exe..." -ForegroundColor Green
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /target:winexe /win32icon:icon.ico /out:KidCut.exe Program.cs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Fallo la compilacion de KidCut.exe."
    exit 1
}

# 3. Preparar carpeta de distribucion Portable
Write-Host "`n[3/4] Preparando carpeta de distribucion 'release\KidCut-Windows'..." -ForegroundColor Green
$releaseDir = Join-Path $baseDir "release\KidCut-Windows"
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# Copiar ejecutable y recursos necesarios
Copy-Item (Join-Path $baseDir "KidCut.exe") $releaseDir -Force
Copy-Item (Join-Path $baseDir "icon.ico") $releaseDir -Force
Copy-Item (Join-Path $baseDir "dist") $releaseDir -Recurse -Force
Copy-Item (Join-Path $baseDir "public") $releaseDir -Recurse -Force

# Crear archivo LEEME.txt de instrucciones
$readmeLines = @(
    "====================================================================",
    "           ★ KIDCUT - 8-BIT RETRO PIXEL VIDEO STUDIO ★",
    "====================================================================",
    "",
    "COMO EJECUTAR:",
    "1. Haz doble clic sobre 'KidCut.exe'.",
    "2. Se abrira automaticamente la ventana de control arcade y tu navegador",
    "   en modo aplicacion de pantalla completa.",
    "3. Puedes hacer clic en el boton para crear un acceso directo en tu Escritorio.",
    "4. Al minimizar la ventana de control, KidCut permanece activo en la bandeja.",
    "",
    "REQUISITOS DEL SISTEMA:",
    "- Windows 7, 8, 10 o 11 (64-bit)",
    "- No requiere instalar Node.js ni Electron.",
    "- No requiere permisos de Administrador.",
    "",
    "====================================================================",
    "               Desarrollado para KidCut Studios",
    "===================================================================="
)
$readmeLines | Out-File -FilePath (Join-Path $releaseDir "LEEME.txt") -Encoding UTF8

# 4. Crear archivo ZIP portable
Write-Host "`n[4/4] Comprimiendo paquete ZIP 'release\KidCut-v1.0-Windows-Portable.zip'..." -ForegroundColor Green
$zipPath = Join-Path $baseDir "release\KidCut-v1.0-Windows-Portable.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path "$releaseDir\*" -DestinationPath $zipPath -Force

Write-Host "`n==================================================" -ForegroundColor Yellow
Write-Host "  EMPAQUETADO COMPLETADO EXITOSAMENTE" -ForegroundColor Green
Write-Host "  Ejecutable local:  $baseDir\KidCut.exe" -ForegroundColor White
Write-Host "  Carpeta Portable:  $releaseDir" -ForegroundColor White
Write-Host "  Archivo ZIP:       $zipPath" -ForegroundColor White
Write-Host "==================================================" -ForegroundColor Yellow
