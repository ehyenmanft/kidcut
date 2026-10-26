# Compila KidVoz.exe (herramienta independiente de KidCut) con el compilador de .NET Framework
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /optimize+ /target:winexe /win32icon:..\icon.ico /out:KidVoz.exe KidVoz.cs
if ($LASTEXITCODE -ne 0) { throw "La compilación de KidVoz falló" }
Write-Host "KidVoz.exe listo en $PSScriptRoot"
