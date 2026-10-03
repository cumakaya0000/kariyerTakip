# Değişiklikler

## Yayına hazırlanan sürüm — 2026-10-03

- Test projesi çözüme eklendi; kurulum adımları ve başarısızlık kontrolleri düzeltildi.
- Profil şeması, ay cinsinden tecrübe ve bilinmeyen doğum tarihi düzeltildi. Çoklu profil ve KPSS puan tablosu eklendi.
- Ayar kaydı JSON bölümlerini koruyan asenkron servise taşındı. Token DPAPI ile korunur; gerçek ayar/profiller Git'ten çıkarıldı.
- API yeniden denemelerinde üstel bekleme, jitter, Retry-After ve iptal desteği; sınırlı paralellik ve istek aralığı eklendi.
- Kadro kimlikleri API sırasından bağımsız hale getirildi; eski kimlikler ve geçmiş korunur.
- Telegram uzun satır/emoji/HTML bölme, parça ilerleme kaydı, 429 beklemesi ve kalıcı hata davranışı düzeltildi.
- Tarihler UTC tutulup Türkiye saatiyle gösterilir; başvuru takibi, notlar ve son tarih hatırlatmaları eklendi.
- Sekmeler UserControl bileşenlerine ayrıldı. Sistem tepsisi ve Windows ile başlatma seçenekleri eklendi.
- Dosya günlükleri, ayrı headless sonuç kodları, Windows CI ve x64 yayın/zip/Release iş akışı eklendi.
- Arayüz görselleri, sorun giderme ve katkı/yayın açıklamaları eklendi.
