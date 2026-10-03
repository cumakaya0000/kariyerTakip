@echo off
chcp 65001 >nul
cd /d "%~dp0"
title KariyerTakip - Temizleme ve Kaldırma
echo ======================================================
echo   🗑️ KariyerTakip - Kaldırma ve Temizleme Sihirbazı
echo ======================================================
echo.

set /p ONAY=Derleme çıktılarını ve yerel veritabanını temizlemek istiyor musunuz? (E/H): 
if /i "%ONAY%" neq "E" (
    echo İşlem iptal edildi.
    pause
    exit /b 0
)

echo.
echo [1/3] Çalışan KariyerTakip süreçleri sonlandırılıyor...
taskkill /f /im KariyerTakip.exe >nul 2>&1

echo [2/3] Derleme klasörleri (bin, obj, tests/*/bin) siliniyor...
if exist "%~dp0bin" rd /s /q "%~dp0bin"
if exist "%~dp0obj" rd /s /q "%~dp0obj"
if exist "%~dp0tests\KariyerTakip.Tests\bin" rd /s /q "%~dp0tests\KariyerTakip.Tests\bin"
if exist "%~dp0tests\KariyerTakip.Tests\obj" rd /s /q "%~dp0tests\KariyerTakip.Tests\obj"

echo [3/3] Yerel veritabanı ve geçici loglar temizleniyor...
if exist "%~dp0kariyertakip.db" del /f /q "%~dp0kariyertakip.db"
if exist "%~dp0kariyertakip.db-shm" del /f /q "%~dp0kariyertakip.db-shm"
if exist "%~dp0kariyertakip.db-wal" del /f /q "%~dp0kariyertakip.db-wal"

echo.
echo ✅ KariyerTakip çalışma dosyaları ve derleme çıktıları başarıyla temizlendi!
pause
exit /b 0
