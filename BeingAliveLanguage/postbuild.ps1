param(
    [string]$TargetDir,
    [string]$SolutionDir,
    [string]$Configuration
)

# Clean up directory paths
Write-Host "++++++++++++++++++++++++++++++: $TargetDir"
Write-Host "++++++++++++++++++++++++++++++: $SolutionDir"

# Set output directory
$outputDir = Join-Path (Join-Path $SolutionDir "bin") $Configuration

# Create output directory
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Copying from++++++++++++++++++++++++++++: $TargetDir"
Write-Host "Copying to++++++++++++++++++++++++++++++: $outputDir"

# Copy build outputs
Copy-Item -Path (Join-Path $TargetDir "*.dll") -Destination $outputDir -Force
Copy-Item -Path (Join-Path $TargetDir "*.gha") -Destination $outputDir -Force
