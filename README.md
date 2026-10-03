# 💼 KariyerTakip — Kişisel Kamu İlan ve Kadro Uygunluk Asistanı

**KariyerTakip**, T.C. Cumhurbaşkanlığı İnsan Kaynakları Ofisi **Kariyer Kapısı Kamu İşe Alım Portalı** üzerindeki aktif ilanları ve bu ilanlara ait tüm alt kadroları tarayan, ilan metinlerini ve kadro tablolarını kurallarla ayrıştırıp kullanıcının profiline göre (bölüm, ön lisans/lisans, KPSS P93 puanı/sınav yılı, mesleki tecrübe, yaş, ehliyet ve şehir) değerlendiren, sonuçları yerel SQLite veritabanında saklayan, masaüstü arayüzünde listeleyen ve Telegram üzerinden bildiren **C# / .NET 10 LTS** tabanlı bir masaüstü uygulamasıdır.

---

## 🌟 Öne Çıkan Özellikler ve Mimari İyileştirmeler

- 🖥️ **Modern Windows Grafik Arayüzü (WinForms):** Arka planda siyah CMD/Terminal penceresi açılmaz; doğrudan pencereli masaüstü uygulaması olarak çalışır.
- ⚡ **Ekransız / Arka Plan Çalışma Modu (`--scan-once`):** Windows Görev Zamanlayıcı veya zamanlanmış görevler için GUI açmadan çalışıp taramayı ve bildirimleri tamamlayarak kapanabilir.
- 🔍 **Resmî Kariyer Kapısı API Entegrasyonu:** Kamuya açık resmî JSON API uç noktalarından veri çeker; geçici ağ hatalarında (429/502/503) üstel geri çekilme (exponential backoff) ile yeniden dener.
- 🛡️ **Hata Sırasında Veri Koruma:** Ağ kesintisi veya sunucu hatası durumunda önbellekteki mevcut kadro ve ilan verileri silinmez, korunur.
- 🎯 **3 Durumlu Akıllı Uygunluk Motoru (Tri-State Decision Engine):** Yalnızca ilan başlığına bakmaz; ilanın içindeki her bir kadro satırını tek tek inceler.
  - ✅ **Şartlara Uygun (Eligible):** İlanın açıkça talep ettiği tüm zorunlu şartlar (Bölüm, Düzey, KPSS Taban Puanı, Sınav Yılı, Tecrübe) kullanıcı profili tarafından tam karşılanıyor.
  - ⚠️ **Kontrol Gerekli (NeedsReview):** Kadro bilişimle ilgili ancak kılavuz kontrolü, özel sertifika/belge veya tecrübe doğrulaması gerekiyor ya da metinden KPSS şartı çıkarılamamış. *(Asla belirsiz şartlar varsayılan olarak sağlanmış sayılmaz).*
  - ❌ **Uygun Değil (Ineligible):** En az bir zorunlu şart (örneğin sadece 4 yıllık lisans mezuniyeti istenmesi veya KPSS puanının tabanın altında kalması) sağlanmıyor.
- 🗄️ **Kalıcı Kadro Kimlikleri & Sürümlü Değerlendirme:** Kadrolar silinip yeniden eklenmez; benzersiz `PositionKey` ile güncellenir. Değerlendirmeler güncel profil ve ilan sürümüne göre sorgulanır; eski kayıtlar yeni kararları ezmez.
- 📲 **Güvenli Telegram Bildirim Kuyruğu (Outbox):** 
  - Telegram yapılandırılmadığında veya kapalıyken mesajlar "gönderildi" sayılmaz (`Disabled` olarak işaretlenir).
  - Her bildirim olay bazlı benzersiz bir anahtar (`DeduplicationKey`) ile kaydedilir; mükerrer bildirim oluşması engellenir.
  - Uzun ilan mesajları Telegram sınırlarına uygun olarak parçalara bölünür.
- 🔄 **Yerel Hızlı Yeniden Değerlendirme:** Profilinizde (bölüm, KPSS puanı vb.) değişiklik yaptığınızda internetten tekrar indirmeye gerek kalmadan önbellekteki tüm aktif ilanlar SQLite üzerinden anında yerel olarak yeniden değerlendirilir.

---

## 🛠️ Kullanılan Teknolojiler

