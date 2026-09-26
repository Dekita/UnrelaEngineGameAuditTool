@echo off
REM Compares the two manifests from before/after an update (see hash-game-files.bat) and lists
REM what changed. Reads OLD_GAME_MANIFEST/NEW_GAME_MANIFEST from settings.bat - paste in the two
REM full paths that hash-game-files.bat printed before running this.
cd /d "%~dp0" || (echo Could not switch to this script's own folder. & pause & exit /b 1)
call "%~dp0settings.bat"

"%EXE%" compare-game-files --old "%OLD_GAME_MANIFEST%" --new "%NEW_GAME_MANIFEST%" --out "%GAME_DIFF_FILE%" --md "%GAME_DIFF_REPORT_MD%"
set EXIT_CODE=%ERRORLEVEL%

pause
exit /b %EXIT_CODE%
