#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 8085,
    [switch]$NoBuild,
    [string]$DotNet
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$project = Join-Path $repoRoot 'src\Quest.Studio.Host\Quest.Studio.Host.csproj'
$contractsPackage = Join-Path $repoRoot 'packages-local\Comfy.Quest.Contracts.0.1.0-local.nupkg'
$packageCacheKey = if (Test-Path -LiteralPath $contractsPackage) {
    (Get-FileHash -LiteralPath $contractsPackage -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 16)
} else {
    'public-packages'
}
$nugetPackages = Join-Path $repoRoot ("artifacts\quest-studio\nuget-cache\host-" + $packageCacheKey)
$workspaceToolchain = Join-Path (Split-Path -Parent $repoRoot) 'dotnet9\dotnet.exe'
$candidates = @(
    $DotNet,
    $env:COMFY_QUEST_DOTNET,
    $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
    $(if (Test-Path $workspaceToolchain) { $workspaceToolchain }),
    $(if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source })
) | Where-Object { $_ } | Select-Object -Unique
$dotnetExe = $null
foreach ($candidate in $candidates) {
    try {
        $resolved = (Get-Item -LiteralPath $candidate -ErrorAction Stop).FullName
        $major = [int]((& $resolved --version) -split '\.')[0]
        if ($LASTEXITCODE -eq 0 -and $major -ge 9) { $dotnetExe = $resolved; break }
    } catch {}
}
if (-not $dotnetExe) {
    throw 'Quest Studio requires a .NET 9 SDK. Set COMFY_QUEST_DOTNET or pass -DotNet with its dotnet executable.'
}
$arguments = @('run', '--project', $project, '--configuration', 'Release')
if ($NoBuild) { $arguments += '--no-build' }
$arguments += @('--', '--port', [string]$Port)

Write-Host "Quest Studio -> http://127.0.0.1:$Port/quest-studio"
$previousNugetPackages = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
try {
    # The interim package version stays fixed while its local bytes evolve. NuGet
    # otherwise reuses an older immutable-version cache and Studio compiles against
    # stale contract types. Keying the cache by package SHA keeps ordinary launches
    # aligned with the exact checked-in package, just like the synthetic E2E runner.
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $nugetPackages, 'Process')
    & $dotnetExe @arguments
    $runExit = $LASTEXITCODE
} finally {
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $previousNugetPackages, 'Process')
}
exit $runExit
