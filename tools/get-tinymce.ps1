<#
.SYNOPSIS
    Scarica TinyMCE e la traduzione italiana da npm in src/BlogEngine.Web/wwwroot/lib/tinymce.

.DESCRIPTION
    TinyMCE dalla versione 7 ha licenza GPL: non sta nel repository (che ha licenza MS-RL) ma viene scaricato
    durante la build, nella versione indicata qui sotto, e finisce solo nei pacchetti dei siti.
    Ogni archivio viene controllato con l'impronta SHA-512 pubblicata da npm: se non corrisponde, non si installa niente.

    La build di BlogEngine.Web lancia questo script da sola quando la cartella manca o quando lo script cambia
    (per esempio dopo aver aggiornato la versione). Per aggiornare TinyMCE:
      1. cercare la nuova versione su https://www.npmjs.com/package/tinymce (meglio se pubblicata da almeno due settimane);
      2. copiare qui sotto versione e "integrity" (npm view tinymce@<versione> dist.integrity, oppure
         https://registry.npmjs.org/tinymce/<versione> -> dist.integrity);
      3. ricompilare e provare l'editor di post, pagine e widget.

.PARAMETER Destination
    Cartella in cui installare TinyMCE. Viene svuotata e ricreata.
#>
[CmdletBinding()]
param(
    [string] $Destination
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$packages = @(
    @{
        Name      = 'tinymce'
        Version   = '8.10.2'
        Integrity = 'sha512-Na5apA/kwb5o5HrHAZWnWssvZFZRph7gyROSXyvkAwS7E/59qEUttQx2ACfHLkM5EUyzCz2ww9PMm5/VpeZgIA=='
    },
    @{
        # Traduzioni dell'interfaccia (cartella langs8 = TinyMCE 8).
        Name      = 'tinymce-i18n'
        Version   = '26.11.2'
        Integrity = 'sha512-2QCB4Z0dlFhyrQHlP5soLaeIHn8KL/C4GZ8FRYMf5ZZGZutsyveWrOZXEEo6daTmcdLI8AVww1SVU7hAeoWXxg=='
    }
)

# Lingue dell'interfaccia dell'editor da installare (l'inglese è già dentro TinyMCE).
$languages = @('it')

# Parti di TinyMCE usate dall'amministrazione: solo i file minimizzati.
$folders = @('icons\default', 'models\dom', 'plugins', 'themes\silver', 'skins\ui\oxide', 'skins\content\default')
$skipPlugins = @('help')   # ha centinaia di file di traduzione e non serve

# In Windows PowerShell $PSScriptRoot non è disponibile nei valori predefiniti dei parametri.
if (-not $Destination) { $Destination = Join-Path $PSScriptRoot '..\src\BlogEngine.Web\wwwroot\lib\tinymce' }
$Destination = [IO.Path]::GetFullPath($Destination)
$work = Join-Path ([IO.Path]::GetTempPath()) ('tinymce-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

function Get-Package($package) {
    $file = Join-Path $work "$($package.Name).tgz"
    $url = "https://registry.npmjs.org/$($package.Name)/-/$($package.Name)-$($package.Version).tgz"
    Write-Host "Scarico $($package.Name) $($package.Version)..."
    Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing

    $sha = [Security.Cryptography.SHA512]::Create()
    try {
        $stream = [IO.File]::OpenRead($file)
        try { $hash = 'sha512-' + [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose() }
    } finally { $sha.Dispose() }
    if ($hash -ne $package.Integrity) {
        throw "L'archivio di $($package.Name) $($package.Version) non corrisponde all'impronta attesa: non viene installato."
    }

    $folder = Join-Path $work $package.Name
    New-Item -ItemType Directory -Path $folder | Out-Null
    # Su Windows il tar di sistema: quello di Git, se viene prima nel PATH, scambia "C:" per un server remoto.
    $tar = if ($env:SystemRoot -and (Test-Path (Join-Path $env:SystemRoot 'System32\tar.exe'))) { Join-Path $env:SystemRoot 'System32\tar.exe' } else { 'tar' }
    & $tar -xzf $file -C $folder
    if ($LASTEXITCODE -ne 0) { throw "Impossibile estrarre $file (tar, codice $LASTEXITCODE)." }
    return (Join-Path $folder 'package')
}

function Copy-File($source, $target) {
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}

try {
    $tinymce = Get-Package $packages[0]
    $i18n = Get-Package $packages[1]

    $staging = Join-Path $work 'out'
    Copy-File (Join-Path $tinymce 'tinymce.min.js') (Join-Path $staging 'tinymce.min.js')
    Copy-File (Join-Path $tinymce 'license.md') (Join-Path $staging 'license.md')
    Copy-File (Join-Path $tinymce 'notices.txt') (Join-Path $staging 'notices.txt')

    foreach ($folder in $folders) {
        $root = Join-Path $tinymce $folder
        Get-ChildItem -LiteralPath $root -Recurse -File |
            Where-Object { $_.Name -like '*.min.js' -or $_.Name -like '*.min.css' } |
            ForEach-Object {
                $relative = $_.FullName.Substring($tinymce.Length + 1)
                $parts = $relative -split '[\\/]'
                if ($parts[0] -eq 'plugins' -and $skipPlugins -contains $parts[1]) { return }
                Copy-File $_.FullName (Join-Path $staging $relative)
            }
    }

    foreach ($language in $languages) {
        Copy-File (Join-Path $i18n "langs8\$language.js") (Join-Path $staging "langs\$language.js")
    }
    Copy-File (Join-Path $i18n 'LICENSE.md') (Join-Path $staging 'langs\LICENSE.md')
    Copy-File (Join-Path $i18n 'NOTICE.md') (Join-Path $staging 'langs\NOTICE.md')

    # Segnaposto letto dalla build: dice quale versione è installata.
    $stamp = ($packages | ForEach-Object { "$($_.Name) $($_.Version)" }) -join "`r`n"
    Set-Content -LiteralPath (Join-Path $staging 'version.txt') -Value $stamp -Encoding ASCII

    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    New-Item -ItemType Directory -Path (Split-Path $Destination) -Force | Out-Null
    Move-Item -LiteralPath $staging -Destination $Destination
    Write-Host "TinyMCE $($packages[0].Version) installato in $Destination"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
