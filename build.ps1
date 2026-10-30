# build.ps1 — сборка MarkdownLister (WLX-плагин для Total Commander).
#
# Выход: dist\x86\MarkdownLister.wlx и dist\x64\MarkdownLister.wlx64
# с полным набором зависимостей (Core, Markdig, WebView2, web\).
#
# После каждой платформы выполняется fail-fast проверка unmanaged-экспорта
# WLX-функций (11 шт.): без них Lister не увидит плагин, а обычная сборка
# MSBuild при этом «зелёная» — проверять обязательно.
#
# Требования: Visual Studio 2022 BuildTools/IDE (MSBuild) + NuGet-пакеты.
# Версия берётся из Directory.Build.props и в скрипте не хардкодится.
#
# Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.
[CmdletBinding()]
param(
    # Конфигурация сборки
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    # Только пересобрать и проверить, без копирования в dist
    [switch]$NoDist
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$wlxProj = Join-Path $root 'src\MarkdownLister.WLX\MarkdownLister.WLX.csproj'

# --- 1. MSBuild: только полный (VS), dotnet build не подходит (net48 + DllExport) ---
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($vsPath) {
        $msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
    }
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw 'MSBuild (VS 2022) не найден. Установите Visual Studio 2022 или BuildTools.'
}
Write-Host "[1/6] MSBuild: $msbuild"

# --- 2. MSBuildSDKsPath: у BuildTools MSBuild может не быть каталога Sdks ---
$msbuildSdks = Join-Path (Split-Path $msbuild) 'Sdks'
if (Test-Path (Join-Path $msbuildSdks 'Microsoft.NET.Sdk')) {
    $env:MSBuildSDKsPath = $null
    Write-Host '[2/6] Sdks: штатные (рядом с MSBuild)'
}
else {
    # У BuildTools-MSBuild нет каталога Sdks: берём его из dotnet SDK.
    # Microsoft.NET.Sdk импортирует два Workload-локатора, которых в dotnet SDK
    # тоже нет — добавляем их как пустые заглушки (иначе MSB4236 при restore).
    $staging = Join-Path $root '.build\Sdks'
    if (-not (Test-Path (Join-Path $staging 'Microsoft.NET.Sdk'))) {
        $sdkSdks = Get-ChildItem "${env:ProgramFiles}\dotnet\sdk" -Directory -ErrorAction SilentlyContinue |
            Sort-Object { try { [version]($_.Name -replace '[^\d.].*$', '') } catch { [version]'0.0' } } -Descending |
            ForEach-Object { $p = Join-Path $_.FullName 'Sdks'; if (Test-Path (Join-Path $p 'Microsoft.NET.Sdk')) { $p } } |
            Select-Object -First 1
        if (-not $sdkSdks) { throw 'Каталог Sdks (Microsoft.NET.Sdk) не найден ни у MSBuild, ни в dotnet\sdk.' }
        Write-Host "[2/6] Sdks: staging из $sdkSdks -> $staging"
        New-Item -ItemType Directory -Force -Path $staging | Out-Null
        Copy-Item (Join-Path $sdkSdks '*') $staging -Recurse -Force
    }
    else {
        Write-Host "[2/6] Sdks: staging $staging (готов)"
    }
    foreach ($stub in @(
            'Microsoft.NET.SDK.WorkloadAutoImportPropsLocator\Sdk\AutoImport.props',
            'Microsoft.NET.SDK.WorkloadManifestTargetsLocator\Sdk\WorkloadManifest.targets')) {
        $stubPath = Join-Path $staging $stub
        if (-not (Test-Path $stubPath)) {
            New-Item -ItemType Directory -Force -Path (Split-Path $stubPath) | Out-Null
            Set-Content -Path $stubPath -Encoding UTF8 -Value ('<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">' + "`n</Project>")
        }
    }
    $env:MSBuildSDKsPath = $staging
}

# --- 3. Restore ---
Write-Host '[3/6] NuGet restore...'
& $msbuild $wlxProj -t:Restore -v:q -nologo
if ($LASTEXITCODE -ne 0) { throw "Restore завершился с ошибкой ($LASTEXITCODE)" }

# --- 4. Fail-fast проверка экспорта PE (11 WLX-функций) ---
$expected = @(
    'ListCloseWindow', 'ListGetDetectString', 'ListLoad', 'ListLoadNext',
    'ListLoadNextW', 'ListLoadW', 'ListNotificationReceived', 'ListPrint',
    'ListSearchDialog', 'ListSendCommand', 'ListSetDefaultParams'
)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

public static class PeExports
{
    public static string[] Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(b, 0x3C);
        int opt = pe + 24;
        int magic = BitConverter.ToUInt16(b, opt);
        int dirOff = opt + (magic == 0x20B ? 112 : 96); // PE32+ / PE32
        int expRva = BitConverter.ToInt32(b, dirOff);
        if (expRva == 0) return new string[0];

