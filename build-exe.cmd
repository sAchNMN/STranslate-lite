@echo off
rem ===========================================================================
rem  STranslate.Lite one-click EXE builder
rem  Double-click this file to get a menu, or pass a mode:
rem      build-exe.cmd 1             single-file exe (needs .NET 10 runtime)
rem      build-exe.cmd 2             self-contained single-file exe
rem      build-exe.cmd 3             Velopack installer (Setup.exe + zip)
rem      build-exe.cmd 4             all of the above
rem      build-exe.cmd 1 -Version 2.0.12 -Out D:\dist
rem  Real work is done by build-exe.ps1 next to this file.
rem ===========================================================================
setlocal EnableExtensions
title STranslate.Lite - build exe
set "CODE=1"
set "ROOT=%~dp0"

if not exist "%ROOT%build-exe.ps1" (
    echo [ERROR] build-exe.ps1 not found next to this script.
    goto :end
)

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Windows PowerShell not found.
    goto :end
)

powershell.exe -NoProfile -NoLogo -ExecutionPolicy Bypass -File "%ROOT%build-exe.ps1" %*
set "CODE=%ERRORLEVEL%"

echo.
if "%CODE%"=="0" (
    echo [DONE] Build finished.
) else (
    echo [FAILED] exit code %CODE%
)

:end
echo.
rem Only wait for a key when double-clicked (no arguments), so scripts stay non-blocking.
if "%~1"=="" pause
endlocal & exit /b %CODE%