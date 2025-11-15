param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$BuildOutput = "D:\KSP Modding\KSPLiftoff\Build\KSPLiftoff.dll",
    [string]$RepoPath    = "D:\KSP Modding\KSPLiftoff\GIT_Updates"
)

Write-Host "=== Publishing KSPLiftoff v$Version to GitHub ==="

# Ensure repo exists
if (!(Test-Path $RepoPath)) {
    Write-Error "Repo path does not exist: $RepoPath"
    exit 1
}

# Create release folder
$releaseFolder = Join-Path $RepoPath "Release_v$Version"
if (!(Test-Path $releaseFolder)) { New-Item -ItemType Directory $releaseFolder }

# Copy compiled files
Copy-Item $BuildOutput "$releaseFolder\KSPLiftoff.dll" -Force
Copy-Item "$RepoPath\README.md" "$releaseFolder\README.md" -Force

# Optional: include logs or config templates
Copy-Item "D:\KSP Modding\KSPLiftoff\Template\Toggle.cfg" $releaseFolder -ErrorAction SilentlyContinue

Set-Location $RepoPath

# Git operations
git add .
git commit -m "Release KSPLiftoff v$Version"
git tag "v$Version"
git push
git push origin "v$Version"

Write-Host "=== Release v$Version pushed to GitHub successfully! ==="
