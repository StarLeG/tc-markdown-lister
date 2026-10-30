# uninstall.ps1 — удаление плагина MarkdownLister из Total Commander.
#
# Что делает:
#   1) удаляет запись плагина из wincmd.ini ([ListerPlugins], с перенумерацией);
#   2) удаляет папку <TC>\Plugins\wlx\MarkdownLister\.
#
# Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.
[CmdletBinding()]
param(
    # Путь к каталогу Total Commander (иначе — поиск автоматический)
    [string]$TotalCommanderDir,

    # Путь к wincmd.ini (иначе — поиск автоматический)
    [string]$IniPath,

    # Оставить файлы плагина (удалить только запись в wincmd.ini)
    [switch]$KeepFiles
)

$ErrorActionPreference = 'Stop'

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

# Перенумеровывает секцию [ListerPlugins] без записей MarkdownLister:
# TC читает номера подряд (0,1,2,...), поэтому дыры недопустимы.
function Rebuild-ListerPluginsSection {
    param([string[]]$SectionLines)
    $drop = @{}
    foreach ($s in $SectionLines) {
        if ($s -match '^\s*(\d+)=.*MarkdownLister\.wlx') { $drop[$Matches[1]] = $true }
    }
    $map = @{}
    $n = 0
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($s in $SectionLines) {
        if ($s -match '^\s*(\d+)=(.*)$') {
            $old = $Matches[1]
            if ($drop.ContainsKey($old)) { continue }
            $map[$old] = $n
            $out.Add("$n=$($Matches[2])")
            $n++
        }
        elseif ($s -match '^\s*(\d+)_(\w+)=(.*)$') {
            $old = $Matches[1]
            if ($drop.ContainsKey($old)) { continue }
            if ($map.ContainsKey($old)) { $out.Add("$($map[$old])_$($Matches[2])=$($Matches[3])") }
            else { $out.Add($s) }
        }
        else {
            $out.Add($s)
        }
    }
    return $out
}

function Unregister-Plugin {
    param([string]$Ini)
    if (-not (Test-Path $Ini)) {
        Write-Host "  INI-файл не найден, пропуск: $Ini"
        return
    }
    $lines = @(Get-Content $Ini)
    $result = New-Object System.Collections.Generic.List[string]
    $section = New-Object System.Collections.Generic.List[string]
    $inSection = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*\[(.+)\]\s*$') {
            if ($inSection) {
                foreach ($x in (Rebuild-ListerPluginsSection -SectionLines $section.ToArray())) { $result.Add($x) }
                $section.Clear()
            }
            $inSection = ($Matches[1] -ieq 'ListerPlugins')
            $result.Add($line)
            continue
        }
        if ($inSection) { $section.Add($line) } else { $result.Add($line) }
    }
    if ($inSection) {
        foreach ($x in (Rebuild-ListerPluginsSection -SectionLines $section.ToArray())) { $result.Add($x) }
    }
    Set-Content -Path $Ini -Value $result -Encoding Default
    Write-Host "  Запись удалена из $Ini"
}

# --- 1. Регистрация ---
$tcDir = Find-TotalCommander
if ($IniPath) {
    $ini = $IniPath
}
else {
    # Порядок поиска как у Total Commander: COMMANDER_INI (задаётся самим TC),
    # реестр, папка программы, профиль пользователя, %WINDIR%.
    $ini = $null
    foreach ($c in @(
            $env:COMMANDER_INI
            (Get-ItemProperty 'HKCU:\Software\Ghisler\Total Commander' -Name IniLocation -ErrorAction SilentlyContinue).IniLocation
            (Join-Path $tcDir 'wincmd.ini')
            (Join-Path $env:APPDATA 'Ghisler\wincmd.ini')
            (Join-Path $env:WINDIR 'wincmd.ini'))) {
        if ($c -and (Test-Path $c)) { $ini = $c; break }
    }
    if (-not $ini) { throw 'wincmd.ini не найден. Укажите путь: -IniPath "C:\totalcmd\wincmd.ini"' }
}
Unregister-Plugin -Ini $ini

# --- 2. Файлы ---
if ($KeepFiles) {
    Write-Host 'Файлы плагина оставлены (-KeepFiles)'
}
else {
    $pluginDir = Join-Path $tcDir 'Plugins\wlx\MarkdownLister'
    if (Test-Path $pluginDir) {
        Remove-Item $pluginDir -Recurse -Force
        Write-Host "  Удалён $pluginDir"
    }
}

Write-Host 'Готово. Перезапустите Total Commander.' -ForegroundColor Green
