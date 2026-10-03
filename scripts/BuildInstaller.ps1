param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $CompilerPath) {
    $taskCandidates = @((Join-Path $taskRoot 'artifacts/tools/inno/ISCC.exe'), 'C:\Program Files\Inno Setup 7\ISCC.exe', 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe')
    $CompilerPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup 7 ISCC.exe yolu -CompilerPath ile verilmelidir.' }
foreach ($taskRuntime in @('win-x86','win-x64','win-arm64')) {
    $taskOutput = Join-Path $taskRoot ('artifacts/publish/' + $taskRuntime)
    dotnet publish (Join-Path $taskRoot 'KariyerTakip.csproj') -c Release -r $taskRuntime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw "$taskRuntime yayınlanamadı." }
    if ($env:KARIYERTAKIP_SIGN_THUMBPRINT) {
        signtool.exe sign /sha1 $env:KARIYERTAKIP_SIGN_THUMBPRINT /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 (Join-Path $taskOutput 'KariyerTakip.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Uygulama kod imzası başarısız.' }
    }
}
$taskArguments = @()
if ($env:KARIYERTAKIP_SIGN_THUMBPRINT) {
    $taskArguments += '/DSignedBuild'
    $taskArguments += ('/Skt-sign=signtool.exe sign /sha1 ' + $env:KARIYERTAKIP_SIGN_THUMBPRINT + ' /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 $f')
}
$taskArguments += (Join-Path $taskRoot 'installer/KariyerTakip.iss')
& $CompilerPath @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Kurulum exe dosyası oluşturulamadı.' }
$taskSetup = Join-Path $taskRoot 'artifacts/KariyerTakip-Kurulum.exe'
$taskHash = (Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($taskSetup + '.sha256', "$taskHash  KariyerTakip-Kurulum.exe`n")
Write-Output $taskSetup
