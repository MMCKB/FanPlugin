# 打包插件为 .cipx（zip 格式，ClassIsland 插件包）
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$out = Join-Path $root "bin\Release\net8.0-windows\win-x64"

$files = @(
    "manifest.yml",
    "README.md",
    "icon.png",
    "ClassIsland.FanControl.dll",
    "ClassIsland.FanControl.deps.json",
    "LibreHardwareMonitorLib.dll",
    "HidSharp.dll",
    "DiskInfoToolkit.dll",
    "BlackSharp.Core.dll",
    "RAMSPDToolkit-NDD.dll",
    "System.CodeDom.dll",
    "System.IO.Ports.dll",
    "System.Management.dll",
    "System.Threading.AccessControl.dll"
)

$stage = Join-Path $root "cipx"
if (Test-Path $stage) { [System.IO.Directory]::Delete($stage, $true) }
New-Item -ItemType Directory -Path $stage | Out-Null

foreach ($f in $files) {
    $src = Join-Path $out $f
    if (-not (Test-Path $src)) { throw "缺少文件：$f，请先执行 dotnet build -c Release" }
    Copy-Item $src (Join-Path $stage $f)
}

$zip = Join-Path $root "ClassIsland.FanControl.cipx"
$tmp = Join-Path $root "ClassIsland.FanControl.zip"
if (Test-Path $tmp) { [System.IO.File]::Delete($tmp) }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $tmp -CompressionLevel Optimal
if (Test-Path $zip) { [System.IO.File]::Delete($zip) }
[System.IO.File]::Move($tmp, $zip)

Write-Host "已生成插件包：$zip"
