# Kaynak

2026-10-03 tarihinde KariyerTakip/1.0 User-Agent ile kamuya açık Kariyer Kapısı API'sinden kaydedildi. Kullanıcı hesabı veya kimlik doğrulaması kullanılmadı.

- `announcements.json`: POST https://api.kariyerkapisi.gov.tr/api/ilan/GetIseAlimPage; filtreler boş, kurum 0, il 0, ilan türü 0.
- `preview.json`: POST https://api.kariyerkapisi.gov.tr/api/ilan/GetIlanPreviewPublic
- `positions.json`: POST https://api.kariyerkapisi.gov.tr/api/altilan/GetAltIlanInfoByIlanIdPublic

Son iki isteğin ilanGuid değeri: `30329a62-04f3-445a-985f-f6a0b8c2e1b7` (Göç İdaresi Başkanlığı sözleşmeli bilişim personeli). Dosyalar ham kamuya açık yanıtları içerir; testler ağ erişimi yapmaz. Aktiflik tarihleri test içinde sabit örneğin tarihlerine uyarlanır, böylece zaman geçince test bozulmaz.
