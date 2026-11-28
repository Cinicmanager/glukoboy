$ErrorActionPreference = 'Stop'
$toolRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('glukoboy-background-checks-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$checkExe = Join-Path $testRoot 'BackgroundChecks.exe'
$arguments = @('-nologo','-target:exe','-codepage:65001','-utf8output','-main:GlukoboyDiagnostics.Tests.BackgroundChecks',('-out:' + $checkExe),'-r:System.Windows.Forms.dll','-r:System.Drawing.dll','-r:System.Web.Extensions.dll','-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll')
$arguments += @('Program.cs','Collector.cs','NativeProbe.cs','ReportPackage.cs','BackgroundCaptureStore.cs') | ForEach-Object { Join-Path $toolRoot $_ }
$arguments += Join-Path $PSScriptRoot 'BackgroundChecks.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Background checks compilation failed' }
& $checkExe $testRoot
if ($LASTEXITCODE -ne 0) { throw ('Background checks failed; artifacts: ' + $testRoot) }
