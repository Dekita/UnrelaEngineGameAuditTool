@echo off
REM Cross-references your mods manifest against a game comparison to report exactly which mods
REM need republishing after the update. Doesn't compare two mod snapshots against each other -
REM there's only ever one mods manifest.
cd /d "%~dp0" || (echo Could not switch to this script's own folder. & pause & exit /b 1)
call "%~dp0settings.bat"

"%EXE%" compare-mod-files --diff "%GAME_DIFF_FILE%" --mods "%MODS_MANIFEST%" --md "%MOD_COMPARE_REPORT_MD%"
set EXIT_CODE=%ERRORLEVEL%

pause
exit /b %EXIT_CODE%
