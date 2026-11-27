param([Parameter(Mandatory = $true)][string]$CheckPath)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\startup.ps1')

function Assert-Startup([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# All shortcuts go into the test directory, never the user's actual Startup.
$testRoot = Join-Path $CheckPath 'Автозапуск ! & [проверка]'
$programFolder = Join-Path $testRoot 'Программа'
$startupFolder = Join-Path $testRoot 'Startup'
[void][IO.Directory]::CreateDirectory($programFolder)
$missingRejected = $false
try { Enable-GlukoboyStartup -ProgramFolder $programFolder -StartupFolder $startupFolder }
catch { $missingRejected = $true }
Assert-Startup $missingRejected 'Missing exe must be rejected'
Assert-Startup (-not [IO.Directory]::Exists($startupFolder)) 'Missing exe must not create startup files'
$exePath = Join-Path $programFolder 'Glukoboy.exe'
[IO.File]::WriteAllText($exePath, 'Test file; never executed')
$linkPath = Enable-GlukoboyStartup -ProgramFolder $programFolder -StartupFolder $startupFolder
$shell = New-Object -ComObject WScript.Shell
$first = $null
$retargeted = $null
try {
    $first = $shell.CreateShortcut($linkPath)
    Assert-Startup ($first.TargetPath -eq $exePath) 'Shortcut target must be the neighboring exe'
    Assert-Startup ($first.WorkingDirectory -eq $programFolder) 'Shortcut working directory must follow the exe'
    Assert-Startup ([string]::IsNullOrEmpty($first.Arguments)) 'No unwanted command-line arguments'
    $originalHash = (Get-FileHash -LiteralPath $linkPath).Hash
    $again = Enable-GlukoboyStartup -ProgramFolder $programFolder -StartupFolder $startupFolder
    Assert-Startup ($again -eq $linkPath -and (Get-FileHash -LiteralPath $linkPath).Hash -eq $originalHash) 'Repeated enable must preserve an already correct shortcut'
    Assert-Startup (@(Get-ChildItem -LiteralPath $startupFolder -Filter '*.bak').Count -eq 0) 'Repeated enable must not create redundant backups'

    $movedFolder = Join-Path $testRoot 'Новая папка'
    [void][IO.Directory]::CreateDirectory($movedFolder)
    $movedExe = Join-Path $movedFolder 'Glukoboy.exe'
    [IO.File]::WriteAllText($movedExe, 'Another test file; never executed')
    $updated = Enable-GlukoboyStartup -ProgramFolder $movedFolder -StartupFolder $startupFolder
    $retargeted = $shell.CreateShortcut($updated)
    Assert-Startup ($retargeted.TargetPath -eq $movedExe -and $retargeted.WorkingDirectory -eq $movedFolder) 'Moving the program must retarget the shortcut'
    $backups = @(Get-ChildItem -LiteralPath $startupFolder -Filter '*.bak')
    Assert-Startup ($backups.Count -eq 1 -and (Get-FileHash -LiteralPath $backups[0].FullName).Hash -eq $originalHash) 'Previous shortcut must be backed up intact'
    Write-Output 'PASS: startup shortcut creation, repeat, relocation, backup, missing-exe and Cyrillic path checks; real Startup untouched'
}
finally {
    foreach ($comObject in @($retargeted, $first, $shell)) {
        if ($null -ne $comObject) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) }
    }
}
