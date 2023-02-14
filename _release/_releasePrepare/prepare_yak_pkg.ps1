
Remove-Item manifest.yml
&'C:\Program Files\Rhino 7\System\Yak.exe' spec

Add-Content manifest.yml "`nicon_url: https://i.imgur.com/WABE4LN.png"
Add-Content manifest.yml "`nkeywords: `n - drawing `n - climate `n - soil `n - language"

Write-Host "========================="
Write-Host "Modified Manifest File:"
Write-Host "========================="
Get-Content manifest.yml


echo "========================="
echo "Build Package:"
echo "========================="
&'C:\Program Files\Rhino 7\System\Yak.exe' build

echo "========================="
echo "Build Package:"
echo "========================="
&'C:\Program Files\Rhino 7\System\Yak.exe' login


# then yak push xx.yak in the cmd line
