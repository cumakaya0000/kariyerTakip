param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Thumbprint)
$ErrorActionPreference = 'Stop'
$taskCertificate = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $Thumbprint)
$taskSignature = Set-AuthenticodeSignature -LiteralPath $Path -Certificate $taskCertificate -HashAlgorithm SHA256
if ($taskSignature.SignerCertificate.Thumbprint -ne $taskCertificate.Thumbprint) { throw 'Dosya beklenen sertifikayla imzalanamadı.' }
if ($taskSignature.Status -ne 'Valid') {
    $taskChain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $taskTrusted = $taskChain.Build($taskCertificate)
        $taskOnlyUntrustedRoot = -not $taskTrusted -and $taskChain.ChainStatus.Count -gt 0 -and
            @($taskChain.ChainStatus | Where-Object { $_.Status -ne [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot }).Count -eq 0
        if ($taskSignature.Status -ne 'UnknownError' -or -not $taskOnlyUntrustedRoot) { throw 'Kod imzası doğrulanamadı.' }
        Write-Warning 'Dosya yerel geliştirme sertifikasıyla imzalandı; yayıncı sertifikası genel olarak güvenilir değildir.'
    } finally { $taskChain.Dispose() }
}
