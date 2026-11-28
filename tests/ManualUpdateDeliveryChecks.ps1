param(
    [Parameter(Mandatory = $true)][string]$OldExe,
    [Parameter(Mandatory = $true)][string]$NewExe
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('glukoboy-update-delivery-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$script:replacementExe = [IO.Path]::GetFullPath($NewExe)
$script:replacementHash = (Get-FileHash -LiteralPath $script:replacementExe -Algorithm SHA256).Hash
$script:launched = New-Object 'System.Collections.Generic.List[string]'
$script:requested = New-Object 'System.Collections.Generic.List[string]'
$script:downloadPrefix = 'https://github.com/Cinicmanager/glukoboy/releases/download/v1.7.3/'
$installedExe = Join-Path $testRoot 'Glukoboy.exe'
$global:GlukoboyDeliveryFixture = @{
    Source = $script:replacementExe; Hash = $script:replacementHash
    Launched = $script:launched; Requested = $script:requested
    Prefix = $script:downloadPrefix; Installed = $installedExe; Root = $testRoot
}
Copy-Item -LiteralPath $OldExe -Destination $installedExe
Copy-Item -LiteralPath (Join-Path $repoPath 'src\update.ps1') -Destination (Join-Path $testRoot 'update.ps1')
$settingsPath = Join-Path $testRoot 'settings.ini'
[IO.File]::WriteAllText($settingsPath, 'Parent settings must survive this update.')

# Exercise the unchanged manual updater with local downloads and a launch spy.
# No network, running-game lookup, keyboard prompt or real EXE launch is used.
function Invoke-RestMethod {
    [CmdletBinding()] param([string]$Uri, [string]$Method, [string]$UserAgent, $Headers, [int]$TimeoutSec)
    if ($Uri -ne 'https://api.github.com/repos/Cinicmanager/glukoboy/releases/latest') { throw 'Unexpected API address' }
    [void]$global:GlukoboyDeliveryFixture.Requested.Add($Uri)
    return [pscustomobject]@{
        tag_name = 'v1.7.3'
        assets = @(
            [pscustomobject]@{ name = 'Glukoboy.exe'; browser_download_url = $global:GlukoboyDeliveryFixture.Prefix + 'Glukoboy.exe' },
            [pscustomobject]@{ name = 'Glukoboy.exe.sha256'; browser_download_url = $global:GlukoboyDeliveryFixture.Prefix + 'Glukoboy.exe.sha256' }
        )
    }
}
function Invoke-WebRequest {
    [CmdletBinding()] param([string]$Uri, [switch]$UseBasicParsing, [string]$UserAgent, [int]$TimeoutSec, [string]$OutFile)
    [void]$global:GlukoboyDeliveryFixture.Requested.Add($Uri)
    if ($Uri -eq ($global:GlukoboyDeliveryFixture.Prefix + 'Glukoboy.exe')) {
        if ([IO.Path]::GetFullPath($OutFile) -ne ($global:GlukoboyDeliveryFixture.Installed + '.new')) { throw 'Unexpected download destination' }
        Copy-Item -LiteralPath $global:GlukoboyDeliveryFixture.Source -Destination $OutFile
        return
    }
    if ($Uri -eq ($global:GlukoboyDeliveryFixture.Prefix + 'Glukoboy.exe.sha256')) {
        return [pscustomobject]@{ Content = $global:GlukoboyDeliveryFixture.Hash + '  Glukoboy.exe' }
    }
    throw 'Unexpected download address'
}
function Get-Process { [CmdletBinding()] param([string]$Name) return @() }
function Read-Host { param([string]$Prompt) return '' }
function Start-Process {
    [CmdletBinding()] param([string]$FilePath, [string]$WorkingDirectory)
    if ($FilePath -ne $global:GlukoboyDeliveryFixture.Installed -or $WorkingDirectory -ne $global:GlukoboyDeliveryFixture.Root) { throw 'Unexpected launch destination' }
    [void]$global:GlukoboyDeliveryFixture.Launched.Add($FilePath)
}
try { & (Join-Path $testRoot 'update.ps1') }
finally { Remove-Variable -Name GlukoboyDeliveryFixture -Scope Global }
if ((Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash -ne $script:replacementHash) { throw 'Update did not install the exact new EXE' }
if ($script:launched.Count -ne 1) { throw 'Updater must request exactly one game launch' }
if ($script:requested.Count -ne 3) { throw 'Updater should download only release metadata, EXE and checksum' }
if ([IO.File]::ReadAllText($settingsPath) -ne 'Parent settings must survive this update.') { throw 'Parent settings changed' }
if (Test-Path -LiteralPath ($installedExe + '.new')) { throw 'Unfinished download remains' }
if (Test-Path -LiteralPath (Join-Path $testRoot 'Glukoboy.Diagnostics.exe')) { throw 'Updater unexpectedly requires a separate helper file' }
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($installedExe)
$resource = $assembly.GetManifestResourceStream('Glukoboy.Diagnostics.exe')
if ($null -eq $resource) { throw 'EXE-only update did not deliver the embedded collector' }
try {
    if ($resource.Length -lt 1024 -or $resource.ReadByte() -ne 0x4D -or $resource.ReadByte() -ne 0x5A) { throw 'Invalid embedded collector payload' }
} finally { $resource.Dispose() }
Write-Output ('PASS: EXE-only manual update delivers diagnostics, preserves settings and requests launch; ' + $testRoot)