| Bileşen | Kullanılan Araç | Görevi |
|---|---|---|
| **Programlama Dili** | C# 14 / .NET 10 LTS | Uygulamanın tüm iş mantığı, ayrıştırıcıları ve arayüzü |
| **Kullanıcı Arayüzü** | Windows Forms (WinExe) | Bağımsız masaüstü grafik arayüzü |
| **Uygulama Altyapısı** | .NET Generic Host & Dependency Injection | Servis, konfigürasyon ve log yönetimi |
| **Ağ İstemcisi** | `HttpClient` (Typed Client) | Kariyer Kapısı API ve Telegram Bot API erişimi |
| **Veritabanı** | SQLite (`Microsoft.Data.Sqlite`) | İlan, kadro, değerlendirme ve outbox kayıtları |
| **Metin & HTML İşleme** | `HtmlAgilityPack` + Regex | HTML/BBCode temizleme, tablo ayrıştırma, kural tabanlı şart çıkarma |
| **Birim Testleri** | xUnit + .NET Test SDK | Puan, tecrübe, mezuniyet ve tekilleştirme testleri |
| **Ayarlar & Profil** | JSON (`appsettings.json`, `profile.json`) | Kullanıcı profili ve bot yapılandırması |

---

## 📁 Proje Dizin Yapısı

```text
kariyertakip/
├── Common/
│   └── AppPaths.cs              # Belirlenmiş dosya ve veri yolları yönetimi
├── Forms/
│   └── MainForm.cs              # Windows Forms modern masaüstü grafik arayüzü
├── Models/
│   ├── AppConfig.cs             # Uygulama ve Telegram ayar modelleri
│   ├── DbEntities.cs            # SQLite veritabanı tablo modelleri
│   ├── EligibilityModels.cs     # 3 durumlu uygunluk değerlendirme modelleri
│   ├── KariyerKapisiModels.cs   # Resmî API istek ve yanıt modelleri
│   └── ProfileOptions.cs        # Kullanıcı profil modelleri (Bölüm, KPSS, vb.)
├── Services/
│   ├── CareerGateClient.cs      # Kariyer Kapısı API istemcisi (Retry & ApiResult destekli)
│   ├── ChangeDetector.cs        # İlan ve son başvuru tarihi değişiklik tespiti & hash
│   ├── DocumentReader.cs        # HTML tablo ve metin temizleyici
│   ├── EligibilityEvaluator.cs  # Şartları profille karşılaştıran karar motoru
│   ├── GuiLogger.cs             # Logları canlı arayüze aktaran sağlayıcı
│   ├── NotificationDispatcher.cs# Outbox bildirim kuyruğunu işleyen servis
│   ├── RequirementExtractor.cs  # Metinlerden puan, yıl, tecrübe çıkaran motor
│   ├── ScanCoordinator.cs       # Tarama, değerlendirme ve eşzamanlılık orkestratörü
│   └── TelegramNotifier.cs      # Telegram mesaj formatlayıcı ve gönderici
├── Storage/
│   ├── AnnouncementRepository.cs# SQLite CRUD, indeks ve sorgu işlemleri
│   └── IAnnouncementRepository.cs
├── tests/
│   └── KariyerTakip.Tests/      # xUnit birim test projesi
├── appsettings.json             # Telegram ve sistem ayarları
├── profile.json                 # Kullanıcı profili ve tercihleri
├── KariyerTakip.csproj          # .NET 10 proje ve paket yapılandırması
├── KariyerTakip.slnx            # .NET 10 çözüm dosyası
├── Program.cs                   # Uygulama başlangıç noktası (GUI / --scan-once)
├── Kurulum.bat                  # Tek tıkla derleme, test ve başlatma betiği
├── Kaldir.bat                   # Tek tıkla geçici dosya ve veritabanı temizleme
└── README.md                    # Proje dokümantasyonu
```

---

## 🚀 Kurulum ve Çalıştırma

### Gereksinimler
- **Windows 10 / 11** işletim sistemi
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** (LTS)

### Yöntem 1: Tek Tıkla Kurulum ve Başlatma (Önerilen)
1. Proje ana dizinindeki **`Kurulum.bat`** dosyasını çift tıklayarak çalıştırın.
2. Betik sırasıyla:
   - Sisteminizde `.NET 10 SDK` kurulu olduğunu doğrular.
   - Gerekli NuGet paketlerini yükler.
   - Projeyi Release modunda derler.
   - Birim testlerini (`dotnet test`) koşturup doğrular.
   - Masaüstü grafik arayüzünü (`KariyerTakip.exe`) başlatır.

### Yöntem 2: Komut Satırından / Visual Studio ile Çalıştırma
```powershell
# 1. Proje klasörüne gelin
cd c:\Users\ck\Desktop\anaklasor\projelerim\projelerim\kariyertakip

# 2. Testleri çalıştırın
dotnet test

# 3. Grafik arayüzü başlatın
dotnet run

# 4. (Alternatif) Ekransız / Görev Zamanlayıcı modunda tek seferlik tarama
dotnet run -- --scan-once
```

