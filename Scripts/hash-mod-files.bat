@echo off
REM Hashes your mod collection so it can be cross-referenced against a game comparison later.
REM Doesn't need the base game files - can be run any time your mods change. Always safe to
REM re-run - it just overwrites MODS_MANIFEST, since there's only ever one current mods snapshot.
cd /d "%~dp0" || (echo Could not switch to this script's own folder. & pause & exit /b 1)
call "%~dp0settings.bat"

"%EXE%" hash-mod-files --mods "%MODS_FOLDER%" --ue-version %UE_VERSION% %AES_ARGS% --out "%MODS_MANIFEST%"
set EXIT_CODE=%ERRORLEVEL%

pause
exit /b %EXIT_CODE%
