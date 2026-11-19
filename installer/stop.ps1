Stop-ScheduledTask -TaskName ZephyrRemote -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -in 'ZephyrRemote.exe', 'cloudflared.exe', 'ffmpeg.exe' -and $_.ExecutablePath -like '*ZephyrRemote*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
