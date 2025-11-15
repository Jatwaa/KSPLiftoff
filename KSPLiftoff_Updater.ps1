cd "D:\KSP Modding\KSPLiftoff\GIT_Updates"

# Get something like '22.5-3-gabc1234' or just commit hash
$desc = git describe --tags --always

# Path to your KSP GameData mod folder
$gameData = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\GameData\KSPLiftoff"
if (!(Test-Path $gameData)) {
    Write-Host "GameData path not found: $gameData"
    exit 1
}

$outFile = Join-Path $gameData "git_version.txt"
$desc | Out-File -FilePath $outFile -Encoding ASCII

Write-Host "Wrote git_version.txt with: $desc"
