# install.ps1 — установка плагина MarkdownLister в Total Commander.
#
# Что делает:
#   1) находит Total Commander (параметр, %COMMANDER_PATH%, реестр);
#   2) собирает комплект плагина из dist\x64 (общие файлы) + dist\x86\MarkdownLister.wlx
#      (TC 32-битный берёт .wlx, 64-битный — .wlx64 рядом с ним);
#   3) копирует комплект в <TC>\Plugins\wlx\MarkdownLister\;
#   4) регистрирует плагин в wincmd.ini: [ListerPlugins] — запись с detect-строкой
#      для расширений MD/MARKDOWN/MKD/MKDN/MDOWN/MDTEXT/MDX, [ListerPlugins64] —
#      компактное зеркало N=1 (TC берёт MarkdownLister.wlx64 рядом с .wlx).
#
# После установки перезапустите Total Commander.
# Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.
[CmdletBinding()]
param(
    # Путь к каталогу Total Commander (иначе — поиск автоматический)
    [string]$TotalCommanderDir,

    # Путь к wincmd.ini (иначе — поиск автоматический)
    [string]$IniPath,

    # Каталог с дистрибутивом dist\ (по умолчанию — папка сборки dist\, два уровня выше)
    [string]$SourceDir,

    # Только скопировать файлы, не трогая wincmd.ini
    [switch]$NoRegister
)

$ErrorActionPreference = 'Stop'
$setupDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SourceDir) { $SourceDir = Join-Path $setupDir '..\..' }
$detect = '"EXT="MD" | EXT="MARKDOWN" | EXT="MKD" | EXT="MKDN" | EXT="MDOWN" | EXT="MDTEXT" | EXT="MDX""'

function Find-TotalCommander {
    if ($TotalCommanderDir) {
        if (-not (Test-Path $TotalCommanderDir)) { throw "Каталог не найден: $TotalCommanderDir" }
        return (Resolve-Path $TotalCommanderDir).Path
    }
    if ($env:COMMANDER_PATH -and (Test-Path $env:COMMANDER_PATH)) { return $env:COMMANDER_PATH }
    foreach ($key in 'HKCU:\Software\Ghisler\Total Commander',
                     'HKLM:\SOFTWARE\WOW6432Node\Ghisler\Total Commander',
                     'HKLM:\SOFTWARE\Ghisler\Total Commander') {
        $dir = (Get-ItemProperty $key -Name InstallDir -ErrorAction SilentlyContinue).InstallDir
        if ($dir -and (Test-Path $dir)) { return $dir }
    }
    throw 'Total Commander не найден. Укажите путь: -TotalCommanderDir "C:\totalcmd"'
}

function Find-WincmdIni {
    param([string]$TcDir)
    if ($IniPath) {
        if (-not (Test-Path $IniPath)) { throw "INI-файл не найден: $IniPath" }
        return $IniPath
    }
    # TC ищет wincmd.ini в таком же порядке; COMMANDER_INI задаётся им же
    # для дочерних процессов — самый надёжный источник.
    $candidates = @(
        $env:COMMANDER_INI
        (Get-ItemProperty 'HKCU:\Software\Ghisler\Total Commander' -Name IniLocation -ErrorAction SilentlyContinue).IniLocation
        (Join-Path $TcDir 'wincmd.ini')
        (Join-Path $env:APPDATA 'Ghisler\wincmd.ini')
        (Join-Path $env:WINDIR 'wincmd.ini')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    throw 'wincmd.ini не найден. Укажите путь: -IniPath "C:\totalcmd\wincmd.ini"'
}

# Идемпотентно переписывает секцию плагинов в wincmd.ini: прежние записи
# MarkdownLister (вместе с N_detect) удаляются, новая — добавляется под номером
# $ForceNumber, иначе — минимальным освободившимся либо первым свободным.
# Номера из $DropNumbers удаляются независимо от значения записи (нужно для
# компактных зеркал вида N=1 в [ListerPlugins64]). Прочие строки секции,
# включая $checksum$, сохраняются.
# Возвращает @{ Number = номер новой записи; Dropped = @(удалённые номера) }.
function Register-PluginEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Ini,
        [Parameter(Mandatory = $true)][string]$SectionName,
        [Parameter(Mandatory = $true)][string]$EntryValue,
        [string]$DetectString = '',
        [int[]]$DropNumbers = @(),
        [int]$ForceNumber = -1
    )
    $lines = if (Test-Path $Ini) { @(Get-Content $Ini) } else { @() }

    # Собираем файл заново: прежние записи плагина удаляем, новую — добавляем.
    $kept = New-Object System.Collections.Generic.List[string]
    $section = New-Object System.Collections.Generic.List[string]
    $inSection = $false
    $sectionSeen = $false
    $state = @{ Num = -1; Dropped = @() }

    $flush = {
        $drop = @{}
        foreach ($s in $section) {
            if ($s -match '^\s*(\d+)=(.*)$') {
                # Номер и значение сохраняем сразу: следующий -match перезаписал бы $Matches
                $num = [int]$Matches[1]
                $val = $Matches[2]
                if ($val -match 'MarkdownLister' -or
                    $DropNumbers -contains $num -or $num -eq $ForceNumber) {
                    $drop[$num] = $true
                }
            }
        }
        $maxNum = -1
        foreach ($s in $section) {
            if ($s -match '^\s*(\d+)=(.*)$') {
                # Запись плагина (N=...) — удаляем, число сохраняем до следующих -match
                $num = [int]$Matches[1]
                if ($drop.ContainsKey($num)) { continue }
                if ($num -gt $maxNum) { $maxNum = $num }
                $kept.Add($s)
                continue
            }
            if ($s -match '^\s*(\d+)_\w+=') {
                # Спутанные строки (N_detect=...) удаляем вместе с записью
                if ($drop.ContainsKey([int]$Matches[1])) { continue }
            }
            $kept.Add($s)
        }
        if ($ForceNumber -ge 0) {
            $num = $ForceNumber
        }
        elseif ($drop.Count -gt 0) {
            # Идемпотентность: переиспользуем освободившийся номер плагина
            $num = [int](($drop.Keys | Sort-Object | Select-Object -First 1))
        }
        else {
            $num = $maxNum + 1
        }
        $kept.Add("$num=$EntryValue")
        if ($DetectString) { $kept.Add("${num}_detect=$DetectString") }
        $state.Num = $num
        $state.Dropped = @($drop.Keys)
        $section.Clear()
    }

    foreach ($line in $lines) {
        if ($line -match '^\s*\[(.+)\]\s*$') {
            if ($inSection) { & $flush }
            $inSection = ($Matches[1] -ieq $SectionName)
            $sectionSeen = $inSection -or $sectionSeen
            $kept.Add($line)
            continue
        }
        if ($inSection) { $section.Add($line) } else { $kept.Add($line) }
    }
    if ($inSection) { & $flush }

    if (-not $sectionSeen) {
        $kept.Add("[$SectionName]")
        $kept.Add("0=$EntryValue")
        if ($DetectString) { $kept.Add("0_detect=$DetectString") }
        $state.Num = 0
        $state.Dropped = @()
    }
    Set-Content -Path $Ini -Value $kept -Encoding Default
    return @{ Number = $state.Num; Dropped = $state.Dropped }
}

