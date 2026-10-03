# 💼 KariyerTakip — Kişisel Kamu İlan ve Kadro Asistanı

**KariyerTakip**, T.C. Cumhurbaşkanlığı İnsan Kaynakları Ofisi **Kariyer Kapısı Kamu İşe Alım Portalı** üzerindeki aktif ilanları otomatik olarak tarayan, ilan metinleri ile kadro tablolarını analiz eden ve kullanıcının profiline göre (bölüm, ön lisans/lisans, KPSS P93 puanı, tecrübe, şehir vb.) uygun kadroları tespit edip masaüstü arayüzünde listeleyen ve Telegram üzerinden anlık bildiren **C# / .NET 10 LTS** tabanlı bir masaüstü uygulamasıdır.

---

## 🌟 Öne Çıkan Özellikler

- 🖥️ **Modern Windows Grafik Arayüzü (WinForms):** Arka planda siyah CMD/Terminal penceresi açılmaz; temiz ve modern bir masaüstü arayüzü sunar.
- 🔍 **Resmî Kariyer Kapısı API Entegrasyonu:** Giriş yapma zorunluluğu olmadan kamuya açık resmî API uç noktalarından anlık ve tam veri çeker.
- 🎯 **Akıllı Kadro Uygunluk Motoru (Tri-State Decision Engine):** Yalnızca ilan başlığına bakmaz; ilanın içindeki her bir kadro satırını tek tek inceler.
  - ✅ **Şartlara Uygun:** Tüm zorunlu koşullar (Bölüm, Öğrenim Düzeyi, KPSS Taban Puanı, Sınav Yılı) tam karşılanıyor.
  - ⚠️ **Kontrol Gerekli:** Kadro bilişim ile ilgili ancak ek belge, tecrübe veya özel sertifika kontrolü gerektiriyor.
  - ❌ **Uygun Değil:** En az bir zorunlu koşul (örneğin sadece 4 yıllık lisans istemesi veya yetersiz puan) açıkça sağlanmıyor.
- 🗄️ **SQLite Yerel Veritabanı:** İlanlar, kadrolar, değerlendirme geçmişi ve bildirim kuyruğu `kariyertakip.db` içerisinde saklanır. Tekrar eden veya değişmeyen ilanlar için mükerrer bildirim gönderilmez.
- 📲 **Telegram Bot Entegrasyonu:** Şartlarına uyan veya güncellenen ilanlar zengin HTML şablonuyla (kurum, birim, kadro, kontenjan, şehir, KPSS şartı, başvuru tarihleri ve doğrudan linkler) Telegram sohbetine iletilir.
- 🌐 **Tek Tıkla Resmî Sayfalara Erişim:** Seçilen ilanın Kariyer Kapısı detay sayfasına veya doğrudan e-Devlet başvuru ekranına tek tıkla tarayıcıdan ulaşılabilir.
- 👤 **Arayüzden Profil Yönetimi:** `profile.json` dosyasını elle açmadan; bölümünüzü, KPSS puanınızı, tecrübenizi ve şehir tercihlerinizi doğrudan arayüzden güncelleyip anında yeniden değerlendirme yapabilirsiniz.

---

## 🛠️ Kullanılan Teknolojiler

| Bileşen | Kullanılan Teknoloji / Kütüphane | Görevi |
|---|---|---|
| **Programlama Dili** | C# 14 / .NET 10 LTS | Uygulamanın tüm iş mantığı ve arayüzü |
| **Kullanıcı Arayüzü** | Windows Forms (WinExe) | Pencereli, bağımsız masaüstü grafik arayüzü |
| **Uygulama Altyapısı** | .NET Generic Host & Dependency Injection | Servis, konfigürasyon ve log yönetimi |
| **Ağ İstemcisi** | `HttpClient` (Typed Client) | Kariyer Kapısı API ve Telegram Bot API erişimi |
| **Veritabanı** | SQLite (`Microsoft.Data.Sqlite`) | İlan, kadro, değerlendirme ve outbox kayıtları |
| **Metin & HTML İşleme** | `HtmlAgilityPack` + Regex | BBCode ve HTML etiketlerini temizleme, şart ayrıştırma |
| **Ayarlar** | JSON (`appsettings.json`, `profile.json`) | Kullanıcı profili ve bot yapılandırması |

---

## 📁 Proje Dizin Yapısı

```text
kariyertakip/
├── Forms/
│   └── MainForm.cs              # Modern Windows Forms grafik arayüzü
├── Models/
│   ├── AppConfig.cs             # Uygulama ve Telegram ayar modelleri
│   ├── DbEntities.cs            # SQLite veritabanı tablo modelleri
│   ├── EligibilityModels.cs     # 3 durumlu uygunluk değerlendirme modelleri
│   ├── KariyerKapisiModels.cs   # Resmî API istek ve yanıt modelleri
│   └── ProfileOptions.cs        # Kullanıcı profil modelleri (Bölüm, KPSS, vb.)
├── Services/
│   ├── CareerGateClient.cs      # Kariyer Kapısı API istemcisi
│   ├── ChangeDetector.cs        # İlan ve son başvuru tarihi değişiklik tespiti
│   ├── DocumentReader.cs        # BBCode / HTML temizleyici ve metin ayrıştırıcı
│   ├── EligibilityEvaluator.cs  # Şartları profille karşılaştıran karar motoru
│   ├── GuiLogger.cs             # Logları canlı arayüze aktaran sağlayıcı
│   ├── NotificationDispatcher.cs# Outbox bildirim kuyruğunu işleyen servis
│   ├── RequirementExtractor.cs  # Metinlerden puan, yıl, tecrübe çıkaran motor
│   ├── ScanCoordinator.cs       # Tarama ve değerlendirme orkestratörü
│   └── TelegramNotifier.cs      # Telegram mesaj formatlayıcı ve gönderici
├── Storage/
│   ├── AnnouncementRepository.cs# SQLite CRUD ve sorgu işlemleri
│   └── IAnnouncementRepository.cs
├── appsettings.json             # Telegram ve sistem ayarları
├── profile.json                 # Kullanıcı profili ve tercihleri
├── KariyerTakip.csproj          # .NET 10 proje ve paket yapılandırması
├── Program.cs                   # Uygulama başlangıç noktası (STA WinExe)
├── Kurulum.bat                  # Tek tıkla derleme ve başlatma betiği
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
   - .NET SDK kontrolünü yapar.
   - Gerekli NuGet paketlerini yükler.
   - Projeyi Release modunda derler.
   - Grafik arayüzü (`KariyerTakip.exe`) otomatik olarak başlatır.

### Yöntem 2: Komut Satırından / Visual Studio ile Çalıştırma
```powershell
# 1. Proje klasörüne gelin
cd c:\Users\ck\Desktop\anaklasor\projelerim\projelerim\kariyertakip

