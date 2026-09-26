@echo off
REM Optional - only for native/C++ mods. Compares the two header manifests from before/after an
REM update (see hash-header-files.bat) and reports memory-layout breaks and source-level changes.
cd /d "%~dp0" || (echo Could not switch to this script's own folder. & pause & exit /b 1)
call "%~dp0settings.bat"

"%EXE%" compare-header-files --old "%OLD_HEADER_MANIFEST%" --new "%NEW_HEADER_MANIFEST%" --out "%HEADER_DIFF_FILE%" --md "%HEADER_DIFF_REPORT_MD%"
set EXIT_CODE=%ERRORLEVEL%

pause
exit /b %EXIT_CODE%
