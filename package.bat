@echo off
setlocal

REM Anchor every relative path below to this script's own folder (the repo root), regardless of the
REM caller's current directory - double-clicking this via a shortcut with a different "Start in", or
REM running it from an unrelated terminal cwd, would otherwise silently build/read/write the wrong tree.
cd /d "%~dp0" || (echo Could not switch to the repository root. & pause & exit /b 1)

REM Set project paths (optional: update if needed)
set CLI_PROJECT=Source\DekUnrealGameAudit.csproj
set GUI_PROJECT=Source\Gui\DekUnrealGameAudit.Gui.csproj
set TEST_PROJECT=Source\Tests\DekUnrealGameAudit.Tests.csproj
set BUILD_DIR=Build
set CLI_PUBLISH_DIR=%BUILD_DIR%\bin\DekUnrealGameAudit\Release\net10.0\win-x64\publish
set GUI_PUBLISH_DIR=%BUILD_DIR%\bin\DekUnrealGameAudit.Gui\Release\net10.0-windows7.0\win-x64\publish
set SCRIPTS_DIR=Scripts
set OUTPUT_DIR=Output
set STAGING_DIR=%BUILD_DIR%\OutputStaging
set CLI_OUTPUT_DIR=%OUTPUT_DIR%\CommandLineTool
set CLI_STAGING_DIR=%STAGING_DIR%\CommandLineTool

REM Clean the exact configuration we're about to publish - cleaning the default (Debug) configuration
REM here (the previous version of this script did) leaves stale Release publish output untouched, which
REM would let a broken publish still pass the "does the exe exist" check further down.
dotnet clean %CLI_PROJECT% -c Release
if errorlevel 1 (echo. & echo dotnet clean failed for %CLI_PROJECT%. & pause & exit /b 1)
dotnet clean %GUI_PROJECT% -c Release
if errorlevel 1 (echo. & echo dotnet clean failed for %GUI_PROJECT%. & pause & exit /b 1)

REM Run the automated suite as part of the same gate used to assemble a release. A successful publish
REM alone proves that the projects compile, not that the parsers, comparisons and cancellation paths still
REM behave correctly.
dotnet test %TEST_PROJECT% -c Release
if errorlevel 1 (echo. & echo dotnet test failed for %TEST_PROJECT%. & pause & exit /b 1)

REM Publish the CLI tool
dotnet publish %CLI_PROJECT% ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  --ignore-failed-sources ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:DebugType=None ^
  /p:DebugSymbols=false
if errorlevel 1 (echo. & echo dotnet publish failed for %CLI_PROJECT%. & pause & exit /b 1)

if not exist "%CLI_PUBLISH_DIR%\DekUnrealGameAudit.exe" (
    echo.
    echo Publish reported success but DekUnrealGameAudit.exe is missing from %CLI_PUBLISH_DIR%
    pause
    exit /b 1
)

REM Publish the GUI
dotnet publish %GUI_PROJECT% ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  --ignore-failed-sources ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:DebugType=None ^
  /p:DebugSymbols=false
if errorlevel 1 (echo. & echo dotnet publish failed for %GUI_PROJECT%. & pause & exit /b 1)

if not exist "%GUI_PUBLISH_DIR%\DekUnrealGameAudit.Gui.exe" (
    echo.
    echo Publish reported success but DekUnrealGameAudit.Gui.exe is missing from %GUI_PUBLISH_DIR%
    pause
    exit /b 1
)

REM Smoke-test the published CLI exe before touching the real Output\ folder at all - existing and
REM running are different things (a missing native dependency can produce an exe that exists but
REM crashes on launch).
"%CLI_PUBLISH_DIR%\DekUnrealGameAudit.exe" help >nul
if errorlevel 1 (echo. & echo Smoke test failed: DekUnrealGameAudit.exe help returned a nonzero exit code. & pause & exit /b 1)

REM Assemble the new deliverable in a disposable staging folder first, and only replace the real
REM Output\ folder once every copy below has actually succeeded. This way a failure partway through
REM never leaves Output\ half old/half new, and a failure anywhere above never touches Output\ at all -
REM unlike deleting Output\ upfront and hoping the rest of the script goes well.
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
mkdir "%STAGING_DIR%" || (echo Could not create staging folder "%STAGING_DIR%". & pause & exit /b 1)
mkdir "%CLI_STAGING_DIR%" || (echo Could not create staging folder "%CLI_STAGING_DIR%". & pause & exit /b 1)

copy /Y "%CLI_PUBLISH_DIR%\DekUnrealGameAudit.exe" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage DekUnrealGameAudit.exe. & pause & exit /b 1)
copy /Y "%GUI_PUBLISH_DIR%\DekUnrealGameAudit.Gui.exe" "%STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage DekUnrealGameAudit.Gui.exe. & pause & exit /b 1)
copy /Y "%SCRIPTS_DIR%\settings.bat" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage settings.bat. & pause & exit /b 1)
copy /Y "%SCRIPTS_DIR%\hash-game-files.bat" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage hash-game-files.bat. & pause & exit /b 1)
copy /Y "%SCRIPTS_DIR%\compare-game-files.bat" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage compare-game-files.bat. & pause & exit /b 1)
copy /Y "%SCRIPTS_DIR%\hash-mod-files.bat" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage hash-mod-files.bat. & pause & exit /b 1)
copy /Y "%SCRIPTS_DIR%\compare-mod-files.bat" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage compare-mod-files.bat. & pause & exit /b 1)
copy /Y "%SCRIPTS_DIR%\hash-header-files.bat" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage hash-header-files.bat. & pause & exit /b 1)
copy /Y "%SCRIPTS_DIR%\compare-header-files.bat" "%CLI_STAGING_DIR%\" >nul
if errorlevel 1 (echo. & echo Failed to stage compare-header-files.bat. & pause & exit /b 1)

REM NOTE: settings.local.bat (real AES keys/paths, if you use one) is deliberately never staged/copied -
REM it's gitignored and personal, and packaging must never ship it. See settings.bat's own comments.

REM Everything staged successfully - now, and only now, replace the real Output\ folder.
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
move /Y "%STAGING_DIR%" "%OUTPUT_DIR%" >nul
if errorlevel 1 (echo. & echo Could not move staged output into %OUTPUT_DIR%. & pause & exit /b 1)

echo.
echo Done. Everything you need is in %OUTPUT_DIR% - DekUnrealGameAudit.Gui.exe for the UI,
echo and %CLI_OUTPUT_DIR% (the CLI exe plus its launcher scripts) for scripting/automation.
echo (%BUILD_DIR% still has the full build output - delete it yourself whenever you like.)
echo.
echo Reminder: %OUTPUT_DIR% is fully deleted and regenerated every time this script runs. If you use
echo the packaged copy for real work rather than just distributing it, keep your own settings.local.bat
echo and any real manifests OUTSIDE %OUTPUT_DIR% (e.g. point OUT_DIR at a folder next to it, not inside
echo it) - re-running package.bat will silently delete anything left inside %OUTPUT_DIR%.
pause
