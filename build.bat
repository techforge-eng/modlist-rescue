@echo off
setlocal
title Build ModlistRescue.exe
cd /d "%~dp0"

rem Uses the C# compiler that is built into every Windows 10/11 PC (.NET Framework 4.x)
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
    echo Could not find csc.exe - .NET Framework 4 is missing.
    pause
    exit /b 1
)

"%FW%\csc.exe" /nologo /target:exe /optimize+ /out:ModlistRescue.exe /win32icon:techforge.ico /codepage:65001 ^
  /r:System.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  ModlistRescue.cs

if errorlevel 1 (
    echo.
    echo BUILD FAILED - see errors above.
) else (
    echo.
    echo Built ModlistRescue.exe successfully.
)
pause
