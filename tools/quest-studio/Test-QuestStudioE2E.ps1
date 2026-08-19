#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$DotNet,
    [switch]$Headed,
    [switch]$KeepArtifacts,
    [switch]$SkipBrowserInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

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
    throw 'Quest Studio E2E requires a .NET 9 SDK. Set COMFY_QUEST_DOTNET or pass -DotNet with its dotnet executable.'
}

$hostProject = Join-Path $repoRoot 'src\Quest.Studio.Host\Quest.Studio.Host.csproj'
$testProject = Join-Path $repoRoot 'src\Quest.Studio.E2E.Tests\Quest.Studio.E2E.Tests.csproj'
$testOutput = Join-Path $repoRoot 'src\Quest.Studio.E2E.Tests\bin\Release\net9.0'
$contractsPackage = Join-Path $repoRoot 'packages-local\Comfy.Quest.Contracts.0.2.0-local.nupkg'
$packageCacheKey = if (Test-Path -LiteralPath $contractsPackage) {
    (Get-FileHash -LiteralPath $contractsPackage -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 16)
} else {
    'public-packages'
}
$nugetPackages = Join-Path $repoRoot ("artifacts\quest-studio-e2e\nuget-cache\host-" + $packageCacheKey)
$hostOutput = Join-Path $repoRoot ("artifacts\quest-studio-e2e\host\" + $packageCacheKey)
$hostDll = Join-Path $hostOutput 'Comfy.Quest.Studio.Host.dll'
# Build intermediates stay out of src\*\bin so a Studio host already running from the
# tree (another session's dev server) never locks this publish. ArtifactsPath keeps
# per-project subfolders, so Host and Studio do not collide with each other either.
$hostBuildArtifacts = Join-Path $repoRoot ("artifacts\quest-studio-e2e\host-build\" + $packageCacheKey)

$variables = @(
    'NUGET_PACKAGES',
    'COMFY_QUEST_E2E_DOTNET',
    'COMFY_QUEST_E2E_HOST_DLL',
    'COMFY_QUEST_E2E_HEADED',
    'COMFY_QUEST_E2E_KEEP_ARTIFACTS'
)
$previous = @{}
foreach ($name in $variables) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

$testExit = 1
try {
    # The interim package version is intentionally fixed while its bytes evolve. A
    # cache keyed by the package hash prevents NuGet's immutable-version cache from
    # silently compiling this run against older Contracts bytes.
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $nugetPackages, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_DOTNET', $dotnetExe, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_HOST_DLL', $hostDll, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_HEADED', $(if ($Headed) { '1' } else { '0' }), 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_KEEP_ARTIFACTS', $(if ($KeepArtifacts) { '1' } else { '0' }), 'Process')

    Write-Host 'Publishing the real Quest Studio host to an isolated E2E output...'
    & $dotnetExe publish $hostProject --configuration Release --output $hostOutput "-p:ArtifactsPath=$hostBuildArtifacts"
    if ($LASTEXITCODE -ne 0) { throw "Quest Studio host build failed with exit code $LASTEXITCODE." }

    # Only the Studio host consumes the evolving interim package. Let the E2E
    # project reuse the normal package cache so Playwright is not duplicated.
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $previous['NUGET_PACKAGES'], 'Process')

    Write-Host 'Building the synthetic browser E2E test...'
    & $dotnetExe build $testProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Quest Studio E2E build failed with exit code $LASTEXITCODE." }

    if (-not $SkipBrowserInstall) {
        $playwrightScript = Join-Path $testOutput 'playwright.ps1'
        if (-not (Test-Path -LiteralPath $playwrightScript)) {
            throw "Playwright install script was not produced at $playwrightScript."
        }
        Write-Host 'Ensuring the pinned Playwright Chromium build is installed...'
        & $playwrightScript install chromium
        if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium install failed with exit code $LASTEXITCODE." }
    }

    Write-Host 'Running synthetic Studio E2E (browser + host + questpack + contract receipts)...'
    & $dotnetExe test $testProject --configuration Release --no-build --logger 'console;verbosity=detailed'
    $testExit = $LASTEXITCODE
} finally {
    foreach ($name in $variables) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}

exit $testExit
