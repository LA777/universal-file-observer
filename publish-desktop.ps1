#requires -Version 5.1
<#
.SYNOPSIS
    Builds the Angular front end and publishes the Windows desktop application as
    a single self-contained ufo.exe.

.DESCRIPTION
    Both steps are needed and the order matters: the Angular bundle is embedded
    into the executable as a resource, and the embedding is decided when the
    project file is evaluated. Publishing without building the front end first
    fails with a clear message rather than shipping a UI-less executable.

.PARAMETER OutputPath
    Where the executable is written. Defaults to publish\desktop.

.PARAMETER SkipFrontend
    Reuses an existing Angular build instead of rebuilding it.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'publish\desktop'),
    [switch] $SkipFrontend
)

$ErrorActionPreference = 'Stop'

$clientPath = Join-Path $PSScriptRoot 'ufo.client'
$desktopProjectPath = Join-Path $PSScriptRoot 'Ufo.Desktop\Ufo.Desktop.csproj'

if (-not $SkipFrontend) {
    Write-Host 'Building the Angular front end...' -ForegroundColor Cyan
    Push-Location $clientPath
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed with exit code $LASTEXITCODE." }

        npm run build -- --configuration production
        if ($LASTEXITCODE -ne 0) { throw "ng build failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
}

Write-Host 'Publishing the desktop application...' -ForegroundColor Cyan
dotnet publish $desktopProjectPath -c Release -p:PublishDesktop=true -o $OutputPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$executablePath = Join-Path $OutputPath 'ufo.exe'
if (-not (Test-Path $executablePath)) { throw "Expected $executablePath to exist." }

$sizeInMegabytes = [math]::Round((Get-Item $executablePath).Length / 1MB, 1)
Write-Host ''
Write-Host "Published $executablePath ($sizeInMegabytes MB)" -ForegroundColor Green
Write-Host 'appsettings.json sits beside it and can be edited without a rebuild.'
