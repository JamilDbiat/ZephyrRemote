param([string]$App)

$exe = Join-Path $App 'ZephyrRemote.exe'
$user = "$env:USERDOMAIN\$env:USERNAME"

$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $App
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName ZephyrRemote -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

Get-NetFirewallRule -DisplayName 'Zephyr Remote' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'Zephyr Remote' -Direction Inbound -Protocol TCP -LocalPort 5050 -Action Allow -Profile Any | Out-Null

Start-ScheduledTask -TaskName ZephyrRemote