---

## 📖 Kullanım Kılavuzu

### 1. 📋 İlanlar & Pozisyonlar Sekmesi
- **Şimdi Tara:** Üst bardaki **"⚡ Şimdi Tara"** butonuna basarak canlı Kariyer Kapısı taramasını anında başlatabilirsiniz. Devam eden bir taramayı **"⛔ İptal"** butonuyla güvenle durdurabilirsiniz.
- **Filtreleme & Arama:** `Tüm İlanlar`, `✅ Uygun İlanlar`, `⚠️ Kontrol Gerekli` filtreleri ile sadece ilginizi çeken kadroları görüntüleyebilirsiniz.
- **Detay Kartı:** İlan seçildiğinde; kadro adı, unvan, toplam kontenjan, başvuru tarihleri ve **sistemin neden uygun bulduğuna veya hangi şartı kontrol etmeniz gerektiğine dair ayrıntılı gerekçe** sağ panelde listelenir.
- **Hızlı Butonlar:**
  - 🌐 **Kariyer Kapısı İlanı:** Resmî ilan sayfasını varsayılan tarayıcınızda açar.
  - 📝 **e-Devlet Başvuru:** İlgili kurumun e-Devlet başvuru kapısına yönlendirir.
  - 📲 **Telegram'a At:** Seçili ilanın güncel durumunu anında Telegram sohbetinize gönderir.

### 2. 👤 Profilim Sekmesi
Profilinizi arayüz üzerinden dilediğiniz gibi güncelleyebilirsiniz:
- **Bölüm Adı & Öğrenim Düzeyi:** `Bilgisayar Programcılığı` / `Ön Lisans` / `Lisans`
- **Mezuniyet Durumu:** `Mezun` / `Öğrenci`
- **KPSS Puan Türü, Puanı ve Yılı:** Örneğin `P93`, `75.00`, `2024`
- **Doğum Tarihi:** Yaş sınırı hesaplamaları için
- **Askerlik Durumu:** `Muaf / Yapıldı`, `Tecilli`, `Yapılmadı`
- **Mesleki Tecrübe:** Ay cinsinden süre, çalışma alanı ve resmi olarak belgelenebilir olup olmadığı
- **Şehir Tercihleri:** Belirli iller veya tüm Türkiye için boş bırakma seçeneği
- **Ehliyet ve Sertifikalar:** Ehliyet sınıfları ve sahip olduğunuz belgeler
- **"💾 Profili Kaydet & İlanları Yeniden Değerlendir"** butonuna bastığınızda ayarlar kaydedilir ve veritabanındaki tüm aktif ilanlar yeni kriterlerinize göre anında yerel olarak yeniden değerlendirilir.

### 3. ⚙️ Telegram & Sistem Ayarları
Telegram bildirimlerini aktif etmek için:
1. Telegram'da **@BotFather** botuna gidip `/newbot` komutuyla yeni bir bot oluşturun ve verilen **API Token** değerini kopyalayın.
2. Oluşturduğunuz bot ile sohbet başlatıp `/start` yazın.
3. Chat ID değerinizi öğrenip arayüzdeki ilgili alanlara girin:
   - **Telegram Bot Token:** `123456789:ABCDefgh...`
   - **Telegram Chat ID:** `987654321`
   - **Telegram Bildirimlerini Etkinleştir:** İşaretleyin.
4. **"📨 Test Bildirimi Gönder"** butonuna basarak bağlantıyı test edin.
5. **"💾 Ayarları Kaydet"** butonuna basarak yapılandırmayı tamamlayın.

---

## 🗑️ Tek Tıkla Kaldırma ve Temizleme

Uygulamanın derleme çıktılarını, geçici dosyalarını ve yerel SQLite veritabanını sıfırlamak için:
- Proje ana dizinindeki **`Kaldir.bat`** dosyasını çift tıklayıp onaylamanız (E tuşuna basmanız) yeterlidir.

---

## ⚖️ Lisans ve Yasal Uyarı

Bu proje yalnızca kişisel kamu işe alım ilanlarını takip etmek amacıyla geliştirilmiştir. Veriler T.C. Cumhurbaşkanlığı İnsan Kaynakları Ofisi Kariyer Kapısı platformunun kamuya açık resmî sayfalarından okunmaktadır. T.C. kimlik numarası veya e-Devlet şifresi gibi kişisel kimlik doğrulama bilgileri kesinlikle toplanmaz veya saklanmaz.