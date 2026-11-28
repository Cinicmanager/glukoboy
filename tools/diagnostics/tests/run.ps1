$ErrorActionPreference = 'Stop'
$toolRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('glukoboy-diagnostics-checks-' + [guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $testRoot 'fixture'
[void][IO.Directory]::CreateDirectory($fixtureRoot)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$fixtureExe = Join-Path $fixtureRoot 'Glukoboy.DiagnosticFixture.exe'
& $compiler -nologo -target:winexe ('-out:' + $fixtureExe) -r:System.Windows.Forms.dll -r:System.Drawing.dll (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
$checkExe = Join-Path $testRoot 'Checks.exe'
$arguments = @('-nologo','-target:exe',('-out:' + $checkExe),'-r:System.Windows.Forms.dll','-r:System.Drawing.dll','-r:System.Web.Extensions.dll','-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll')
$arguments += @('Collector.cs','NativeProbe.cs','ReportPackage.cs','BackgroundCaptureStore.cs') | ForEach-Object { Join-Path $toolRoot $_ }
$arguments += Join-Path $PSScriptRoot 'Checks.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic checks compilation failed' }
& $checkExe $testRoot $fixtureExe
if ($LASTEXITCODE -ne 0) { throw ('Diagnostic checks failed; artifacts: ' + $testRoot) }
