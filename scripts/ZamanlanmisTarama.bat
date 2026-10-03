@echo off
cd /d "%~dp0"
REM Gorev Zamanlayici: Program = bu .bat, saatlik tetikleyici, ayni Windows hesabi.
if exist "%~dp0KariyerTakip.exe" (
    "%~dp0KariyerTakip.exe" --scan-once
) else (
    echo KariyerTakip.exe bulunamadi. Bu dosyayi yayin paketindeki exe ile ayni klasore koyun.
    exit /b 1
)
exit /b %errorlevel%
