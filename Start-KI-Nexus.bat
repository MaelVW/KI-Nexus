@echo off
rem KI-Nexus per Doppelklick starten.
rem Baut die App, falls sich etwas geändert hat, und startet sie dann.
rem Dieses Fenster schliesst sich danach von selbst.

setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "PROJECT=%~dp0src\KiNexus.App\KiNexus.App.csproj"
set "APP_EXE=%~dp0src\KiNexus.App\bin\Release\net10.0-windows\KI-Nexus.exe"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Das .NET SDK fehlt. Installiere .NET 10 und starte KI-Nexus dann nochmals.
    pause
    exit /b 1
)

rem Läuft die App schon, ist die .exe gesperrt. Dann nicht bauen, sondern nur Bescheid geben.
tasklist /fi "imagename eq KI-Nexus.exe" /nh | find /i "KI-Nexus.exe" >nul
if not errorlevel 1 (
    echo KI-Nexus läuft schon. Schau in der Taskleiste nach.
    timeout /t 4 >nul
    exit /b 0
)

rem Fehlt llama.cpp (z.B. nach einem frischen git clone), bieten wir den Download an.
if not exist "%~dp0engines\llama.cpp\llama-server.exe" (
    echo llama.cpp fehlt noch. Ohne llama.cpp gehen nur Ollama-KIs.
    choice /c JN /m "Jetzt herunterladen (ca. 560 MB)"
    if not errorlevel 2 (
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-engines.ps1"
        if errorlevel 1 (
            echo Der Download ist fehlgeschlagen. KI-Nexus startet trotzdem.
            pause
        )
    )
)

echo KI-Nexus wird vorbereitet ...
dotnet build "%PROJECT%" -c Release --nologo -v quiet
if errorlevel 1 (
    echo.
    echo Der Build ist fehlgeschlagen. Die Fehler stehen oben.
    echo Hilfe: docs\09-Fehlerbehebung.md
    pause
    exit /b 1
)

start "" "%APP_EXE%"
endlocal
