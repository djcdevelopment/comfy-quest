[CmdletBinding()]
param(
    [string]$EventsDir,
    [string]$StateDir,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
$entrypoint = Join-Path $toolRoot 'questlab_sheets.py'

if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw 'Python 3.10 or newer is required for the Quest Lab export companion. Install Python, then run this script again.'
}

& python -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 42)"
if ($LASTEXITCODE -ne 0) {
    throw 'Python 3.10 or newer is required for the Quest Lab export companion. Upgrade Python, then run this script again.'
}

$arguments = @($entrypoint, 'serve')
if ($EventsDir) {
    $resolvedEvents = [System.IO.Path]::GetFullPath($EventsDir)
    $arguments += @('--events-dir', $resolvedEvents)
}
if ($StateDir) {
    $resolvedState = [System.IO.Path]::GetFullPath($StateDir)
    $arguments += @('--state-dir', $resolvedState)
}
if ($NoBrowser) {
    $arguments += '--no-browser'
}

Write-Host 'Starting the local Quest Lab export companion on http://127.0.0.1:47631/'
Write-Host 'Google remains optional; local parsing and CSV do not require credentials or extra packages.'
& python @arguments
exit $LASTEXITCODE
