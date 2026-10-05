[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$ApplicationDirectory)
$ErrorActionPreference = 'Stop'
$taskApplication = [IO.Path]::GetFullPath($ApplicationDirectory).TrimEnd('\')
$taskLocalData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KariyerTakip'
$taskRoots = @($taskApplication, $taskLocalData)
if ($env:KARIYERTAKIP_DATA_DIR) { $taskRoots += [IO.Path]::GetFullPath($env:KARIYERTAKIP_DATA_DIR) }
$taskMutex = [Threading.Mutex]::new($false, 'Global\KariyerTakip')
$taskOwned = $false
try {
    try { $taskOwned = $taskMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $taskOwned = $true }
    if (-not $taskOwned) { throw 'KariyerTakip çalışıyor. Arayüzü ve zamanlanmış görevleri kapatın.' }
    function Remove-TaskChild([string]$root, [string]$relative) {
        $taskResolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\')
        $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskResolvedRoot $relative))
        if (-not $taskTarget.StartsWith($taskResolvedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Güvensiz hedef: $taskTarget" }
        $taskAncestor = $taskResolvedRoot
        foreach ($taskSegment in $relative.Split('\')) {
            $taskAncestor = Join-Path $taskAncestor $taskSegment
            if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Bağlantı üzerinden silme engellendi: $taskAncestor"
            }
        }
        if (Test-Path -LiteralPath $taskTarget) {
            $taskItem = Get-Item -LiteralPath $taskTarget -Force
            if ($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Bağlantı hedefi silinmedi: $taskTarget" }
            if ($taskItem.PSIsContainer) {
                $taskLinks = Get-ChildItem -LiteralPath $taskTarget -Recurse -Force -Attributes ReparsePoint
                if ($taskLinks) { throw "Klasör bağlantı içeriyor: $taskTarget" }
            }
            if ($PSCmdlet.ShouldProcess($taskTarget, 'Sil')) { Remove-Item -LiteralPath $taskTarget -Recurse -Force }
        }
    }
    foreach ($taskRoot in ($taskRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $taskRoot)) { continue }
        if ((Get-Item -LiteralPath $taskRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Veri klasörü bağlantı olamaz: $taskRoot" }
        foreach ($taskName in @('kariyertakip.db','kariyertakip.db-shm','kariyertakip.db-wal','telegram.secret','profiles.json','profile.json','appsettings.json','api-health.json','logs','feedback-fixtures','documents')) {
            Remove-TaskChild $taskRoot $taskName
        }
        Get-ChildItem -LiteralPath $taskRoot -File -Force | Where-Object { $_.Name -match '^(appsettings|profile|profiles|api-health)\.json\.bak(\..+)?$' } | ForEach-Object { Remove-TaskChild $taskRoot $_.Name }
    }
    foreach ($taskName in @('bin','obj','tests\KariyerTakip.Tests\bin','tests\KariyerTakip.Tests\obj')) { Remove-TaskChild $taskApplication $taskName }
    $taskRunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if ((Get-ItemProperty -LiteralPath $taskRunKey -Name KariyerTakip -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess('HKCU Run KariyerTakip', 'Başlangıç kaydını kaldır')) {
        Remove-ItemProperty -LiteralPath $taskRunKey -Name KariyerTakip
    }
    Write-Output 'Yerel uygulama verileri ve mevcut kullanıcının başlangıç kaydı temizlendi.'
} finally {
    if ($taskOwned) { $taskMutex.ReleaseMutex() }
    $taskMutex.Dispose()
}
