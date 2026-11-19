Stop-ScheduledTask -TaskName ZephyrRemote -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName ZephyrRemote -Confirm:$false -ErrorAction SilentlyContinue

Stop-Service ZephyrRemoteHelper -Force -ErrorAction SilentlyContinue
sc.exe delete ZephyrRemoteHelper | Out-Null

Get-CimInstance Win32_Process |
    Where-Object { $_.Name -in 'ZephyrRemote.exe', 'cloudflared.exe', 'ffmpeg.exe' -and $_.ExecutablePath -like '*ZephyrRemote*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

Remove-Item (Join-Path $env:ProgramFiles 'ZephyrRemote') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:ProgramData 'ZephyrRemote') -Recurse -Force -ErrorAction SilentlyContinue
Get-NetFirewallRule -DisplayName 'Zephyr Remote' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
