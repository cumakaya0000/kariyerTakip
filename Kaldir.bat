@echo off
chcp 65001 >nul
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
echo [1/3] Çalışan KariyerTakip süreçleri kapatılıyor...
taskkill /f /im KariyerTakip.exe >nul 2>&1

echo [2/3] Derleme klasörleri (bin, obj) siliniyor...
if exist "bin" rd /s /q "bin"
if exist "obj" rd /s /q "obj"

echo [3/3] Geçici veritabanı ve log dosyaları temizleniyor...
if exist "kariyertakip.db" del /f /q "kariyertakip.db"
if exist "kariyertakip.db-shm" del /f /q "kariyertakip.db-shm"
if exist "kariyertakip.db-wal" del /f /q "kariyertakip.db-wal"

echo.
echo ✅ KariyerTakip çalışma dosyaları başarıyla temizlendi!
pause
exit /b 0
