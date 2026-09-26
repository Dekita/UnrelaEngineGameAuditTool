@echo off
REM Optional - only for native/C++ mods. Run this BEFORE an update to record the current UE4SS
REM header dump, then run it again AFTER the update. No fixed output path - each run auto-names
REM its own manifest into OUT_DIR (date + a random suffix, so two runs never collide), and prints
REM the full path below. Copy the two paths you get into OLD_HEADER_MANIFEST/NEW_HEADER_MANIFEST
REM in settings.bat before running compare-header-files.bat.
cd /d "%~dp0" || (echo Could not switch to this script's own folder. & pause & exit /b 1)
call "%~dp0settings.bat"

REM settings.bat documents paths as "absolute or relative to this folder" - resolve UE4SS_FOLDER to an
REM absolute path now, while still in that folder, since the cd into OUT_DIR below would otherwise make a
REM relative UE4SS_FOLDER (which itself defaults to a path relative to PAKS_FOLDER) resolve against OUT_DIR
REM instead.
for %%I in ("%UE4SS_FOLDER%") do set "UE4SS_FOLDER=%%~fI"

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%" || (echo Could not create OUT_DIR "%OUT_DIR%". & pause & exit /b 1)
cd /d "%OUT_DIR%" || (echo Could not switch to OUT_DIR "%OUT_DIR%". & pause & exit /b 1)
"%EXE%" hash-header-files --ue4ss "%UE4SS_FOLDER%"
set EXIT_CODE=%ERRORLEVEL%

pause
exit /b %EXIT_CODE%
