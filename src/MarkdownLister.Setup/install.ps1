# install.ps1 — установка плагина MarkdownLister в Total Commander.
#
# Что делает:
#   1) находит Total Commander (параметр, %COMMANDER_PATH%, реестр);
#   2) собирает комплект плагина из dist\x64 (общие файлы) + dist\x86\MarkdownLister.wlx
#      (TC 32-битный берёт .wlx, 64-битный — .wlx64 рядом с ним);
#   3) копирует комплект в <TC>\Plugins\wlx\MarkdownLister\;
#   4) регистрирует плагин в wincmd.ini ([ListerPlugins]) с detect-строкой
#      для расширений MD/MARKDOWN/MKD/MDOWN/MDX.
#
# После установки перезапустите Total Commander.
# Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.
[CmdletBinding()]
param(
    # Путь к каталогу Total Commander (иначе — поиск автоматический)
    [string]$TotalCommanderDir,

    # Путь к wincmd.ini (иначе — поиск автоматический)
    [string]$IniPath,

    # Каталог с дистрибутивом dist\ (по умолчанию ..\..\dist от этого скрипта)
    [string]$SourceDir,

    # Только скопировать файлы, не трогая wincmd.ini
    [switch]$NoRegister
)

$ErrorActionPreference = 'Stop'
$setupDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SourceDir) { $SourceDir = Join-Path $setupDir '..\..\dist' }
$detect = '"EXT="MD" | EXT="MARKDOWN" | EXT="MKD" | EXT="MDOWN" | EXT="MDX""'

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

function Register-Plugin {
    param([string]$Ini, [string]$PluginEntry, [string]$DetectString)
    $lines = if (Test-Path $Ini) { @(Get-Content $Ini) } else { @() }

    # Собираем файл заново: прежние записи плагина (вместе с их _detect) удаляем
    # для идемпотентности, новую — добавляем с первым свободным номером.
    $kept = New-Object System.Collections.Generic.List[string]
    $section = New-Object System.Collections.Generic.List[string]
    $inSection = $false

    $flush = {
        if ($section.Count -eq 0 -and -not $script:sectionSeen) { return }
        $drop = @{}
        $maxNum = -1
        foreach ($s in $section) {
            if ($s -match '^\s*(\d+)=') {
                $num = [int]$Matches[1]
                if ($num -gt $maxNum) { $maxNum = $num }
                if ($s -match 'MarkdownLister\.wlx') { $drop[$Matches[1]] = $true }
            }
        }
        foreach ($s in $section) {
            if ($s -match '^\s*(\d+)_\w+=') {
                if ($drop.ContainsKey($Matches[1])) { continue }
            }
            $kept.Add($s)
        }
        $num = $maxNum + 1
        $kept.Add("$num=$PluginEntry")
        $kept.Add("${num}_detect=$DetectString")
        $script:newEntryNum = $num
        $section.Clear()
    }

    foreach ($line in $lines) {
        if ($line -match '^\s*\[(.+)\]\s*$') {
            if ($inSection) { & $flush }
            $inSection = ($Matches[1] -ieq 'ListerPlugins')
            $script:sectionSeen = $inSection -or $script:sectionSeen
            $kept.Add($line)
            continue
        }
        if ($inSection) { $section.Add($line) } else { $kept.Add($line) }
    }
    if ($inSection) { & $flush }

    if (-not $script:sectionSeen) {
        $kept.Add('[ListerPlugins]')
        $kept.Add("0=$PluginEntry")
        $kept.Add("0_detect=$DetectString")
        $script:newEntryNum = 0
    }
    Set-Content -Path $Ini -Value $kept -Encoding Default
    Write-Host "  Зарегистрирован в $Ini (запись $($script:newEntryNum))"
}

# --- 1. Total Commander и INI ---
$tcDir = Find-TotalCommander
Write-Host "Total Commander: $tcDir"

# --- 2. Комплект из dist ---
$x64 = Join-Path $SourceDir 'x64'
$x86 = Join-Path $SourceDir 'x86'
$wlx86 = Join-Path $x86 'MarkdownLister.wlx'
$wlx64 = Join-Path $x64 'MarkdownLister.wlx64'
if (-not (Test-Path $wlx86)) { throw "Не найден $wlx86 — соберите плагин: build.ps1" }
if (-not (Test-Path $wlx64)) { throw "Не найден $wlx64 — соберите плагин: build.ps1" }

# --- 3. Копирование: общие файлы из x64 + .wlx (x86) + .wlx64 (x64) ---
$pluginDir = Join-Path $tcDir 'Plugins\wlx\MarkdownLister'
Write-Host "Копирование в $pluginDir ..."
if (Test-Path $pluginDir) { Remove-Item $pluginDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item (Join-Path $x64 '*') $pluginDir -Recurse -Force
Remove-Item (Join-Path $pluginDir 'MarkdownLister.wlx64') -Force -ErrorAction SilentlyContinue
Copy-Item $wlx86 (Join-Path $pluginDir 'MarkdownLister.wlx') -Force
Copy-Item $wlx64 (Join-Path $pluginDir 'MarkdownLister.wlx64') -Force

# --- 4. Регистрация в wincmd.ini ---
if ($NoRegister) {
    Write-Host 'Регистрация в wincmd.ini пропущена (-NoRegister)'
}
else {
    $ini = Find-WincmdIni -TcDir $tcDir
    Register-Plugin -Ini $ini -PluginEntry '%COMMANDER_PATH%\Plugins\wlx\MarkdownLister\MarkdownLister.wlx' -DetectString $detect
}

Write-Host 'Готово. Перезапустите Total Commander.' -ForegroundColor Green
