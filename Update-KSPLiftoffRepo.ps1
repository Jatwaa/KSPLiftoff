param(
    [string]$Version,
    [string]$SourceFile = "D:\KSP Modding\KSPLiftoff\KSPLiftoffController.cs",
    [string]$RepoPath  = "D:\KSP Modding\KSPLiftoff\GIT_Updates"
)

Write-Host "=== Updating KSPLiftoff GitHub Repository ==="

# Copy file
$dest = Join-Path $RepoPath "KSPLiftoffController_v$Version.cs"
Copy-Item $SourceFile $dest -Force

Set-Location $RepoPath

git add .
git commit -m "KSPLiftoff updated to version $Version"
git push

Write-Host "Uploaded version $Version to GitHub."
