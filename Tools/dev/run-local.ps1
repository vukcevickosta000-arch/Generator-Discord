# Starts the Bloodfall development stack on Windows (backend + game servers).
# Usage: .\Tools\dev\run-local.ps1 [-GameServers 2] [-Region dev-local]
param([int]$GameServers = 2, [string]$Region = "dev-local")
$Root = Resolve-Path "$PSScriptRoot\..\.."
$Logs = Join-Path $Root "Server\artifacts\logs"
New-Item -ItemType Directory -Force -Path $Logs | Out-Null
dotnet build "$Root\Server\src\Bloodfall.Backend" -c Debug -nologo -v q
dotnet build "$Root\Server\src\Bloodfall.GameServer" -c Debug -nologo -v q
$env:ASPNETCORE_ENVIRONMENT = "Development"
Start-Process -FilePath "$Root\Server\src\Bloodfall.Backend\bin\Debug\net8.0\Bloodfall.Backend.exe" -WorkingDirectory "$Root\Server\src\Bloodfall.Backend" -RedirectStandardOutput "$Logs\backend.log" -WindowStyle Minimized
Start-Sleep -Seconds 5
for ($i = 0; $i -lt $GameServers; $i++) {
  $port = 27015 + $i
  Start-Process -FilePath "$Root\Server\src\Bloodfall.GameServer\bin\Debug\net8.0\Bloodfall.GameServer.exe" -ArgumentList "--port $port --region $Region --id local-$port" -WorkingDirectory "$Root\Server\src\Bloodfall.GameServer" -RedirectStandardOutput "$Logs\gameserver-$port.log" -WindowStyle Minimized
}
Write-Host "Bloodfall services running: http://localhost:5080/api/content/status"
