<#
.SYNOPSIS
    把 NekoLua 测试套件安装到服务器的 NekoLua 目录。

.EXAMPLE
    pwsh -File tests/lua/install.ps1 -NekoLuaRoot "D:\Server\NekoLua"

.EXAMPLE
    pwsh -File tests/lua/install.ps1 -NekoLuaRoot "D:\Server\NekoLua" -Scope port -Port 7777
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$NekoLuaRoot,

    [ValidateSet('global', 'port')]
    [string]$Scope = 'global',

    [int]$Port = 0
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $NekoLuaRoot).Path
$sourceRoot = $PSScriptRoot

if (-not (Test-Path -LiteralPath (Join-Path $root 'mods'))) {
    throw "目标目录不像 NekoLua 根目录（缺少 mods/）：$root"
}

switch ($Scope) {
    'global' {
        $modsTarget = Join-Path $root 'mods\global'
    }
    'port' {
        if ($Port -le 0) {
            throw "Scope=port 时必须提供 -Port（例如 -Port 7777）"
        }
        $modsTarget = Join-Path $root ("mods\{0}" -f $Port)
    }
}

New-Item -ItemType Directory -Path $modsTarget -Force | Out-Null

$globalLibs = Join-Path $root 'dependencies\global'
New-Item -ItemType Directory -Path $globalLibs -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $sourceRoot 'dependencies\global\neko_testkit.lua') `
    -Destination (Join-Path $globalLibs 'neko_testkit.lua') -Force

$installed = @()

Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'mods') -Directory | ForEach-Object {
    $destination = Join-Path $modsTarget $_.Name
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }

    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    $installed += $_.Name
}

Write-Host "已安装到: $modsTarget"
Write-Host ("测试插件: {0}" -f ($installed -join ', '))
Write-Host "公共库:   $(Join-Path $globalLibs 'neko_testkit.lua')"
Write-Host ''
Write-Host '下一步：'
Write-Host '  nekolua refresh'
Write-Host '  lua_rt / lua_astra / lua_err / lua_sched / lua_core / lua_hr'
Write-Host '热重载验证：lua_hr -> nekolua reload 50_hotreload -> lua_hr'
