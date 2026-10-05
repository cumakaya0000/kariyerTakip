param([string]$CompilerPath, [switch]$SkipPublish, [switch]$TestBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $CompilerPath) {
    $taskCandidates = @((Join-Path $taskRoot 'artifacts/tools/inno/ISCC.exe'), 'C:\Program Files\Inno Setup 7\ISCC.exe', 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe')
    $CompilerPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup 7 ISCC.exe yolu -CompilerPath ile verilmelidir.' }
$taskSigningThumbprint = $env:KARIYERTAKIP_SIGN_THUMBPRINT
if (-not $taskSigningThumbprint) {
    $taskLocalCertificate = Get-Item 'Cert:\CurrentUser\My\123998ADC41E0FE2360514686A2935951A23CCF7' -ErrorAction SilentlyContinue
    if ($taskLocalCertificate) { $taskSigningThumbprint = $taskLocalCertificate.Thumbprint }
}
$taskSignTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $SkipPublish) { foreach ($taskRuntime in @('win-x86','win-x64','win-arm64')) {
    $taskOutput = Join-Path $taskRoot ('artifacts/publish/' + $taskRuntime)
    dotnet publish (Join-Path $taskRoot 'KariyerTakip.csproj') -c Release -r $taskRuntime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw "$taskRuntime yayınlanamadı." }
    if ($taskSigningThumbprint -and $taskSignTool) {
        & $taskSignTool.Source sign /sha1 $taskSigningThumbprint /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 (Join-Path $taskOutput 'KariyerTakip.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Uygulama kod imzası başarısız.' }
    } elseif ($taskSigningThumbprint) {
        & (Join-Path $PSScriptRoot 'SignReleaseFile.ps1') -Path (Join-Path $taskOutput 'KariyerTakip.exe') -Thumbprint $taskSigningThumbprint
    }
} }
$taskArguments = @()
if ($TestBuild) { $taskArguments += @('/DTestBuild','/DX64Only') }
if ($taskSigningThumbprint) {
    $taskArguments += '/DSignedBuild'
    if ($taskSignTool) {
        $taskArguments += ('/Skt-sign=$q' + $taskSignTool.Source + '$q sign /sha1 ' + $taskSigningThumbprint + ' /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 $f')
    } else {
        $taskPowerShellPath = (Get-Process -Id $PID).Path
        $taskArguments += ('/Skt-sign=$q' + $taskPowerShellPath + '$q -NoProfile -File $q' + (Join-Path $PSScriptRoot 'SignReleaseFile.ps1') + '$q -Path $f -Thumbprint ' + $taskSigningThumbprint)
    }
}
$taskArguments += (Join-Path $taskRoot 'installer/KariyerTakip.iss')
& $CompilerPath @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Kurulum exe dosyası oluşturulamadı.' }
$taskSetup = Join-Path $taskRoot $(if ($TestBuild) { 'artifacts/test-installer/KariyerTakip-Kurulum-Test.exe' } else { 'artifacts/KariyerTakip-Kurulum.exe' })
$taskHash = (Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($taskSetup + '.sha256', "$taskHash  $([IO.Path]::GetFileName($taskSetup))`n")
Write-Output $taskSetup
