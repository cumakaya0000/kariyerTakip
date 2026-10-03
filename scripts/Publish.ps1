$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/publish/win-x64'
dotnet publish (Join-Path $taskRoot 'KariyerTakip.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Yayın paketi oluşturulamadı.' }
# Publishing never copies local credentials or personal profiles into the package.
Copy-Item -LiteralPath (Join-Path $taskRoot 'appsettings.example.json') -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $taskRoot 'profile.example.json') -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $taskRoot 'scripts/ZamanlanmisTarama.bat') -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskOutput
$taskExecutable = Join-Path $taskOutput 'KariyerTakip.exe'
if ($env:KARIYERTAKIP_SIGN_THUMBPRINT) {
    $taskSignTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($taskSignTool) {
        & $taskSignTool.Source sign /sha1 $env:KARIYERTAKIP_SIGN_THUMBPRINT /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 $taskExecutable
    } else {
        $taskCert = Get-Item ("Cert:\CurrentUser\My\" + $env:KARIYERTAKIP_SIGN_THUMBPRINT) -ErrorAction SilentlyContinue
        if ($taskCert) { Set-AuthenticodeSignature -FilePath $taskExecutable -Certificate $taskCert | Out-Null }
    }
    if ($LASTEXITCODE -ne 0) { throw 'Kod imzası başarısız.' }
} else {
    $localCert = Get-Item "Cert:\CurrentUser\My\123998ADC41E0FE2360514686A2935951A23CCF7" -ErrorAction SilentlyContinue
    if ($localCert) {
        Set-AuthenticodeSignature -FilePath $taskExecutable -Certificate $localCert | Out-Null
        Write-Output 'Yerel geliştirici sertifikası ile imzalandı.'
    } else {
        Write-Output 'Sertifika yapılandırılmadı; exe imzasız yayınlanıyor.'
    }
}
$taskExeHash = (Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $taskOutput 'SHA256SUMS.txt'), "$taskExeHash  KariyerTakip.exe`n")
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchivePath = Join-Path $taskRoot 'artifacts/KariyerTakip-win-x64.zip'
if (Test-Path -LiteralPath $taskArchivePath) { Remove-Item -LiteralPath $taskArchivePath }
$taskArchive = [System.IO.Compression.ZipFile]::Open($taskArchivePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in @('KariyerTakip.exe', 'SHA256SUMS.txt', 'appsettings.example.json', 'profile.example.json', 'ZamanlanmisTarama.bat', 'README.md')) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskOutput $taskName), $taskName) | Out-Null
    }
    foreach ($taskName in @('CHANGELOG.md', 'SECURITY.md', 'Kaldir.bat', 'scripts/Uninstall.ps1', 'docs/images/ilanlar.png', 'docs/images/profiller.png', 'docs/images/ayarlar.png')) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskRoot $taskName), $taskName) | Out-Null
    }
} finally { $taskArchive.Dispose() }
$taskZipHash = (Get-FileHash -LiteralPath $taskArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($taskArchivePath + '.sha256', "$taskZipHash  KariyerTakip-win-x64.zip`n")
Write-Output $taskArchivePath
