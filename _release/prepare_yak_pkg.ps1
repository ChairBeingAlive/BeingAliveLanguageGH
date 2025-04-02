# Description: Prepare the package for Yak

# setting the path to the target folder
$currentFolder = Get-Location
$targetFolder = (Get-Item $currentFolder).Parent.FullName

echo "========================="
echo "current folder:"
echo $currentFolder
echo "target folder:"
echo $targetFolder
echo "========================="

# Download Yak.exe if not already present
curl.exe -fSLo yak.exe https://files.mcneel.com/yak/tools/0.13.0/yak.exe

#--------------------------------------
# Rhino 8, net7.0
#--------------------------------------
if (Test-Path "releaseRH8"){
  Remove-Item releaseRH8 -Recurse
}
New-Item -Path "releaseRH8" -ItemType Directory -Force

Push-Location ".\releaseRH8"; 

if (Test-Path "manifest.yml")
{
  Remove-Item manifest.yml
}

Copy-Item -Path "${targetFolder}\bin\net48" -Destination "." -Recurse
Copy-Item -Path "${targetFolder}\bin\net7.0" -Destination "." -Recurse
Copy-Item -Path "${targetFolder}\bin\net7.0-windows" -Destination "." -Recurse

Copy-Item -Path "${currentFolder}\icon_new.png" -Destination "." -Recurse
./../yak.exe spec; 
Add-Content manifest.yml "`nicon: icon_new.png"
Add-Content manifest.yml "`nkeywords: `n - drawing `n - climate `n - soil `n - language"

Write-Host "======================================="
Write-Host "Modified Manifest File for NetCore 7, Rhino 8"
Write-Host "======================================="
Get-Content manifest.yml

./../yak.exe build
Copy-Item -Path ".\*.yak" -Destination "${currentFolder}" -Recurse

Pop-Location

# Rhino 7, net48
if (Test-Path "releaseRH7"){
  Remove-Item releaseRH7 -Recurse
}
New-Item -Path "releaseRH7" -ItemType Directory -Force

Push-Location ".\releaseRH7"; 

if (Test-Path "manifest.yml")
{
  Remove-Item manifest.yml
}

Copy-Item -Path "${targetFolder}\bin\net48\*" -Destination "." -Recurse
Copy-Item -Path "${currentFolder}\icon_new.png" -Destination "." -Recurse

./../yak.exe spec; 
Add-Content manifest.yml "`nicon: icon_new.png"
Add-Content manifest.yml "`nkeywords: `n - drawing `n - climate `n - soil `n - language"

Write-Host "======================================="
Write-Host "Modified Manifest File for NetCore 7, Rhino 8"
Write-Host "======================================="
Get-Content manifest.yml

./../yak.exe build
Copy-Item -Path ".\*.yak" -Destination "${currentFolder}" -Recurse
Pop-Location


# then yak push xx.yak in the cmd line