# 2. Paketleri geri yükleyin ve derleyin
dotnet build

# 3. Uygulamayı başlatın
dotnet run
```

---

## 📖 Kullanım Kılavuzu

### 1. 📋 İlanlar & Pozisyonlar Sekmesi
- **Şimdi Tara:** Üst bardaki **"⚡ Şimdi Tara"** butonuna basarak canlı Kariyer Kapısı taramasını anında başlatabilirsiniz.
- **Filtreleme:** `Tüm İlanlar`, `✅ Uygun İlanlar`, `⚠️ Kontrol Gerekli` filtreleri ile sadece ilginizi çeken kadroları görüntüleyebilirsiniz.
- **Arama:** Kurum adına veya ilan başlığına göre anlık filtreleme yapabilirsiniz.
- **Detay Kartı:** İlan seçildiğinde; kadro adı, unvan, toplam kontenjan, başvuru başlangıç/bitiş tarihleri ve **sistemin neden uygun bulduğuna dair detaylı gerekçe** sağ panelde listelenir.
- **Hızlı Butonlar:**
  - 🌐 **Kariyer Kapısı İlanı:** Resmî ilan sayfasını açar.
  - 📝 **e-Devlet Başvuru:** İlgili kurumun e-Devlet başvuru kapısına yönlendirir.
  - 📲 **Telegram'a At:** Seçili ilanı doğrudan Telegram sohbetinize gönderir.

### 2. 👤 Profilim Sekmesi (`profile.json`)
Profilinizi arayüz üzerinden dilediğiniz gibi güncelleyebilirsiniz:
- **Bölüm:** `Bilgisayar Programcılığı` (veya istediğiniz ön lisans/lisans bölümü)
- **Öğrenim Düzeyi:** `Ön Lisans` / `Lisans` / `Ortaöğretim`
- **KPSS Puan Türü ve Puanı:** Örneğin `P93` ve `75.0`
- **Sınav Yılı:** Örneğin `2024`
- **Mesleki Tecrübe:** Yıl ve çalışma alanı
- **Şehir Tercihleri:** Belirli iller veya tüm Türkiye için boş bırakma seçeneği
- **"💾 Profili Kaydet & İlanları Yeniden Değerlendir"** butonuna bastığınızda ayarlar kaydedilir ve tüm aktif ilanlar yeni kriterlerinize göre anında yeniden taranır.

### 3. ⚙️ Telegram & Sistem Ayarları (`appsettings.json`)
Telegram bildirimlerini aktif etmek için:
1. Telegram'da **@BotFather** botuna gidip `/newbot` komutuyla yeni bir bot oluşturun ve verilen **API Token** değerini kopyalayın.
2. Oluşturduğunuz bot ile sohbet başlatıp `/start` yazın.
3. Chat ID değerinizi öğrenip arayüzdeki ilgili alanlara girin:
   - **Telegram Bot Token:** `123456789:ABCDefgh...`
   - **Telegram Chat ID:** `987654321`
   - **Telegram Bildirimlerini Etkinleştir:** İşaretleyin.
4. **"📨 Test Bildirimi Gönder"** butonuna basarak bağlantıyı test edin.
5. **"💾 Ayarları Kaydet"** butonuna basarak yapılandırmayı tamamlayın.

### 4. 📜 Canlı İşlem Günlüğü
Arka planda yapılan tüm HTTP API çağrıları, tespit edilen kadro sayıları ve uygunluk analizleri bu ekranda renkli olarak canlı akar.

---

## 🗑️ Tek Tıkla Kaldırma ve Temizleme

Uygulamanın derleme çıktılarını, geçici dosyalarını ve yerel SQLite veritabanını sıfırlamak için:
- Proje ana dizinindeki **`Kaldir.bat`** dosyasını çift tıklayıp onaylamanız (E tuşuna basmanız) yeterlidir.

---

## ⚖️ Lisans ve Yasal Uyarı

Bu proje yalnızca kişisel kamu işe alım ilanlarını takip etmek amacıyla geliştirilmiştir. Veriler T.C. Cumhurbaşkanlığı İnsan Kaynakları Ofisi Kariyer Kapısı platformunun kamuya açık resmî sayfalarından okunmaktadır. T.C. kimlik numarası veya e-Devlet şifresi gibi kişisel kimlik doğrulama bilgileri kesinlikle toplanmaz veya saklanmaz.