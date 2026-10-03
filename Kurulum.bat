@echo off
chcp 65001 >nul
cd /d "%~dp0"
title KariyerTakip - Kurulum ve Başlatma
echo ======================================================
echo   💼 KariyerTakip - Kurulum ve Başlatma Sihirbazı
echo ======================================================
echo.

echo [.NET 10 SDK Kontrolü Yapılıyor...]
dotnet --list-sdks | findstr /R "10\." >nul 2>&1
if %errorlevel% neq 0 (
    echo [HATA] Sisteminizde .NET 10 SDK bulunamadı!
    echo Mevcut .NET sürümleriniz:
    dotnet --list-sdks 2>nul
    echo.
    echo Lütfen https://dotnet.microsoft.com/download/dotnet/10.0 adresinden .NET 10 LTS SDK yükleyiniz.
    pause
    exit /b 1
)

echo [1/3] NuGet Bağımlılıkları Geri Yükleniyor...
dotnet restore KariyerTakip.slnx

echo.
echo [2/3] Proje Release Modunda Derleniyor...
dotnet build KariyerTakip.slnx -c Release

if %errorlevel% neq 0 (
    echo [HATA] Proje derlenirken hata oluştu!
    pause
    exit /b 1
)

echo.
echo [3/3] Birim Testleri Koşturuluyor...
dotnet test KariyerTakip.slnx --no-build -c Release

if %errorlevel% neq 0 (
    echo [HATA] Birim testleri başarısız oldu!
    pause
    exit /b 1
)

echo.
echo [4/4] Uygulama Başlatılıyor...
start "" "%~dp0bin\Release\net10.0-windows\KariyerTakip.exe"

echo.
echo ✅ KariyerTakip başarıyla derlendi ve pencereli arayüz başlatıldı!
exit /b 0
