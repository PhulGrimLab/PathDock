<#
.SYNOPSIS
    PathDock.App을 실제 프로세스로 구동하는 UI Automation 테스트를 빌드부터 실행까지 진행한다.
.DESCRIPTION
    화면을 실제로 조작(SendKeys 포함)하므로 실행 중에는 마우스/키보드를 건드리지 않는다.
.PARAMETER SkipBuild
    빌드를 건너뛰고 이미 빌드된 exe로 실행한다.
.PARAMETER Tests
    실행할 테스트 이름(공백 구분). 생략하면 전체.
#>
[CmdletBinding()]
param([switch]$SkipBuild, [string[]]$Tests)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\PathDock.UiAutomation.Tests\PathDock.UiAutomation.Tests.csproj"
$Harness = Join-Path $RepoRoot "src\PathDock.UiAutomation.Tests\bin\Debug\net10.0-windows\PathDock.UiAutomation.Tests.exe"

Get-Process -Name "PathDock.App" -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force -ErrorAction Stop } catch {}
}
if (-not $SkipBuild) {
    & dotnet build $Project
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
}
& $Harness @Tests
exit $LASTEXITCODE
