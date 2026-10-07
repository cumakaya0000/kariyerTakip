# Değişiklikler

## Yerel geliştirme — 2026-10-07

- Boş veya önceki güvenilir listenin yarısından küçük kaynak sonuçlarında ilan kaldırma atlanır ve kesinti başına tek uyarı oluşturulur. Kaldırma için iki ardışık güvenilir taramada kayıp olmak gerekir; sayaç veritabanında korunur. Kaldırma/iptal ve geri gelme bildirimleri yalnızca ilgili ilanlar için üretilir.
- Açık bölüm listesiyle uyuşmayan kadrolara otomatik bildirilmeyen “Büyük olasılıkla uygun değil” durumu eklendi. Bölüm listelerindeki bağlaçlar, öğrenim alt sınırları, kısaltma noktaları ve sürücü belgesi kapsamları desteklenir; birleşik bölüm adları ihtiyatla değerlendirilir.
- Çakışmalar yalnızca ilgili kriteri etkiler. KPSS yılları sınava bağlı ifadelerden çıkarılır; takvim tarihleri listeye katılmaz. Tecrübe alanlarında doğrulanmış eş adlar ve sözcük eşleşmesi kullanılır.
- Yaş hesabında “gün almamış” kalıbı doğrudan aranır. İlanda açık doğum tarihi sınırı varsa önceliklidir; belirtilmeyen tam sınır doğum günü kontrol gerektirir. AFAD'ın 2024 ilanındaki doğum tarihi örneği regresyon testine eklendi.
- Arşiv içe aktarma daha eski ilan, kadro ve değerlendirmeyle güncel kaydı ezmez; mevcut başvuru kararları ve notlar korunur.
- Profil kaydı tarama kilidini beklemez; yoğun işlem sırasında son profil yeniden değerlendirme kuyruğuna alınır. Özet mesajı yalnızca yeni uygun ilan oluştuğunda gönderilir.
- KPSS muafiyeti kadro şartlarını ezmez; açık muafiyet cümlesi gerekir. Yıl listeleri/aralıkları ve en yüksek geçerli puan desteklenir; puan türü tam eşleştirilir.
- Tecrübe/yaş yazım çeşitleri, tecrübe alt/üst sınırları, mezuniyet durumu ve öğrenim düzeyi kontrolleri genişletildi. Çakışan veya çıkarılamayan şartlar kontrol gerektirir.
- Şehir ve çalışma türü tercihleri yeterlilikten ayrıldı. Ayrıntılarda kaynak cümle ve çıkarım bilgisi gösterilir.
- Bölüm eş adları profil ekranından, doğrulanan kadro şartları ilan ayrıntısındaki düzenleme tablosundan değiştirilebilir.
- Profil kimliği ilan bildirimlerinin tekrar anahtarından çıkarıldı. Tarih değişikliği yeni uygunluk durumundan önce bildirilir.
- Başarılı, filtresiz kaynak taramasında kayıp ilanlar izlenir. Başarısız veya şüpheli taramalar kayıp serisini sıfırlar; filtreli taramalar kaldırma uygulamaz.
- PDF okuma hataları içerik hash'ine katılmaz; kontenjanlar yapılandırılmış JSON olarak saklanır. Son içerik değişikliği önce/şimdi tablosunda ve Telegram özetinde gösterilir.
- Başvuracağım ilanlar için .ics aktarımı ve kalan süre; JSON arşiv aktarımı, içe aktarma öncesi otomatik SQLite yedeği ve elle veritabanı yedeği eklendi.
- Değerlendirme geçmişi kadro başına son 20 kayıtla sınırlıdır; `Scan.EvaluationHistoryLimit` ile değiştirilebilir.
- Regresyon havuzu KPSS, yaş, tecrübe, tercihler, bölüm eşleştirme, önbellek, bildirim tekrarları ve veri bakımını kapsayacak şekilde genişletildi.

## 1.1.0 — Standart Windows kurulumu — 2026-10-05

- Windows Program Files altında tüm kullanıcılar için kurulum ve yalnızca mevcut hesaba kurma seçeneği eklendi; x86/x64/ARM64 uygulamaları aynı pakette seçilir.
- Masaüstü ve Başlat menüsü kısayolları ayrı sorulur; kurulum sonunda konum gösterilir, programı veya klasörünü açma isteğe bağlıdır.
- Denetim Masası kaldırma kaydı, çalışan program kontrolü ve varsayılan olarak kişisel verileri koruma akışı tamamlandı.
- Kurulum sonunda uygulama ilk oturumun kullanıcı yetkileriyle açılır. Windows ile başlatma programın kullanıcıya ait ayarlarından yönetilir.
- Kurulum/kaldırma ve uygulama dosyaları için mevcut sertifikayla imzalama desteği genişletildi.

