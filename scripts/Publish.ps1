$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/publish/win-x64'
dotnet publish (Join-Path $taskRoot 'KariyerTakip.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Yayın paketi oluşturulamadı.' }
# Publishing never copies local credentials or personal profiles into the package.
Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/ZamanlanmisTarama.bat') -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskOutput
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchivePath = Join-Path $taskRoot 'artifacts/KariyerTakip-win-x64.zip'
if (Test-Path -LiteralPath $taskArchivePath) { Remove-Item -LiteralPath $taskArchivePath }
$taskArchive = [System.IO.Compression.ZipFile]::Open($taskArchivePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in @('KariyerTakip.exe', 'appsettings.example.json', 'profile.example.json', 'ZamanlanmisTarama.bat', 'README.md')) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskOutput $taskName), $taskName) | Out-Null
    }
    foreach ($taskName in @('CHANGELOG.md', 'docs/images/ilanlar.png', 'docs/images/profiller.png', 'docs/images/ayarlar.png')) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskRoot $taskName), $taskName) | Out-Null
    }
} finally { $taskArchive.Dispose() }
Write-Output $taskArchivePath
