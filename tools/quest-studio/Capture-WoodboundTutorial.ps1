# Regenerates the screenshot-led Woodbound tutorial captures.
#
# The captures come from the same synthetic browser journey that proves the authoring
# path (Test-QuestStudioE2E.ps1) with the tutorial capture lane enabled, so they are
# pixel-real Studio, sanitized by construction (fresh temp profile, no machine identity,
# no player asked to recreate anything). Screenshots land in docs/tutorials/woodbound/.
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& (Join-Path $repo 'tools\Assert-RepoIdentity.ps1')
$shots = Join-Path $repo 'docs\tutorials\woodbound'
$env:QUEST_STUDIO_TUTORIAL_SHOTS = $shots
try {
  & (Join-Path $repo 'tools\quest-studio\Test-QuestStudioE2E.ps1')
  if ($LASTEXITCODE -ne 0) { throw 'Synthetic journey failed; captures are not trustworthy.' }
} finally {
  Remove-Item Env:\QUEST_STUDIO_TUTORIAL_SHOTS -ErrorAction SilentlyContinue
}
Get-ChildItem $shots -Filter *.png | ForEach-Object { Write-Host ("captured " + $_.Name) }
