@echo off
REM Run this BEFORE an update to record the current asset hashes, then run it again AFTER the
REM update to record the new state. No fixed output path - each run auto-names its own manifest
REM into OUT_DIR (version + date + a random suffix, so two runs never collide), and prints the
REM full path below. Copy the two paths you get into OLD_GAME_MANIFEST/NEW_GAME_MANIFEST in
REM settings.bat before running compare-game-files.bat.
cd /d "%~dp0" || (echo Could not switch to this script's own folder. & pause & exit /b 1)
call "%~dp0settings.bat"

REM settings.bat documents paths as "absolute or relative to this folder" - resolve PAKS_FOLDER to an
REM absolute path now, while still in that folder, since the cd into OUT_DIR below would otherwise make a
REM relative PAKS_FOLDER resolve against OUT_DIR instead.
for %%I in ("%PAKS_FOLDER%") do set "PAKS_FOLDER=%%~fI"

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%" || (echo Could not create OUT_DIR "%OUT_DIR%". & pause & exit /b 1)
cd /d "%OUT_DIR%" || (echo Could not switch to OUT_DIR "%OUT_DIR%". & pause & exit /b 1)
"%EXE%" hash-game-files --paks "%PAKS_FOLDER%" --ue-version %UE_VERSION% %AES_ARGS%
set EXIT_CODE=%ERRORLEVEL%

pause
exit /b %EXIT_CODE%
