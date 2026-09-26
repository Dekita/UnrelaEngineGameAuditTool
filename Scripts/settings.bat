@echo off
REM ===================================================================
REM Edit these values for your game, then run the .bat files below in
REM this order. Paths can be absolute or relative to this folder.
REM Double-click each .bat, or run from a terminal.
REM
REM   hash-game-files.bat / compare-game-files.bat     - hash and compare the game's assets
REM   hash-mod-files.bat / compare-mod-files.bat        - hash your mods, check them against
REM                                                        a game comparison
REM   hash-header-files.bat / compare-header-files.bat  - optional, UE4SS header comparison -
REM                                                        only relevant for native/C++ mods
REM
REM hash-game-files.bat and hash-header-files.bat auto-name their own output on every run (no
REM fixed baseline/after-update slot) - after running each one twice, paste the two printed
REM paths into the OLD_/NEW_ variables below before running the matching compare script.
REM
REM Don't put your real AES key or paths directly in THIS file - it's the one committed to
REM source control and shipped in packaged builds. Instead, copy it to "settings.local.bat"
REM next to this file and edit that copy: if settings.local.bat exists, it's used
REM automatically instead of the blank values below, and it's gitignored/never packaged, so
REM a real decryption key never ends up in git history or a shared Output\ folder.
REM ===================================================================

if exist "%~dp0settings.local.bat" (
    call "%~dp0settings.local.bat"
    goto :eof
)

set EXE=%~dp0DekUnrealGameAudit.exe

REM Exact CUE4Parse EGame enum name for your game, e.g. GAME_UE4_26, GAME_UE5_3.
REM See: https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/Versions/EGame.cs
set UE_VERSION=

REM Folder containing the game's .pak / .utoc+.ucas files.
set PAKS_FOLDER=

REM Folder containing your mod paks (subfolders are scanned too).
set MODS_FOLDER=%PAKS_FOLDER%\~mods

REM Leave blank if the game isn't encrypted. Otherwise set to
REM --aes GUID:HEXKEY (repeat --aes for multiple keys), or just
REM --aes HEXKEY for a game with a single main key.
set AES_ARGS=

REM Where manifest/report files get written. hash-game-files.bat and hash-header-files.bat
REM auto-name their own output straight into OUT_DIR (see the note above) - they don't use a
REM variable here. Everything else writes to (or reads from) a fixed path below.
set OUT_DIR=manifests
set MODS_MANIFEST=%OUT_DIR%\mods.json
set GAME_DIFF_FILE=%OUT_DIR%\game-comparison.json
set GAME_DIFF_REPORT_MD=%OUT_DIR%\game-comparison-report.md
set MOD_COMPARE_REPORT_MD=%OUT_DIR%\mod-update-report.md

REM Paste in the two paths that hash-game-files.bat printed (before and after the update)
REM before running compare-game-files.bat.
set OLD_GAME_MANIFEST=%OUT_DIR%\PASTE-OLD-GAME-MANIFEST-PATH-HERE.json
set NEW_GAME_MANIFEST=%OUT_DIR%\PASTE-NEW-GAME-MANIFEST-PATH-HERE.json

REM Optional - only needed for hash-header-files.bat/compare-header-files.bat. UE4SS's own
REM output folder, next to its DLL, e.g. <Game>\Binaries\Win64\ue4ss. Must contain a
REM CXXHeaderDump and/or UHTHeaderDump subfolder (UE4SS creates these itself when its
REM header-dumping options are enabled).
set UE4SS_FOLDER=%PAKS_FOLDER%\..\..\Binaries\Win64\ue4ss

set HEADER_DIFF_FILE=%OUT_DIR%\header-comparison.json
set HEADER_DIFF_REPORT_MD=%OUT_DIR%\header-comparison-report.md

REM Paste in the two paths that hash-header-files.bat printed (before and after the update)
REM before running compare-header-files.bat.
set OLD_HEADER_MANIFEST=%OUT_DIR%\PASTE-OLD-HEADER-MANIFEST-PATH-HERE.json
set NEW_HEADER_MANIFEST=%OUT_DIR%\PASTE-NEW-HEADER-MANIFEST-PATH-HERE.json