# --- 1. Total Commander и INI ---
$tcDir = Find-TotalCommander
Write-Host "Total Commander: $tcDir"

# --- 2. Комплект: dist\ при сборке из репозитория либо папка MarkdownLister\
#        рядом со скриптом (режим архива релиза) ---
$commonDir = Join-Path $SourceDir 'x64'
$wlx86 = Join-Path $SourceDir 'x86\MarkdownLister.wlx'
$wlx64 = Join-Path $SourceDir 'x64\MarkdownLister.wlx64'
if (-not ((Test-Path $wlx86) -and (Test-Path $wlx64))) {
    $flatDir = Join-Path $setupDir 'MarkdownLister'
    $commonDir = $flatDir
    $wlx86 = Join-Path $flatDir 'MarkdownLister.wlx'
    $wlx64 = Join-Path $flatDir 'MarkdownLister.wlx64'
}
if (-not (Test-Path $wlx86)) { throw "Не найден $wlx86 — соберите плагин: build.ps1 либо возьмите архив релиза" }
if (-not (Test-Path $wlx64)) { throw "Не найден $wlx64 — соберите плагин: build.ps1 либо возьмите архив релиза" }

# --- 3. Копирование: общие файлы из x64 + .wlx (x86) + .wlx64 (x64) ---
$pluginDir = Join-Path $tcDir 'Plugins\wlx\MarkdownLister'
Write-Host "Копирование в $pluginDir ..."
if (Test-Path $pluginDir) {
    try { Remove-Item $pluginDir -Recurse -Force -ErrorAction Stop }
    catch {
        throw "Не могу удалить старые файлы плагина — скорее всего Total Commander запущен и держит их. Закройте Total Commander и повторите установку."
    }
}
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item (Join-Path $commonDir '*') $pluginDir -Recurse -Force
Remove-Item (Join-Path $pluginDir 'MarkdownLister.wlx') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $pluginDir 'MarkdownLister.wlx64') -Force -ErrorAction SilentlyContinue
Copy-Item $wlx86 (Join-Path $pluginDir 'MarkdownLister.wlx') -Force
Copy-Item $wlx64 (Join-Path $pluginDir 'MarkdownLister.wlx64') -Force

# Файлы из распакованного архива релиза могут нести Zone.Identifier (Mark of
# the Web) — копирование переносит метку и ломает загрузку зависимостей плагина.
# Снимаем метку со всех файлов установки.
Get-ChildItem $pluginDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# --- 4. Регистрация в wincmd.ini ---
if ($NoRegister) {
    Write-Host 'Регистрация в wincmd.ini пропущена (-NoRegister)'
}
else {
    $ini = Find-WincmdIni -TcDir $tcDir
    $reg = Register-PluginEntry -Ini $ini -SectionName 'ListerPlugins' `
        -EntryValue '%COMMANDER_PATH%\Plugins\wlx\MarkdownLister\MarkdownLister.wlx' `
        -DetectString $detect
    Write-Host "  [ListerPlugins]: запись $($reg.Number)"

    # 64-битный TC: компактное зеркало N=1 — берёт MarkdownLister.wlx64 рядом с .wlx
    Register-PluginEntry -Ini $ini -SectionName 'ListerPlugins64' `
        -EntryValue '1' -ForceNumber $reg.Number -DropNumbers $reg.Dropped | Out-Null
    Write-Host "  [ListerPlugins64]: зеркало $($reg.Number)=1"
}

Write-Host 'Готово. Перезапустите Total Commander.' -ForegroundColor Green
