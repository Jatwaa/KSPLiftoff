param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

Write-Host "=== Updating KSPLiftoff Repository for version v$Version ==="

# Paths
$BuildOutput = "D:\KSP Modding\KSPLiftoff\bin\Release\KSPLiftoff.dll"
$RepoPath    = "D:\KSP Modding\KSPLiftoff\GIT_Updates"
$SourceCS    = "D:\KSP Modding\KSPLiftoff\KSPLiftoffController.cs"
$ReadmeFile  = "$RepoPath\README.md"

# Verify Repo Exists
if (!(Test-Path $RepoPath)) {
    Write-Error "ERROR: Git repo path does not exist: $RepoPath"
    exit 1
}

# Verify DLL Exists
if (!(Test-Path $BuildOutput)) {
    Write-Error "ERROR: DLL not found: $BuildOutput"
    exit 1
}

# Create versioned release folder
$releaseFolder = Join-Path $RepoPath "Release_v$Version"
if (!(Test-Path $releaseFolder)) {
    New-Item -ItemType Directory $releaseFolder | Out-Null
}

Write-Host "Copying DLL, README, and source file to release folder..."

Copy-Item $BuildOutput "$releaseFolder\KSPLiftoff.dll" -Force
Copy-Item $ReadmeFile "$releaseFolder\README.md" -Force

# Optional: include the controller code
if (Test-Path $SourceCS) {
    Copy-Item $SourceCS "$releaseFolder\KSPLiftoffController_v$Version.cs" -Force
}

# Move into repo directory
Set-Location $RepoPath

# Stage all changes
git add .

# Commit with message
$commitMessage = "KSPLiftoff v$Version Release - Auto commit"
git commit -m $commitMessage

# Tag the version
git tag "v$Version"

# Push commit and tag
git push
git push origin "v$Version"

Write-Host "=== KSPLiftoff v$Version has been successfully published to GitHub ==="
