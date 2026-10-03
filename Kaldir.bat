@echo off
chcp 65001 >nul
title KariyerTakip - Kaldırma
echo Bu işlem yerel profilleri, tokenı, veritabanını, günlükleri ve başlangıç kaydını siler.
echo Önce KariyerTakip'i ve zamanlanmış taramaları kapatın.
set /p "ONAY=Verileri temizlemek istiyor musunuz? (E/H): "
if /i "%ONAY%" neq "E" exit /b 0
powershell.exe -NoProfile -File "%~dp0scripts\Uninstall.ps1" -ApplicationDirectory "%~dp0."
if errorlevel 1 (
    echo Temizleme tamamlanamadı. Yukarıdaki hatayı kontrol edin.
    pause
    exit /b 1
)
echo Temizleme tamamlandı.
pause
exit /b 0