        int nSec = BitConverter.ToUInt16(b, pe + 6);
        int secOff = opt + BitConverter.ToUInt16(b, pe + 20);
        Func<int, int> rva2off = rva =>
        {
            for (int i = 0; i < nSec; i++)
            {
                int s = secOff + 40 * i;
                int vsize = BitConverter.ToInt32(b, s + 8);
                int vaddr = BitConverter.ToInt32(b, s + 12);
                int raw = BitConverter.ToInt32(b, s + 20);
                if (rva >= vaddr && rva < vaddr + Math.Max(vsize, 1)) return raw + (rva - vaddr);
            }
            return -1;
        };

        int e = rva2off(expRva);
        int nNames = BitConverter.ToInt32(b, e + 24);
        int namesRva = BitConverter.ToInt32(b, e + 32);
        int namesOff = rva2off(namesRva);
        var names = new List<string>();
        for (int i = 0; i < nNames; i++)
        {
            int nameRva = BitConverter.ToInt32(b, namesOff + 4 * i);
            int no = rva2off(nameRva);
            int end = Array.IndexOf(b, (byte)0, no);
            names.Add(System.Text.Encoding.ASCII.GetString(b, no, end - no));
        }
        return names.ToArray();
    }
}
'@

function Test-WlxExports {
    param([string]$Dll, [string[]]$Expected)
    $found = [PeExports]::Read($Dll)
    $missing = @($Expected | Where-Object { $found -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "FAIL EXPORTS: отсутствуют $($missing -join ', ')`nНайдено ($($found.Count)): $($found -join ', ')"
    }
    Write-Host "  EXPORTS OK ($($found.Count)): $($found -join ', ')" -ForegroundColor Green
}

# --- 5. Сборка x86 и x64 ---
# Обе платформы пишут в ОДИН каталог bin\...\net48, поэтому dist формируется
# сразу после каждой платформы — иначе оба каталога получат последний бинарник.
# Перед каждым проходом чистим промежуточный вывод: смена PlatformTarget — это
# смена глобального свойства, MSBuild по таймстампам её не видит и без Rebuild
# пропустил бы перекомпиляцию (оба прохода выдали бы один и тот же бинарник).
# Core чистим один раз: с GlobalPropertiesToRemove он всегда AnyCPU, но мог
# остаться бинарь от старых сборок с наследованным PlatformTarget.
foreach ($cfg in 'Release', 'Debug') {
    Remove-Item (Join-Path $root "src\MarkdownLister.Core\obj\$cfg\net48") -Recurse -Force -ErrorAction SilentlyContinue
}
Remove-Item (Join-Path $root 'src\MarkdownLister.Core\bin') -Recurse -Force -ErrorAction SilentlyContinue

$wlxObjOut = Join-Path $root "src\MarkdownLister.WLX\obj\$Configuration\net48"
foreach ($plat in 'x86', 'x64') {
    Write-Host "[5/6] Сборка $plat ($Configuration)..."
    Remove-Item $wlxObjOut -Recurse -Force -ErrorAction SilentlyContinue
    & $msbuild $wlxProj -t:Rebuild -p:Configuration=$Configuration -p:PlatformTarget=$plat `
        -p:RestorePackages=false -v:m -nologo
    if ($LASTEXITCODE -ne 0) { throw "Сборка $plat завершилась с ошибкой ($LASTEXITCODE)" }

    $dll = Join-Path $root "src\MarkdownLister.WLX\bin\$Configuration\net48\MarkdownLister.WLX.dll"
    if (-not (Test-Path $dll)) { throw "Не найден $dll" }
    Write-Host "  Проверка экспорта $plat..."
    Test-WlxExports -Dll $dll -Expected $expected

    if (-not $NoDist) {
        $name = if ($plat -eq 'x86') { 'MarkdownLister.wlx' } else { 'MarkdownLister.wlx64' }
        $src = Join-Path $root "src\MarkdownLister.WLX\bin\$Configuration\net48"
        $dst = Join-Path $root "dist\$plat"
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        Copy-Item $src $dst -Recurse -Exclude *.pdb, *.xml
        # Before\ — промежуточные IL/RES-дампы DllExport, в дистрибутив не нужны
        Remove-Item (Join-Path $dst 'Before') -Recurse -Force -ErrorAction SilentlyContinue
        Rename-Item (Join-Path $dst 'MarkdownLister.WLX.dll') $name
        Remove-Item (Join-Path $dst 'MarkdownLister.WLX.dll.bak1') -Force -ErrorAction SilentlyContinue
        Write-Host "  dist\$plat\$name"
    }
}

# --- 6. Итог ---
if ($NoDist) {
    Write-Host '[6/6] dist пропущено (-NoDist)'
}
else {
    Write-Host '[6/6] dist готов: dist\x86\MarkdownLister.wlx, dist\x64\MarkdownLister.wlx64'
}

Write-Host 'Готово.' -ForegroundColor Green
