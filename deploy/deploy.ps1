<#
  Builds NavEstate from this checkout and publishes it to the IIS folder on the Contabo server.
  Run on the server in an elevated PowerShell from the repository root:

      .\deploy\deploy.ps1                       # defaults below
      .\deploy\deploy.ps1 -SiteName NavEstate -Target D:\NavEstate\app

  Needs: .NET 8 SDK, Node.js 22, IIS with the ASP.NET Core 8 Hosting Bundle, and the IIS site
  created once as described in docs/DEPLOY.md. Settings and secrets live in environment variables
  on the app pool (or appsettings.Production.json next to the DLL), never in git.
#>
param(
    [string]$SiteName = "NavEstate",
    [string]$AppPool = "NavEstate",
    [string]$Target = "D:\NavEstate\app"
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

Write-Host "Building the web app..."
Push-Location web
npm ci
npm run build
Pop-Location

Write-Host "Publishing the API..."
$out = Join-Path $env:TEMP "navestate-publish"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src/NavEstate.Api -c Release -o $out

Write-Host "Stopping $AppPool..."
if ((Get-WebAppPoolState $AppPool).Value -ne "Stopped") { Stop-WebAppPool $AppPool }
Start-Sleep -Seconds 3

# Keep server-only files (production settings, uploaded documents) when copying the new build.
New-Item -ItemType Directory -Force -Path $Target | Out-Null
robocopy $out $Target /MIR /XF appsettings.Production.json /XD App_Data | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with $LASTEXITCODE" }

Write-Host "Starting $AppPool (the app applies database migrations on start)..."
Start-WebAppPool $AppPool
Start-Sleep -Seconds 5
try {
    $r = Invoke-WebRequest -UseBasicParsing "http://localhost/api/health"
    Write-Host "Health check: $($r.StatusCode) $($r.Content)"
} catch {
    Write-Warning "Health check failed: $_  Check Event Viewer > Windows Logs > Application (source: IIS AspNetCore Module V2)"
}
