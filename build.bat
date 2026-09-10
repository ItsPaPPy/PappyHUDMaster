@echo off
setlocal EnableExtensions

REM ============================================================
REM Pappy HUD Master - public build script
REM Version 1.0.0
REM
REM This script intentionally does NOT depend on Gale or any
REM specific mod manager.
REM ============================================================

set "PROJECT_DIR=%~dp0"
set "OUT_DIR=%PROJECT_DIR%build"

REM ------------------------------------------------------------
REM Find Valheim
REM ------------------------------------------------------------

set "VALHEIM_DIR=C:\Program Files (x86)\Steam\steamapps\common\Valheim"
if exist "%VALHEIM_DIR%\valheim_Data\Managed\UnityEngine.CoreModule.dll" goto valheim_found

set "VALHEIM_DIR=C:\Program Files\Steam\steamapps\common\Valheim"
if exist "%VALHEIM_DIR%\valheim_Data\Managed\UnityEngine.CoreModule.dll" goto valheim_found

goto valheim_not_found

:valheim_found
set "MANAGED=%VALHEIM_DIR%\valheim_Data\Managed"
echo.
echo Found Valheim:
echo   %VALHEIM_DIR%
echo.
goto find_bepinex

:valheim_not_found
echo.
echo Could not automatically find Valheim.
echo Edit VALHEIM_DIR near the top of build.bat.
echo.
pause
exit /b 1

REM ------------------------------------------------------------
REM Find BepInEx
REM ------------------------------------------------------------

:find_bepinex
set "BEPINEX_ROOT="

REM Optional developer override:
REM   set PAPPYHUD_BEPINEX_ROOT=C:\Path\To\BepInEx
if defined PAPPYHUD_BEPINEX_ROOT set "BEPINEX_ROOT=%PAPPYHUD_BEPINEX_ROOT%"

REM Standard/manual BepInEx installation inside the game directory:
if not defined BEPINEX_ROOT if exist "%VALHEIM_DIR%\BepInEx\core\BepInEx.dll" set "BEPINEX_ROOT=%VALHEIM_DIR%\BepInEx"

if defined BEPINEX_ROOT goto bepinex_found

echo.
echo Could not automatically find BepInEx.
echo.
echo This build script is mod-manager agnostic.
echo If your mod manager stores profiles elsewhere, set:
echo.
echo   PAPPYHUD_BEPINEX_ROOT
echo.
echo to the BepInEx folder used by your development/test profile.
echo.
echo Example:
echo   set PAPPYHUD_BEPINEX_ROOT=C:\Path\To\BepInEx
echo   build.bat
echo.
pause
exit /b 1

:bepinex_found
echo BepInEx:
echo   %BEPINEX_ROOT%
echo.

if not exist "%BEPINEX_ROOT%\core\BepInEx.dll" goto missing_bepinex
if not exist "%BEPINEX_ROOT%\core\0Harmony.dll" goto missing_harmony
if not exist "%MANAGED%\netstandard.dll" goto missing_netstandard
if not exist "%MANAGED%\assembly_valheim.dll" goto missing_valheim_assembly
if not exist "%MANAGED%\assembly_utils.dll" goto missing_utils
if not exist "%MANAGED%\assembly_guiutils.dll" goto missing_guiutils
if not exist "%MANAGED%\UnityEngine.dll" goto missing_unity
if not exist "%MANAGED%\UnityEngine.CoreModule.dll" goto missing_unity
if not exist "%MANAGED%\UnityEngine.InputLegacyModule.dll" goto missing_unity
if not exist "%MANAGED%\UnityEngine.UIModule.dll" goto missing_unity
if not exist "%MANAGED%\UnityEngine.IMGUIModule.dll" goto missing_imgui
if not exist "%MANAGED%\UnityEngine.TextRenderingModule.dll" goto missing_text
if not exist "%MANAGED%\UnityEngine.UI.dll" goto missing_ui
goto find_compiler

:missing_bepinex
echo Missing: %BEPINEX_ROOT%\core\BepInEx.dll
pause
exit /b 1

:missing_harmony
echo Missing: %BEPINEX_ROOT%\core\0Harmony.dll
pause
exit /b 1

:missing_netstandard
echo Missing: %MANAGED%\netstandard.dll
pause
exit /b 1

:missing_valheim_assembly
echo Missing: %MANAGED%\assembly_valheim.dll
pause
exit /b 1

:missing_utils
echo Missing: %MANAGED%\assembly_utils.dll
pause
exit /b 1

:missing_guiutils
echo Missing: %MANAGED%\assembly_guiutils.dll
pause
exit /b 1

:missing_unity
echo A required Unity DLL was not found in:
echo   %MANAGED%
pause
exit /b 1

:missing_imgui
echo Missing: %MANAGED%\UnityEngine.IMGUIModule.dll
pause
exit /b 1

:missing_text
echo Missing: %MANAGED%\UnityEngine.TextRenderingModule.dll
pause
exit /b 1

:missing_ui
echo Missing: %MANAGED%\UnityEngine.UI.dll
pause
exit /b 1

:find_compiler
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if exist "%CSC%" goto compiler_found

set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if exist "%CSC%" goto compiler_found

echo.
echo csc.exe was not found.
echo.
pause
exit /b 1

:compiler_found
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo Building PappyHUDMaster.dll...
echo.

"%CSC%" ^
 /nologo ^
 /target:library ^
 /optimize+ ^
 /out:"%OUT_DIR%\PappyHUDMaster.dll" ^
 /reference:"%MANAGED%\netstandard.dll" ^
 /reference:"%BEPINEX_ROOT%\core\BepInEx.dll" ^
 /reference:"%BEPINEX_ROOT%\core\0Harmony.dll" ^
 /reference:"%MANAGED%\assembly_valheim.dll" ^
 /reference:"%MANAGED%\assembly_utils.dll" ^
 /reference:"%MANAGED%\assembly_guiutils.dll" ^
 /reference:"%MANAGED%\UnityEngine.dll" ^
 /reference:"%MANAGED%\UnityEngine.CoreModule.dll" ^
 /reference:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
 /reference:"%MANAGED%\UnityEngine.UIModule.dll" ^
 /reference:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
 /reference:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
 /reference:"%MANAGED%\UnityEngine.UI.dll" ^
 "%PROJECT_DIR%PappyHUDMaster.cs"

if errorlevel 1 goto build_failed

echo.
echo BUILD SUCCEEDED.
echo.
echo DLL:
echo   %OUT_DIR%\PappyHUDMaster.dll
echo.
pause
exit /b 0

:build_failed
echo.
echo BUILD FAILED.
echo.
pause
exit /b 1
