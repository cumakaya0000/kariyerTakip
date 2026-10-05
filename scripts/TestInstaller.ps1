$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'BuildInstaller.ps1') -SkipPublish -TestBuild
$taskSetup = Join-Path $taskRoot 'artifacts/test-installer/KariyerTakip-Kurulum-Test.exe'
$taskInstall = [IO.Path]::GetFullPath((Join-Path $taskRoot 'tmp/installer-smoke'))
if (-not $taskInstall.StartsWith([IO.Path]::GetFullPath($taskRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Test yolu çalışma alanının dışında.' }
$taskGroup = 'KariyerTakip Kurulum Testi'
$taskGroupPath = Join-Path ([Environment]::GetFolderPath('Programs')) $taskGroup
$taskRegistryPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\KariyerTakip-Installer-SmokeTest_is1'
if ((Test-Path -LiteralPath $taskInstall) -or (Test-Path -LiteralPath $taskGroupPath) -or [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskRegistryPath)) { throw 'Önceki test kurulumu var; üzerine yazılmadı.' }
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'tmp') -Force | Out-Null
$taskLog = Join-Path $taskRoot 'tmp/installer-smoke-install.log'
try {
    $taskProcess = Start-Process -FilePath $taskSetup -WindowStyle Hidden -Wait -PassThru -ArgumentList @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $taskInstall + '"'),('/GROUP="' + $taskGroup + '"'),'/TASKS=startmenuicon',('/LOG="' + $taskLog + '"'))
    if ($taskProcess.ExitCode -ne 0) { throw "Test kurulumu başarısız ($($taskProcess.ExitCode)); günlük: $taskLog" }
    $taskKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskRegistryPath)
    if (-not $taskKey) { throw 'Denetim Masası kaldırma kaydı yok.' }
    try {
        if ($taskKey.GetValue('DisplayName') -ne 'KariyerTakip' -or $taskKey.GetValue('DisplayVersion') -ne '1.1.0') { throw 'Kaldırma kaydının adı/sürümü yanlış.' }
        if ($taskKey.GetValue('InstallLocation').TrimEnd('\') -ne $taskInstall) { throw 'Kayıtlı kurulum konumu yanlış.' }
        if (-not $taskKey.GetValue('UninstallString').Contains($taskInstall)) { throw 'Kaldırma komutu yanlış konuma gidiyor.' }
    } finally { $taskKey.Dispose() }
    $taskShell = New-Object -ComObject WScript.Shell
    foreach ($taskShortcutName in @('KariyerTakip.lnk',"KariyerTakip'i Kaldır.lnk")) {
        $taskLinkPath = Join-Path $taskGroupPath $taskShortcutName
        if (-not (Test-Path -LiteralPath $taskLinkPath)) { throw 'Başlat menüsü kısayolu yok.' }
        $taskLink = $taskShell.CreateShortcut($taskLinkPath)
        if (-not $taskLink.TargetPath.StartsWith($taskInstall + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Kısayol başka bir konuma gidiyor.' }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $taskInstall 'KariyerTakip.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskRoot 'artifacts/publish/win-x64/KariyerTakip.exe')).Hash) { throw 'Kurulan EXE yayınlanan EXE ile aynı değil.' }
    Write-Output 'Kurulum konumu, kaldırma kaydı, uygulama ve Başlat menüsü kısayolları doğrulandı.'
} finally {
    $taskUninstaller = Join-Path $taskInstall 'unins000.exe'
    if (Test-Path -LiteralPath $taskUninstaller) {
        $taskUninstallProcess = Start-Process -FilePath $taskUninstaller -WindowStyle Hidden -Wait -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
        if ($taskUninstallProcess.ExitCode -ne 0) { throw 'Test kaldırma işlemi başarısız.' }
    }
}
$taskLeftoverKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskRegistryPath)
if ($taskLeftoverKey) { $taskLeftoverKey.Dispose(); throw 'Test kaldırma kaydı temizlenmedi.' }
if ((Test-Path -LiteralPath (Join-Path $taskInstall 'KariyerTakip.exe')) -or (Test-Path -LiteralPath $taskGroupPath)) { throw 'Test dosyaları/kısayolları kaldırılmadı.' }
Write-Output 'Kaldırma ve test kaydının/kısayollarının temizlenmesi doğrulandı.'
