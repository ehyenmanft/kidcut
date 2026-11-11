# Compila KidNoez.exe (herramienta independiente de KidCut) con el compilador de .NET Framework
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /optimize+ /target:winexe /win32icon:KidNoez.ico /out:KidNoez.exe KidNoez.cs
if ($LASTEXITCODE -ne 0) { throw "La compilación de KidNoez falló" }
Write-Host "KidNoez.exe listo en $PSScriptRoot"
