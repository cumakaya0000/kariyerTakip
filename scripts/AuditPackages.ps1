$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskAudit = dotnet list (Join-Path $taskRoot 'KariyerTakip.slnx') package --vulnerable --include-transitive --format json --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Bağımlılık güvenlik taraması çalıştırılamadı.' }
$taskReport = ($taskAudit -join "`n") | ConvertFrom-Json
$taskPackages = $taskReport.projects.frameworks.topLevelPackages + $taskReport.projects.frameworks.transitivePackages
if ($taskPackages | Where-Object { $_.vulnerabilities.Count -gt 0 }) { throw 'Bilinen güvenlik açığı olan NuGet bağımlılığı bulundu.' }
Write-Output 'Bilinen NuGet güvenlik açığı bulunmadı.'
