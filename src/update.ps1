$ErrorActionPreference = 'Stop'

function Test-GlukoboyRunning {
    param([string]$ExecutablePath)

    foreach ($process in @(Get-Process -Name 'Glukoboy' -ErrorAction SilentlyContinue)) {
        try {
            $processPath = $null
            try { $processPath = $process.Path }
            catch {
                if ($process.HasExited) { continue }
                throw 'Не удалось определить папку запущенного Глюкобоя. Закройте его через Ctrl+Alt+Q и повторите обновление.'
            }
            if ([string]::IsNullOrWhiteSpace($processPath)) {
                if ($process.HasExited) { continue }
                throw 'Не удалось определить папку запущенного Глюкобоя. Закройте его через Ctrl+Alt+Q и повторите обновление.'
            }
            if ([string]::Equals(
                    [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($processPath)),
                    [System.IO.Path]::GetDirectoryName($ExecutablePath),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        finally { $process.Dispose() }
    }
    return $false
}

function Assert-DownloadedExe {
    param([string]$Path, [string]$ExpectedHash)

    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, $ExpectedHash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Контрольная сумма SHA-256 не совпала. Обновление отменено.'
    }
    $inputStream = [System.IO.File]::OpenRead($Path)
    try {
        if (($inputStream.Length -lt 2) -or
            ($inputStream.ReadByte() -ne 0x4D) -or ($inputStream.ReadByte() -ne 0x5A)) {
            throw 'У скачанного файла нет заголовка MZ. Обновление отменено.'
        }
    }
    finally { $inputStream.Dispose() }
}

function Invoke-GlukoboyUpdate {
    $folder = $PSScriptRoot
    $exePath = [System.IO.Path]::GetFullPath((Join-Path -Path $folder -ChildPath 'Glukoboy.exe'))
    $newPath = $exePath + '.new'
    $oldPath = $exePath + '.old'
    $downloadOwned = $false

    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        Write-Host 'Glukoboy.exe не найден. Положите скрипты обновления рядом с ним.'
        return
    }

    try {
        $rawVersion = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
        if ([string]::IsNullOrWhiteSpace($rawVersion)) { $rawVersion = '0.0.0' }
        try {
            $parsedVersion = [version]$rawVersion.Trim()
            $localVersion = [version]('{0}.{1}.{2}.{3}' -f $parsedVersion.Major,
                $parsedVersion.Minor, [Math]::Max(0, $parsedVersion.Build),
                [Math]::Max(0, $parsedVersion.Revision))
        }
        catch { throw 'Не удалось прочитать номер версии Glukoboy.exe.' }

        Write-Host ('Установлена версия {0}. Проверяем обновления...' -f $localVersion.ToString(3))
        [Net.ServicePointManager]::SecurityProtocol =
            [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $headers = @{ Accept = 'application/vnd.github+json' }
        try {
            $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/Cinicmanager/glukoboy/releases/latest' -Method Get -UserAgent 'Glukoboy-Updater' -Headers $headers -TimeoutSec 15
        }
        catch {
            throw 'Не удалось получить последний релиз с GitHub. Проверьте интернет и наличие опубликованного релиза, затем повторите попытку.'
        }

        $tag = [string]$release.tag_name
        if ($tag -cnotmatch '\Av([0-9]+)\.([0-9]+)\.([0-9]+)\z') {
            throw 'Номер релиза имеет неверный формат. Ожидается vX.Y.Z.'
        }
        try { $remoteVersion = [version]($tag.Substring(1) + '.0') }
        catch { throw 'Номер версии релиза недопустим.' }

        if ($remoteVersion -le $localVersion) {
            Write-Host ('У вас последняя версия {0}' -f $localVersion.ToString(3))
            return
        }

        $exeAssets = @($release.assets | Where-Object { $_.name -ceq 'Glukoboy.exe' })
        $hashAssets = @($release.assets | Where-Object { $_.name -ceq 'Glukoboy.exe.sha256' })
        if (($exeAssets.Count -ne 1) -or ($hashAssets.Count -ne 1)) {
            throw 'В релизе должны быть ровно один Glukoboy.exe и один Glukoboy.exe.sha256.'
        }
        $exeUrl = [string]$exeAssets[0].browser_download_url
        $hashUrl = [string]$hashAssets[0].browser_download_url
        $prefix = 'https://github.com/Cinicmanager/glukoboy/releases/download/'
        if ((-not $exeUrl.StartsWith($prefix, [System.StringComparison]::Ordinal)) -or
            (-not $hashUrl.StartsWith($prefix, [System.StringComparison]::Ordinal))) {
            throw 'Адреса файлов релиза не принадлежат разрешённому разделу GitHub. Обновление отменено.'
        }

        Write-Host ('Скачиваем версию {0}...' -f $remoteVersion.ToString(3))
        $downloadOwned = $true
        try {
            Invoke-WebRequest -Uri $exeUrl -UseBasicParsing -UserAgent 'Glukoboy-Updater' -TimeoutSec 120 -OutFile $newPath
            $hashResponse = Invoke-WebRequest -Uri $hashUrl -UseBasicParsing -UserAgent 'Glukoboy-Updater' -TimeoutSec 15
        }
        catch { throw 'Не удалось скачать обновление или контрольную сумму. Проверьте интернет, место на диске и права записи в папку.' }

        if ($hashResponse.Content -is [byte[]]) {
            $hashText = [System.Text.Encoding]::UTF8.GetString($hashResponse.Content)
        }
        else { $hashText = [string]$hashResponse.Content }
        $hashText = $hashText.TrimStart([char]0xFEFF).Trim()
        $expectedHash = ($hashText -split '\s+')[0]
        if ($expectedHash -cnotmatch '\A[a-fA-F0-9]{64}\z') {
            throw 'Файл контрольной суммы имеет неверный формат. Обновление отменено.'
        }
        Assert-DownloadedExe -Path $newPath -ExpectedHash $expectedHash

        while (Test-GlukoboyRunning -ExecutablePath $exePath) {
            [void](Read-Host 'Закройте Глюкобой: Ctrl+Alt+Q, затем нажмите Enter')
        }
        # Пока родитель закрывал программу, временный файл мог измениться.
        Assert-DownloadedExe -Path $newPath -ExpectedHash $expectedHash

        $movedOld = $false
        $installedNew = $false
        try {
            if (Test-Path -LiteralPath $oldPath) {
                Remove-Item -LiteralPath $oldPath -Force
            }
            [System.IO.File]::Move($exePath, $oldPath)
            $movedOld = $true
            [System.IO.File]::Move($newPath, $exePath)
            $installedNew = $true
            Unblock-File -LiteralPath $exePath
            Remove-Item -LiteralPath $oldPath -Force
            $downloadOwned = $false
        }
        catch {
            if ($movedOld) {
                try {
                    if ($installedNew) {
                        [System.IO.File]::Move($exePath, $newPath)
                    }
                    [System.IO.File]::Move($oldPath, $exePath)
                }
                catch {
                    $downloadOwned = $false
                    throw 'Не удалось завершить замену и вернуть старый файл. Сохранённые файлы Glukoboy.exe.old и Glukoboy.exe.new могут понадобиться для ручного восстановления.'
                }
                throw 'Не удалось заменить файл программы. Старый Glukoboy.exe восстановлен. Проверьте права записи и закройте программу через Ctrl+Alt+Q.'
            }
            throw 'Не удалось подготовить замену программы. Текущий Glukoboy.exe сохранён.'
        }

        try {
            Start-Process -FilePath $exePath -WorkingDirectory $folder -ErrorAction Stop
        }
        catch {
            Write-Host ('Файл обновлён до {0}, но запустить Глюкобой не удалось. Запустите Glukoboy.exe вручную.' -f $remoteVersion.ToString(3))
            return
        }
        Write-Host ('Обновлено до {0}' -f $remoteVersion.ToString(3))
    }
    finally {
        if ($downloadOwned) {
            try {
                if (Test-Path -LiteralPath $newPath -PathType Leaf) {
                    Remove-Item -LiteralPath $newPath -Force
                }
            }
            catch { Write-Host 'Не удалось удалить временный Glukoboy.exe.new. Его можно удалить вручную.' }
        }
    }
}

try { Invoke-GlukoboyUpdate }
catch { Write-Host ('Обновление не выполнено: {0}' -f $_.Exception.Message) }
finally { [void](Read-Host 'Нажмите Enter') }
