@echo off
chcp 65001 >nul
title KariyerTakip - Kurulum ve Başlatma
echo ======================================================
echo   💼 KariyerTakip - Tek Tıkla Kurulum ve Başlatma
echo ======================================================
echo.

echo [.NET 10 SDK Kontrolü Yapılıyor...]
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [HATA] Sisteminizde .NET SDK bulunamadı!
    echo Lütfen https://dotnet.microsoft.com/download adresinden .NET 10 LTS yükleyiniz.
    pause
    exit /b 1
)

echo [1/3] NuGet Paketleri Yükleniyor...
dotnet restore

echo.
echo [2/3] Proje Derleniyor...
dotnet build -c Release

if %errorlevel% neq 0 (
    echo [HATA] Proje derlenirken hata oluştu!
    pause
    exit /b 1
)

echo.
echo [3/3] Uygulama Başlatılıyor...
start "" "%~dp0bin\Release\net10.0-windows\KariyerTakip.exe"

echo.
echo ✅ KariyerTakip başarıyla başlatıldı!
exit /b 0
