@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem Build from the repository root with the Windows .NET Framework compiler.
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework 4 compiler was not found.
    exit /b 1
)
set "RESOURCES="
for %%F in ("resources\*.png") do if exist "%%~F" set "RESOURCES=!RESOURCES! -resource:"%%~F","%%~nxF""
"%CSC%" -nologo -nowarn:108 -target:winexe -out:Glukoboy.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll !RESOURCES! src\*.cs
set "CSC_RESULT=%ERRORLEVEL%"
if not "%CSC_RESULT%"=="0" exit /b %CSC_RESULT%
rem Preserve local settings and tasks on subsequent builds.
if not exist "settings.ini" copy /y "src\settings.ini" "settings.ini" >nul
if not exist "tasks.txt" copy /y "src\tasks.txt" "tasks.txt" >nul
exit /b %CSC_RESULT%
