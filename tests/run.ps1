$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$checkPath = Join-Path ([IO.Path]::GetTempPath()) ('glukoboy-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$exePath = Join-Path $checkPath 'Regression.exe'
$diagnosticExe = Join-Path $checkPath 'Glukoboy.Diagnostics.exe'
& (Join-Path $repoPath 'tools\diagnostics\build.cmd') $diagnosticExe
if ($LASTEXITCODE -ne 0) { throw 'Embedded diagnostics compilation failed' }
$compilerArgs = @('-nologo','-nowarn:108','-target:exe','-main:Bubliklyuk.Regression',('-out:' + $exePath),'-r:System.Windows.Forms.dll','-r:System.Drawing.dll')
$compilerArgs += '-resource:' + $diagnosticExe + ',Glukoboy.Diagnostics.exe'
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $repoPath 'resources') -Filter '*.png' | ForEach-Object { '-resource:' + $_.FullName + ',' + $_.Name })
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $repoPath 'resources') -Filter '*.wav' | ForEach-Object { '-resource:' + $_.FullName + ',' + $_.Name })
$compilerArgs += @('Включить автозапуск.cmd', 'startup.ps1' | ForEach-Object { '-resource:' + (Join-Path $repoPath ('src\' + $_)) + ',' + $_ })
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $repoPath 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$compilerArgs += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Regression compilation failed' }
& $exePath $checkPath (Join-Path $repoPath 'src\tasks.txt')
if ($LASTEXITCODE -ne 0) { throw ('Regression checks failed; artifacts: ' + $checkPath) }
& (Join-Path $PSScriptRoot 'StartupChecks.ps1') -CheckPath $checkPath
