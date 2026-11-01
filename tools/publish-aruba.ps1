<#
.SYNOPSIS
    Prepara il pacchetto da caricare via FTP sull'hosting Windows di Aruba.

.DESCRIPTION
    Pubblica BlogEngine.Web in artifacts\aruba\<data-ora>\ con:
      sito\             contenuto da caricare via FTP nella root del sito (la cartella con il nome del dominio):
                        solo web.config e la cartella app\ con il programma, i temi e i file statici
      app_offline.htm   da caricare nella root PRIMA dell'aggiornamento e da cancellare DOPO
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
$site = Join-Path $output 'sito'
$app = Join-Path $site 'app'

$arguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--output', $app,
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

# Nella root restano solo web.config e app\: il programma con le sue cartelle sta tutto in app\.
$webConfigPath = Join-Path $site 'web.config'
Move-Item (Join-Path $app 'web.config') $webConfigPath

# I dati restano nella cartella App_Data della root, accanto ad app\ (quella di Aruba, scrivibile e
# nascosta dal web). Il percorso è relativo alla cartella del programma.
$settingsPath = Join-Path $app 'appsettings.json'
$settings = Get-Content $settingsPath -Raw
if ($settings -notmatch '"DataPath": "App_Data"') { throw "DataPath non trovato in appsettings.json." }
$settings.Replace('"DataPath": "App_Data"', '"DataPath": "../App_Data"') | Set-Content $settingsPath -Encoding utf8 -NoNewline

# web.config: programma da avviare e modello di hosting scelti (il file del progetto vale per "dotnet BlogEngine.Web.dll").
[xml] $webConfig = Get-Content $webConfigPath -Raw
$aspNetCore = $webConfig.SelectSingleNode('//aspNetCore')
if ($Mode -eq 'SelfContained') {
    $aspNetCore.SetAttribute('processPath', '.\app\BlogEngine.Web.exe')
    $aspNetCore.SetAttribute('arguments', '')
} else {
    $aspNetCore.SetAttribute('processPath', 'dotnet')
    $aspNetCore.SetAttribute('arguments', '.\app\BlogEngine.Web.dll')
}
$aspNetCore.SetAttribute('hostingModel', $HostingModel.ToLowerInvariant())

# Full Trust, come lo imposta il pannello Aruba (Application pool -> Trust level): così ricaricare il
# web.config durante un aggiornamento non riporta il sito al livello "Aruba predefinito". La riga
# compilation/tempDirectory la aggiunge Aruba da sola e non va messa qui.
$location = $webConfig.SelectSingleNode('/configuration/location')
$systemWeb = $location.SelectSingleNode('system.web')
if (-not $systemWeb) {
    $systemWeb = $location.AppendChild($webConfig.CreateElement('system.web'))
}
if (-not $systemWeb.SelectSingleNode('trust')) {
    $trust = $systemWeb.AppendChild($webConfig.CreateElement('trust'))
    $trust.SetAttribute('level', 'Full')
}
$webConfig.Save($webConfigPath)

# File che non devono arrivare sul server.
Get-ChildItem $site -Recurse -Include '*.pdb', 'appsettings.Development.json' | Remove-Item -Force

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

$version = (Get-Item (Join-Path $app 'BlogEngine.Web.dll')).VersionInfo.ProductVersion
@"
ExtraGeek-BlogEngine $version - pacchetto del $(Get-Date -Format 'dd/MM/yyyy HH:mm')
Framework: $Framework
Modalità: $Mode$(if ($Mode -eq 'SelfContained') { " ($Runtime)" })
Hosting IIS: $HostingModel

Procedura completa: docs\deploy-aruba.md
Tutto va nella ROOT del sito (la cartella con il nome del dominio), NON nella cartella "public".
1. Backup via FTP della root del sito (App_Data compresa).
2. Carica app_offline.htm nella root.
3. Carica il contenuto di sito\ nella root (sovrascrivendo): web.config e la cartella app\.
   Non cancellare le cartelle di Aruba (public, App_Data, mdb-database, cgi-bin...).
4. Cancella app_offline.htm dalla root e apri il sito.
L'eseguibile da abilitare nel pannello Aruba (Hosting Windows -> .NET) è app/BlogEngine.Web.exe.
"@ | Set-Content -Path (Join-Path $output 'LEGGIMI.txt') -Encoding utf8

$zip = "$output.zip"
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
Write-Host "Pacchetto pronto: $output" -ForegroundColor Green
Write-Host "Zip: $zip" -ForegroundColor Green
