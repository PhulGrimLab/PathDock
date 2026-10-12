<#
.SYNOPSIS
    PathDock을 복원-빌드-MSTest 전체까지 한 번에 실행한다 (CodeAtlas의 run_tests.ps1과 같은 역할).
.PARAMETER SkipBuild
    빌드를 건너뛰고 이미 빌드된 결과로 테스트만 실행한다.
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\PathDock.Tests\PathDock.Tests.csproj"

# 이전에 남은 앱 프로세스가 있으면 빌드 산출물 잠금으로 실패하므로 정리한다.
Get-Process -Name "PathDock.App" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  - 남은 프로세스 종료: PID $($_.Id)" -ForegroundColor DarkYellow
    try { Stop-Process -Id $_.Id -Force -ErrorAction Stop } catch {}
}

$args = @("test", $Project)
if ($SkipBuild) { $args += "--no-build" }
& dotnet @args
exit $LASTEXITCODE
