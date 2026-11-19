param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Notes = ""
)

$root = $PSScriptRoot
$build = Join-Path $env:TEMP 'ZephyrBuild'
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1

Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\ZephyrRemote.csproj" -c Release -r win-x64 --self-contained true -p:Version=$Version -o "$build\app"
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed' -ForegroundColor Red; exit 1 }

& $iscc "/DAppVersion=$Version" "/DBuildDir=$build" "$root\installer\ZephyrRemote.iss"
if ($LASTEXITCODE -ne 0) { Write-Host 'Installer failed' -ForegroundColor Red; exit 1 }

git -C $root add -A
git -C $root commit -m "Zephyr Remote $Version"
git -C $root push

if ($Notes -eq "") { $Notes = "Zephyr Remote $Version" }
gh release create "v$Version" "$build\ZephyrRemoteSetup.exe" --repo JamilDbiat/ZephyrRemote --title "Zephyr Remote $Version" --notes $Notes
Write-Host "Released v$Version" -ForegroundColor Green
