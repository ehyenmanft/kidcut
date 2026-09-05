@echo off
title KidCut - 8-Bit Pixel Video Studio
echo ===================================================
echo   KIDCUT - 8-BIT RETRO PIXEL VIDEO STUDIO
echo   Iniciando KidCut en tu equipo Windows...
echo ===================================================
cd /d "%~dp0"

echo [1/2] Verificando dependencias...
if not exist node_modules (
    echo Instalando modulos necesarios...
    call npm install
)

echo [2/2] Lanzando KidCut Studio...
start http://localhost:5173
call npm run dev -- --host

pause