## Yerel geliştirme — 2026-10-05

- Varsayılan pencere 1080×700, en küçük pencere 900×600 olacak şekilde arayüz sıkılaştırıldı; üst başlık, düğmeler, profil alanları ve ayrıntı metni küçültüldü.
- Koyu temada tarih alanı, tablo başlık/seçim renkleri, devre dışı düğmeler ve sütun menüsü düzeltildi; uyarı etiketleri daha okunabilir hale getirildi.
- İlan kaynakları arasında geçişte ortak görünümün fontu korunarak çizim hatası giderildi; tema ve küçük pencere regresyon kontrolleri genişletildi.
- İlan listesine başvuru durumu ve son tarih filtreleri, Türkçe kurum/unvan/şehir araması ve sütun başlıklarından sıralama eklendi.
- Kalan süre ve kadro sayısı sütunları, yaklaşan son tarih vurgusu, liste özeti ve sağ tıkla sütun görünürlüğü eklendi.
- Görünen ilanları topluca seçme, seçimleri temizleme ve seçili ilanları UTF-8 CSV dosyasına aktarma eklendi; seçimler filtreleme/sıralamada korunur.
- Kamu İlan sekmesine SBB ana sayfasını açan ayrı site düğmesi eklendi.
- SBB ilanını açarken 404 veren oturuma bağlı URL yerine indirilen resmî PDF açılır; eski kayıtların belgesi güncel bağlantıyla indirilir.
- PDF düğmesi, çift tıklama ve toplu açma aynı belge akışını kullanır. Telegram'da SBB listesi/arşivi bağlantısı gösterilir.

- Kamu İlan (SBB) ana sayfası taramaya eklendi; Kariyer Kapısı ve Kamu İlan ayrı sekmelerde gösterilir.
- Kaynak bilgisi veritabanında ve Telegram mesajlarında saklanır. Mevcut ilanlar Kariyer Kapısı olarak korunur.
- SBB bağlantı kodları değişse de ilan kimliği ve başvuru notları korunur; bir kaynağın erişim hatası diğer kaynağın taramasını durdurmaz.
- SBB PDF belgeleri oturum ve ana sayfa referansıyla okunur; tablolardaki kadro şartları ayrı ayrı profille değerlendirilir.
- PDF metni, genel şartlar ve açıkça belirtilen son başvuru saati önbelleğe alınır; okuma hatasında korunur.
- Ayrıştırılamayan veya görüntü PDF'leri açık kontrol gerekçesiyle gösterilir; OCR bu sürümde yoktur.
- Gerçek SBB sayfası, kaynak ayrımı, başvuru notlarının korunması ve kısmi tarama için regresyon testleri eklendi.

## Yerel geliştirme — 2026-10-04

- KT simgesi exe, ana pencere, sistem tepsisi ve kısayollara eklendi.
- Türkçe tanıtım adımları, klasör seçimi, masaüstü/Windows başlangıç tercihleri ve kurulum sonrası açma seçeneği olan tek kurulum exe'si eklendi.
- Kurulum x86/x64/ARM64 sürümünü otomatik seçer; .NET dahildir. Denetim Masası kaldırma kaydı ve veri silme tercihi eklenir.

- Kaldırma betiği gerçek veri klasörlerini ve başlangıç kaydını temizler; hata ve çalışan süreçte başarı bildirmez.
- Global mutex arayüz ve zamanlanmış taramanın eşzamanlı çalışmasını önler; SQLite bağlantıları 5 saniye bekleme kullanır ve WAL doğrulanır.
- Bozuk JSON yedeklenerek varsayılanlarla açılır; profil adı çakışmaları ve DPAPI okuma hataları açılışı çökertmez.
- API beklemesi 60 saniyeyle sınırlanır; Türkiye saat dilimi Windows ve UTC+3 yedeklerine sahiptir.
- Gerçek API yanıtlarıyla sözleşme testleri, kalıcı API sağlık sayacı ve yerel değerlendirme geri bildirimi eklenir.
- Telegram mesajları otomatik değerlendirme notu içerir; istemci dürüst uygulama kimliğiyle istek yapar.
- Yayın sıkıştırması, SHA256, isteğe bağlı sertifika imzası ve CHANGELOG sürüm notları eklenir.
- SDK sabitleme, Dependabot, güvenlik denetimi, SECURITY.md ve editör ayarları eklenir.
- Hatırlatmalar DI'a, tarama ve tepsi yaşam döngüsü ayrı sınıflara taşınır; güvenilirlik testleri genişletilir.

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
