@echo off
setlocal EnableDelayedExpansion
title SS14 MedBay Trainer Installer

set "INSTALL_DIR=%~dp0"
if "%INSTALL_DIR:~-1%"=="\" set "INSTALL_DIR=%INSTALL_DIR:~0,-1%"

set "SS14_REPO=https://github.com/space-wizards/space-station-14.git"
set "MOD_ZIP_URL=https://github.com/etu-brute/ss14-Medbaytrainer/archive/refs/heads/main.zip"
set "MOD_ZIP=%INSTALL_DIR%\_mod.zip"
set "MOD_EXTRACT=%INSTALL_DIR%\_mod_extract"

cls
echo.
echo  =====================================================
echo    SS14 MEDBAY TRAINER - INSTALLER
echo    by Brutus / etu_brute
echo  =====================================================
echo.
echo  Install location: %INSTALL_DIR%
echo.

:: ── Check prerequisites ────────────────────────────────────────────────────
echo [1/5] Checking prerequisites...

dotnet --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo.
    echo  [FAIL] .NET SDK not found.
    echo  Please install .NET SDK 9.0 or later from:
    echo  https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set "DOTNET_VER=%%v"
echo  [OK] .NET SDK %DOTNET_VER% found.

git --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo.
    echo  [FAIL] Git not found.
    echo  Please install Git from:
    echo  https://git-scm.com/download/win
    echo.
    pause
    exit /b 1
)
for /f "tokens=*" %%v in ('git --version 2^>nul') do set "GIT_VER=%%v"
echo  [OK] %GIT_VER% found.
echo.

:: ── Clone SS14 ─────────────────────────────────────────────────────────────
echo [2/5] Cloning Space Station 14 with all dependencies...
echo  This will take several minutes depending on your connection.
echo.
git clone --recurse-submodules --progress "%SS14_REPO%" "%INSTALL_DIR%\ss14src" 2>&1
if %ERRORLEVEL% neq 0 (
    echo.
    echo  [FAIL] Clone failed. Check your internet connection and try again.
    pause
    exit /b 1
)
echo.
echo  [OK] SS14 cloned.
echo.

echo  Copying files to install directory...
xcopy /e /i /y /q "%INSTALL_DIR%\ss14src\*" "%INSTALL_DIR%\" >nul
rmdir /s /q "%INSTALL_DIR%\ss14src"
echo  [OK] Done.
echo.

:: ── Download mod ───────────────────────────────────────────────────────────
echo [3/5] Downloading MedBay Trainer mod...
curl -L --progress-bar -o "%MOD_ZIP%" "%MOD_ZIP_URL%"
if %ERRORLEVEL% neq 0 (
    echo  [FAIL] Failed to download mod.
    pause
    exit /b 1
)
echo  [OK] Mod downloaded.

echo  Extracting mod...
if exist "%MOD_EXTRACT%" rmdir /s /q "%MOD_EXTRACT%"
powershell -NoProfile -Command "Expand-Archive -LiteralPath '%MOD_ZIP%' -DestinationPath '%MOD_EXTRACT%' -Force"
del "%MOD_ZIP%"

for /d %%d in ("%MOD_EXTRACT%\*") do set "MOD_ROOT=%%d"

if not exist "%MOD_ROOT%\medtrainer" (
    echo.
    echo  [FAIL] Could not find 'medtrainer' folder inside the mod download.
    dir /b "%MOD_ROOT%"
    pause
    exit /b 1
)

echo  Applying mod files...
xcopy /e /i /y /q "%MOD_ROOT%\medtrainer\*" "%INSTALL_DIR%\" >nul
rmdir /s /q "%MOD_EXTRACT%"
echo  [OK] Mod applied.
echo.

:: ── Build ──────────────────────────────────────────────────────────────────
echo [4/5] Building - this will take several minutes...
echo.

dotnet build "%INSTALL_DIR%\Content.Server\Content.Server.csproj" --configuration Tools /p:WarningLevel=0
if %ERRORLEVEL% neq 0 (
    echo  [FAIL] Server build failed.
    pause
    exit /b 1
)
echo  [OK] Server built.
echo.

dotnet build "%INSTALL_DIR%\Content.Client\Content.Client.csproj" --configuration Tools /p:WarningLevel=0
if %ERRORLEVEL% neq 0 (
    echo  [FAIL] Client build failed.
    pause
    exit /b 1
)
echo  [OK] Client built.
echo.

:: ── Create launch scripts ──────────────────────────────────────────────────
echo [5/5] Creating launch scripts...

:: StartServer.bat
> "%INSTALL_DIR%\StartServer.bat" (
    echo @echo off
    echo cd /d "%%~dp0"
    echo dotnet run --project "%%~dp0Content.Server\Content.Server.csproj" --configuration Tools
    echo pause
)

:: StartClient.bat
> "%INSTALL_DIR%\StartClient.bat" (
    echo @echo off
    echo cd /d "%%~dp0"
    echo dotnet run --project "%%~dp0Content.Client\Content.Client.csproj" --configuration Tools
    echo pause
)

:: Run Medical Trainer.bat - no cd needed, full project path handles spaces fine
> "%INSTALL_DIR%\Run Medical Trainer.bat" (
    echo @echo off
    echo set "DIR=%%~dp0"
    echo if "%%DIR:~-1%%"=="^\^" set "DIR=%%DIR:~0,-1%%"
    echo start "MedBay Trainer - Server" cmd /k dotnet run --project "%%DIR%%\Content.Server\Content.Server.csproj" --configuration Tools
    echo timeout /t 5 /nobreak ^>nul
    echo start "MedBay Trainer - Client" cmd /k dotnet run --project "%%DIR%%\Content.Client\Content.Client.csproj" --configuration Tools
)

echo  [OK] Launch scripts created.
echo.

:: ── Done ───────────────────────────────────────────────────────────────────
echo  =====================================================
echo    INSTALLATION COMPLETE!
echo  =====================================================
echo.
echo  Installed to: %INSTALL_DIR%
echo.
echo  To play:
echo    - Run "Run Medical Trainer.bat" to launch both at once
echo    - Or run StartServer.bat and StartClient.bat separately
echo.
echo  If you really-realllyyy like the mod, help me fuel my addiction to orange chicken and tip me at Ko-fi.com/etu_brute
pause
endlocal
