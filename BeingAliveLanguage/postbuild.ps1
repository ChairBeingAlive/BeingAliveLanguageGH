param(
    [string]$TargetDir,
    [string]$SolutionDir
)

# Clean up directory paths
Write-Host "++++++++++++++++++++++++++++++: $TargetDir"
Write-Host "++++++++++++++++++++++++++++++: $SolutionDir"

# $TargetDir = $TargetDir.TrimEnd('\', '/')
# $SolutionDir = $SolutionDir.TrimEnd('\', '/')

# Extract target framework from path
$tfm = Split-Path $TargetDir -Leaf

# Set output directory
$outputDir = Join-Path (Join-Path $SolutionDir "bin") $tfm
$cppPrebuild = Join-Path $SolutionDir "cppPrebuild"

# Create output directory
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Copying from++++++++++++++++++++++++++++: $TargetDir"
Write-Host "Copying to++++++++++++++++++++++++++++++: $outputDir"

# Copy build outputs
Copy-Item -Path (Join-Path $TargetDir "*.dll") -Destination $outputDir -Force
Copy-Item -Path (Join-Path $TargetDir "*.gha") -Destination $outputDir -Force

# Copy C++ DLLs if directory exists
if (Test-Path $cppPrebuild) {
    Copy-Item -Path (Join-Path $cppPrebuild "*.dll") -Destination $outputDir -Force
}
