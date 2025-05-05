param(
    [string]$TargetDir,
    [string]$SolutionDir
)

# Create cppPrebuild directory if it doesn't exist
$cppPrebuildDir = Join-Path $SolutionDir "cppPrebuild"
New-Item -ItemType Directory -Path $cppPrebuildDir -Force | Out-Null

# Check if we found any DLL
$targetDll = Join-Path $cppPrebuildDir "GeoSharPlusCPP.dll"
if (Test-Path $targetDll) {
    # Copy to the target directory as well
    Write-Host "Copying to project's target directory: $TargetDir"
    Copy-Item -Path $targetDll -Destination $TargetDir -Force
    
    if (Test-Path (Join-Path $TargetDir "GeoSharPlusCPP.dll")) {
        Write-Host "Successfully copied GeoSharPlusCPP.dll to $TargetDir"
    } else {
        Write-Host "Warning: Failed to copy GeoSharPlusCPP.dll to $TargetDir"
    }
} else {
    Write-Host "Warning: Could not find GeoSharPlusCPP.dll in any expected location"
}
