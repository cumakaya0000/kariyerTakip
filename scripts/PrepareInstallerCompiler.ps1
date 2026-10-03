$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskTools = Join-Path $taskRoot 'artifacts/tools'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskDownload = Join-Path $taskTools 'innosetup-7.1.0-x64.exe'
Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $taskDownload
$taskSignature = Get-AuthenticodeSignature -LiteralPath $taskDownload
if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notlike '*Pyrsys*') { throw 'Inno Setup yayıncı imzası doğrulanamadı.' }
$taskDirectory = Join-Path $taskTools 'inno'
$taskProcess = Start-Process -FilePath $taskDownload -ArgumentList @('/PORTABLE=1','/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $taskDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
if ($taskProcess.ExitCode -ne 0) { throw 'Portable derleyici hazırlığı başarısız.' }
Write-Output (Join-Path $taskDirectory 'ISCC.exe')
