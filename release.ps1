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

$apkDir = Join-Path $env:TEMP 'ZephyrApk'
Remove-Item $apkDir -Recurse -Force -ErrorAction SilentlyContinue
$appRun = (gh run list --repo JamilDbiat/ZephyrRemote --workflow app.yml --status success --limit 1 --json databaseId | Out-String | ConvertFrom-Json)
if ($appRun) {
    gh run download $appRun[0].databaseId --repo JamilDbiat/ZephyrRemote -n ZephyrRemote-android --dir $apkDir
    $apk = Get-ChildItem $apkDir -Filter *.apk | Select-Object -First 1
    if ($apk) {
        Copy-Item $apk.FullName "$apkDir\ZephyrRemote-Android.apk"
        gh release upload "v$Version" "$apkDir\ZephyrRemote-Android.apk" --repo JamilDbiat/ZephyrRemote --clobber
        Write-Host "Attached Android app" -ForegroundColor Green
    }
}

