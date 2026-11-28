@echo off
setlocal EnableExtensions
set "OUT=%~dp0Glukoboy.Diagnostics.exe"
if not "%~1"=="" for %%I in ("%~1") do set "OUT=%%~fI"
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: .NET Framework 4 compiler was not found.
    exit /b 1
)
for %%I in ("%OUT%") do if not exist "%%~dpI" mkdir "%%~dpI"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /utf8output /out:"%OUT%" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll Program.cs ReportPackage.cs BackgroundCaptureStore.cs Collector.cs NativeProbe.cs
exit /b %ERRORLEVEL%
