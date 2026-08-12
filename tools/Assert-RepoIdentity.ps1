# Assert-RepoIdentity.ps1 -- stale-checkout-root defense.
# Windows PowerShell 5.1 compatible.

$ErrorActionPreference = 'Stop'
$expected = 'djcdevelopment/comfy-quest'
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    $origin = git remote get-url origin 2>$null
}
finally {
    Pop-Location
}

if (-not $origin) {
    Write-Error "REPO IDENTITY FAILURE: no git origin resolvable from '$repoRoot'. Refusing to act."
    exit 1
}
if ($origin -notmatch [regex]::Escape($expected)) {
    Write-Error ("REPO IDENTITY FAILURE: origin is '{0}', expected '*{1}*'. Refusing to act." -f $origin, $expected)
    exit 1
}

exit 0
