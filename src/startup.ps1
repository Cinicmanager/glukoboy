$ErrorActionPreference = 'Stop'

function Enable-GlukoboyStartup {
    param(
        [string]$ProgramFolder = $PSScriptRoot,
        [string]$StartupFolder = [Environment]::GetFolderPath('Startup')
    )

    $exePath = [IO.Path]::GetFullPath((Join-Path $ProgramFolder 'Glukoboy.exe'))
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw 'Glukoboy.exe не найден. Положите оба файла автозапуска рядом с программой.'
    }
    if ([string]::IsNullOrWhiteSpace($StartupFolder)) {
        throw 'Не удалось определить папку автозагрузки текущего пользователя.'
    }
    $workingDirectory = [IO.Path]::GetDirectoryName($exePath)
    $startupPath = [IO.Path]::GetFullPath($StartupFolder)
    [void][IO.Directory]::CreateDirectory($startupPath)
    $shortcutPath = Join-Path $startupPath 'Glukoboy.lnk'
    $shell = $null
    $shortcut = $null
    $saved = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ((Test-Path -LiteralPath $shortcutPath -PathType Leaf) -and
            [string]::Equals($shortcut.TargetPath, $exePath, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals($shortcut.WorkingDirectory, $workingDirectory, [StringComparison]::OrdinalIgnoreCase) -and
            [string]::IsNullOrEmpty($shortcut.Arguments)) {
            Write-Host 'Ярлык автозапуска уже настроен.'
            return $shortcutPath
        }
        if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
            $backupPath = $shortcutPath + '.' + [guid]::NewGuid().ToString('N') + '.bak'
            [IO.File]::Copy($shortcutPath, $backupPath, $false)
            Write-Host ('Предыдущий ярлык сохранён: ' + $backupPath)
        }
        $shortcut.TargetPath = $exePath
        $shortcut.WorkingDirectory = $workingDirectory
        $shortcut.Arguments = ''
        $shortcut.IconLocation = $exePath + ',0'
        $shortcut.WindowStyle = 1
        $shortcut.Description = 'Глюкобой — запуск при входе в Windows'
        $shortcut.Save()
        $saved = $shell.CreateShortcut($shortcutPath)
        if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf) -or
            -not [string]::Equals($saved.TargetPath, $exePath, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals($saved.WorkingDirectory, $workingDirectory, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::IsNullOrEmpty($saved.Arguments)) {
            throw 'Не удалось проверить сохранённый ярлык автозапуска.'
        }
        return $shortcutPath
    }
    finally {
        foreach ($comObject in @($saved, $shortcut, $shell)) {
            if ($null -ne $comObject) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) }
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $shortcutPath = Enable-GlukoboyStartup
        Write-Host 'Готово! Глюкобой будет запускаться при входе этого пользователя в Windows.'
        Write-Host 'Папку с программой оставьте на месте. Если перенесёте её — запустите этот скрипт снова.'
        Write-Host ('Ярлык: ' + $shortcutPath)
    }
    catch {
        Write-Host ('Не удалось включить автозапуск: ' + $_.Exception.Message)
        exit 1
    }
}
