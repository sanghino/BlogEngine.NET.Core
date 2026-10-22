<#
.SYNOPSIS
    Prepara il pacchetto da caricare via FTP sull'hosting Windows di Aruba.

.DESCRIPTION
    Pubblica BlogEngine.Web in artifacts\aruba\<data-ora>\ con:
      public\           contenuto della cartella "public" del sito (da caricare via FTP)
      app_offline.htm   da caricare in "public" PRIMA dell'aggiornamento e da cancellare DOPO
      LEGGIMI.txt       riepilogo delle opzioni usate
    e ne crea anche lo zip. La cartella App_Data non fa parte del pacchetto: contiene i dati del blog
    e non va mai sovrascritta.

.PARAMETER Framework
    net10.0 (predefinito, supporto Microsoft fino a novembre 2028) oppure net8.0.

.PARAMETER Mode
    SelfContained (predefinito): il runtime .NET è incluso, non dipende da cosa è installato sul server.
    FrameworkDependent: pacchetto più piccolo, richiede che Aruba abbia installato la stessa versione di .NET.

.PARAMETER HostingModel
    OutOfProcess (predefinito con SelfContained): il sito gira in un processo proprio, indipendente
    dall'architettura (32/64 bit) del pool applicativo di IIS.
    InProcess: un po' più veloce, ma con SelfContained l'architettura deve coincidere con quella del pool.

.PARAMETER Runtime
    Architettura per SelfContained: win-x64 (predefinito) oppure win-x86.

.EXAMPLE
    .\tools\publish-aruba.ps1
    .\tools\publish-aruba.ps1 -Framework net8.0 -Mode FrameworkDependent -HostingModel InProcess
#>
[CmdletBinding()]
param(
    [ValidateSet('net10.0', 'net8.0')] [string] $Framework = 'net10.0',
    [ValidateSet('SelfContained', 'FrameworkDependent')] [string] $Mode = 'SelfContained',
    [ValidateSet('OutOfProcess', 'InProcess')] [string] $HostingModel,
    [ValidateSet('win-x64', 'win-x86')] [string] $Runtime = 'win-x64',
    [string] $Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
if (-not $HostingModel) { $HostingModel = if ($Mode -eq 'SelfContained') { 'OutOfProcess' } else { 'InProcess' } }

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\BlogEngine.Web\BlogEngine.Web.csproj'
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$output = Join-Path $root "artifacts\aruba\$stamp"
$public = Join-Path $output 'public'

$arguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--output', $public,
    "-p:BlogEngineTargetFramework=$Framework",
    "-p:AspNetCoreHostingModel=$HostingModel"
)
if ($Mode -eq 'SelfContained') {
    $arguments += @('--runtime', $Runtime, '--self-contained', 'true')
} else {
    $arguments += @('--self-contained', 'false')
}

Write-Host "Pubblicazione: $Framework, $Mode, $HostingModel$(if ($Mode -eq 'SelfContained') { ", $Runtime" })" -ForegroundColor Cyan
& $Dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish non riuscito (codice $LASTEXITCODE)." }

# web.config: programma da avviare e modello di hosting scelti (il file del progetto vale per "dotnet BlogEngine.Web.dll").
$webConfigPath = Join-Path $public 'web.config'
[xml] $webConfig = Get-Content $webConfigPath -Raw
$aspNetCore = $webConfig.SelectSingleNode('//aspNetCore')
if ($Mode -eq 'SelfContained') {
    $aspNetCore.SetAttribute('processPath', '.\BlogEngine.Web.exe')
    $aspNetCore.SetAttribute('arguments', '')
} else {
    $aspNetCore.SetAttribute('processPath', 'dotnet')
    $aspNetCore.SetAttribute('arguments', '.\BlogEngine.Web.dll')
}
$aspNetCore.SetAttribute('hostingModel', $HostingModel.ToLowerInvariant())
$webConfig.Save($webConfigPath)

# File che non devono arrivare sul server.
Get-ChildItem $public -Recurse -Include '*.pdb', 'appsettings.Development.json' | Remove-Item -Force

# Pagina mostrata ai visitatori durante l'aggiornamento (IIS ferma il sito finché il file esiste).
@'
<!DOCTYPE html>
<html lang="it">
<head><meta charset="utf-8"><title>Aggiornamento in corso</title>
<style>body{font-family:sans-serif;text-align:center;padding:4em;color:#333}</style></head>
<body><h1>Aggiornamento in corso</h1><p>Il blog torna disponibile tra qualche minuto.</p>
<p lang="en">The blog is being updated and will be back in a few minutes.</p></body>
</html>
'@ | Set-Content -Path (Join-Path $output 'app_offline.htm') -Encoding utf8

$version = (Get-Item (Join-Path $public 'BlogEngine.Web.dll')).VersionInfo.ProductVersion
@"
BlogEngine.NET $version - pacchetto del $(Get-Date -Format 'dd/MM/yyyy HH:mm')
Framework: $Framework
Modalità: $Mode$(if ($Mode -eq 'SelfContained') { " ($Runtime)" })
Hosting IIS: $HostingModel

Procedura completa: docs\deploy-aruba.md
1. Backup via FTP di "public" e "App_Data".
2. Carica app_offline.htm in "public".
3. Carica il contenuto di public\ in "public" (sovrascrivendo).
4. Cancella app_offline.htm da "public" e apri il sito.
"@ | Set-Content -Path (Join-Path $output 'LEGGIMI.txt') -Encoding utf8

$zip = "$output.zip"
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
Write-Host "Pacchetto pronto: $output" -ForegroundColor Green
Write-Host "Zip: $zip" -ForegroundColor Green
