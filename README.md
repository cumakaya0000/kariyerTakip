# KariyerTakip

Windows için Kariyer Kapısı ilanlarını tarayan, kadro şartlarını profille karşılaştıran ve başvuruları takip eden masaüstü uygulaması. C# / .NET 10, WinForms ve SQLite kullanır.

## Özellikler

- İlan ve kadro taraması; uygun / kontrol gerekli / uygun değil sonuçları ve ayrı gerekçe satırları.
- Kutucuklarla çoklu ilan seçimi ve seçilen ilanları tarayıcıda açma.
- Başvuru takibi: **başvuracağım / başvurdum / geçtim** ve yerel notlar.
- Birden fazla profil ve her profilde birden fazla KPSS puan türü/yılı.
- Öğrenim, KPSS, tecrübe, yaş, askerlik, sertifika, ehliyet, şehir ve çalışma türü değerlendirmesi.
- Profil değişince önbellekteki kadroları yerel olarak yeniden değerlendirme.
- Telegram bildirim kuyruğu, parça ilerlemesini saklama ve son başvuru hatırlatmaları.
- Sistem tepsisine küçültme ve isteğe bağlı Windows ile başlatma.
- Ekransız tarama, UTC dosya günlükleri ve Windows x64 tek dosya yayın paketi.

## Ekran görüntüleri

Görseller sentetik test verileriyle oluşturulmuştur; kişisel ayar veya gerçek token içermez.

![İlanlar, çoklu seçim ve kadro gerekçeleri](docs/images/ilanlar.png)
![Çoklu profil ve KPSS düzenleyicisi](docs/images/profiller.png)
![Telegram ve sistem ayarları](docs/images/ayarlar.png)

## Çalıştırma

### Hazır Windows paketi

[GitHub Releases](https://github.com/cumakaya0000/kariyerTakip/releases) üzerinde bir sürüm yayınlandığında `KariyerTakip-win-x64.zip` dosyasını indirip açın ve `KariyerTakip.exe` dosyasını çalıştırın. Paket kendi .NET çalışma zamanını içerir; kullanıcıda SDK kurulumu gerekmez. Henüz sürüm etiketi oluşturulmamışsa aşağıdaki kaynak kod yöntemini kullanın.

### Kaynak koddan

Windows 10/11 ve .NET 10 SDK gerekir. `Kurulum.bat` bağımlılıkları yükler, çözümü derler, testleri çalıştırır ve uygulamayı açar. Herhangi bir adım başarısız olursa sonraki adımlara geçmez; test başarısızlığında uygulama açılmaz.

```powershell
git clone https://github.com/cumakaya0000/kariyerTakip.git
cd kariyerTakip
dotnet restore KariyerTakip.slnx
dotnet build KariyerTakip.slnx -c Release --no-restore
dotnet test KariyerTakip.slnx -c Release --no-build
dotnet run --project KariyerTakip.csproj
```

Test projesi çözüme dahildir. Çözüm üzerinden test komutu gerçekten testleri çalıştırır.

## Profil ve başvuru takibi

**Profilim** sekmesinde mevcut profili seçin veya yeni profil ekleyin. KPSS tablosunun boş satırına P93/P3/P94, sınav yılı ve puanı girerek birden fazla puan saklayabilirsiniz. Satır seçip Delete ile silebilirsiniz. Aynı puan türü/yıl çifti yinelenemez. **Kaydet** düğmesi aktif profili ve diğer profilleri saklar, ilanları yeniden değerlendirir.

Doğum tarihi kutusunu işaretlemezseniz doğum tarihiniz bilinmiyor kabul edilir. Yaş şartı bulunan kadrolar kontrol gerektirebilir. `Experience.TotalMonths` ay cinsindedir; bilinmiyor/belgeli seçenekleri ayrı saklanır. Çalışma türlerini boş bırakırsanız tür filtresi uygulanmaz. Sertifika adlarını virgülle ayırın. Belirsiz veya metinden çıkarılamayan koşullar kontrol gerektirir; sonuçlar resmî başvuru kararının yerine geçmez.

İlan ayrıntılarındaki **Başvuru takibi ve notlar** sekmesinden başvuru durumunu ve kişisel notları kaydedin. Sonraki tarama bu kayıtları silmez. Başvurulmuş veya geçilmiş ilanlara son tarih hatırlatması gönderilmez.

## Ayarlar ve veri klasörü

Git deposunda yalnızca `appsettings.example.json` ve `profile.example.json` bulunur. İlk çalıştırmada gerçek dosyalar veri klasörüne oluşturulur. Yerel `appsettings.json`, `profile.json`, `profiles.json`, `telegram.secret`, SQLite ve günlük dosyaları Git'e dahil edilmez.

Varsayılan veri klasörü `%LOCALAPPDATA%\KariyerTakip` dizinidir. Exe yanında `portable.flag` veya eski bir `appsettings.json` / `profile.json` bulunursa o klasör kullanılır. `KARIYERTAKIP_DATA_DIR` ortam değişkeniyle başka bir tam yol seçebilirsiniz. Veri klasörünün yazılabilir olması gerekir.

- `appsettings.json`: uygulama ayarları ve Logging bölümü. Ayar kaydı diğer üst düzey JSON bölümlerini korur.
- `profile.json`: aktif profilin uyumluluk kopyası.
- `profiles.json`: adlandırılmış profiller ve aktif profil; mevcutsa esas kaynak budur.
- `telegram.secret`: Windows DPAPI ile mevcut Windows hesabına bağlı şifreli token.
- `kariyertakip.db`: ilanlar, kadrolar, değerlendirmeler, başvuru notları ve kuyruk.
- `logs/kariyertakip-YYYY-MM-DD.log`: UTC zaman damgalı günlükler.

Eski profillerde `Experience.Years` aya çevrilir; `OtherConditions.MilitaryStatus` üst seviyeye taşınır. `MaxAge` bir kişinin doğum tarihi yerine kullanılamayacağı için doğum tarihi uydurulmaz. Eski SQLite şeması kayıtlar korunarak otomatik güncellenir; veritabanını silmeyin.

## Telegram

BotFather ile bot oluşturup token ve sohbet ID'sini **Telegram & Sistem** sekmesine girin. Token maskelenir ve kaydedildiğinde DPAPI ile korunur. Eski düz metin token ilk açılışta şifreli dosyaya taşınır; JSON'dan çıkarılır. Şifreli dosya başka Windows hesabında açılamaz; o hesapta token'ı tekrar girin.

Ortam değişkenleri de kullanılabilir; bunlar yerel ayarlardan önceliklidir:

```powershell
$env:KARIYERTAKIP_KariyerTakip__Telegram__BotToken = 'BOT_TOKEN'
$env:KARIYERTAKIP_KariyerTakip__Telegram__ChatId = 'CHAT_ID'
$env:KARIYERTAKIP_KariyerTakip__Telegram__Enabled = 'true'
```

**Test bildirimi** uygulamanın DI istemcisini kullanır. Token içeren HTTP istemci günlükleri kapatılır; uygulama hata mesajları token/URL içermez.

[Telegram Bot API](https://core.telegram.org/bots/api#sendmessage) sınırlarına göre mesajlar bölünür. Uzun mesajlarda düz metne geçilerek HTML etiketleri/karakter kaçışları bozulmadan bölünür; bağlantı adresleri korunur. Başarılı her parça veritabanına işlenir; sonraki deneme kalan parçadan başlar. `429` yanıtındaki [retry_after](https://core.telegram.org/bots/api#responseparameters) tüm kuyruğun bekleme süresine uygulanır. Kalıcı hatalar otomatik tekrar edilmez.

Ağ kesintisinde Telegram isteği kabul etmiş ancak uygulama yanıtı alamamış olabilir. API idempotency anahtarı sunmadığından bu belirsiz durumda tam olarak bir kez teslim garantisi verilemez.

## Tarama, zaman ve hatırlatmalar

İlan/kadro tarihleri ve veritabanındaki zaman damgaları UTC tutulur; arayüz ve Telegram Türkiye saatini gösterir. Saat dilimi taşımayan portal tarihleri Türkiye saati kabul edilir. Doğum tarihi takvim tarihidir.

`Scan.MaxConcurrency` 1–8 arasında eşzamanlı ilan sayısını, `RequestDelayMs` tüm API istekleri arasındaki asgari aralığı belirler. Varsayılanlar 2 ve 300 ms. 429/5xx ve ağ hatalarında üstel bekleme, jitter ve HTTP `Retry-After` kullanılır. İptal istekleri yeniden denenmez.

Kadro anahtarları başlık/unvan kimliğinden üretilir; API sıralaması kimliği değiştirmez. Eski eşleşen kadro anahtarları korunur. Aynı kimlikli kadrolar ayrıştırılır; artık dönmeyen kadrolar geçmişten silinmeden pasif yapılır. API başarısız olduğunda önbellek korunur.

`Scan.ReminderDays` son başvuruya kaç gün kala hatırlatma gönderileceğini belirler (varsayılan 3; 0 kapalı). Hatırlatmalar **tarama çalıştığında** kontrol edilir ve ilan/son tarih/eşik için tekilleştirilir. Düzenli kontrol için Görev Zamanlayıcı kullanın; uygulamanın açık olması tek başına periyodik tarama başlatmaz.

```powershell
# Kaynak koddan
dotnet run --project KariyerTakip.csproj -- --scan-once
# Yayın paketinden
.\KariyerTakip.exe --scan-once
```

| Çıkış kodu | Sonuç |
|---|---|
| 0 | Başarılı |
| 1 | Başarısız |
| 2 | Kısmi; bazı ilanlar okunamadı |
| 3 | İptal edildi |
| 4 | Başka bir tarama çalışıyor |

Tarama işi asenkron yürür; WinForms giriş noktası STA gereksinimi için senkron tutulur. `scripts/ZamanlanmisTarama.bat` yayın paketinde exe ile aynı klasöre konur. Görev Zamanlayıcı'da saatlik tetikleyici ve aynı Windows hesabıyla bu betiği çalıştırın. Bir önceki görev sürüyorsa yeni örnek başlatmama seçeneğini seçin.

## CI ve yayın

- `.github/workflows/ci.yml`: `main` push ve PR'larda `windows-latest` ile Release derlemesi ve çözüm testleri; TRX raporu artifact olarak saklanır.
- `.github/workflows/release.yml`: elle çalıştırıldığında test edilmiş Windows zip artifact'i oluşturur. `v*` etiketi gönderildiğinde aynı paket GitHub Releases'e yüklenir.
- Yerel paket: normal PowerShell oturumunda `& ./scripts/Publish.ps1` çalıştırın. Betik çalıştırma ilkeniz izin vermiyorsa CI iş akışını kullanın.
- Paket yalnızca exe, örnek ayarlar, README ve Görev Zamanlayıcı betiğinden oluşur; kişisel veri içermez.

## Sık karşılaşılan sorunlar

| Sorun | Çözüm |
|---|---|
| SQLite `ON CONFLICT` hatası | Güncel sürümü açın; benzersiz indeks otomatik eklenir. Veritabanını silmeyin. |
| Exe kopyalanamıyor / derleme kilidi | Açık KariyerTakip'i ve tepsi simgesinden çalışan örneği kapatın, yeniden derleyin. |
| Windows Uygulama Denetimi exe/test DLL'sini engelliyor | Windows güvenlik/kurum ilkesini kontrol edin. Korumayı kapatmayın; politika yöneticisi veya güvenilir bir yayın paketiyle ilerleyin. |
| Telegram 401/403 | Token, sohbet ID, botla `/start` ve sohbet izinlerini kontrol edin. |
| Telegram 429 | Kuyruk belirtilen süreyi bekler; daha sonraki taramada devam eder. |
| API/JSON yanıtı değişti | Dosya günlüğünü kontrol edip örnek yanıtla sorun açın. |
| İlanlar kontrol gerekli görünüyor | Aktif profili kaydedip yeniden değerlendirin; bilinmeyen şartlar için kılavuzu kontrol edin. |
| Şifreli token başka hesapta okunamıyor | Token'ı o Windows hesabında yeniden kaydedin. |

## Geliştirme ve katkı

Servisler `Services/`, depolama `Storage/`, modeller `Models/`, UI bileşenleri `Forms/` altındadır. Profil/ayar/bildirim kaydı servislerde; sekmeler ayrı UserControl bileşenlerindedir. `MainForm` seçim, tarama, tema ve bileşen koordinasyonunu yapar.

Testler ayrıştırma, uygunluk, şema migrasyonu, sıra bağımsız kadro kimliği, iptal, sahte HTTP yanıtları, mesaj bölme/kodlama, kısmi gönderim, hız sınırı, yollar, ayar koruma, DPAPI, loglar, profiller, başvuru takibi ve hatırlatmaları kapsar. DPAPI testlerini normal Windows kullanıcı hesabında çalıştırın. Gerçek kurumsal ilanlardan kısa örneklerin kaynakları test dosyasında belirtilmiştir.

Katkı için bir dal açın, ilgili regresyon testini ekleyin ve `dotnet test KariyerTakip.slnx` çalıştırın. PR'a davranış değişikliğini ve doğrulamayı yazın. Bot token, kişisel profil, kimlik bilgisi veya veritabanı eklemeyin. Sürüm değişiklikleri [CHANGELOG.md](CHANGELOG.md) dosyasındadır.

Kariyer Kapısı'nın kamuya açık portal uç noktaları kullanılır; bu proje için yayımlanmış/sözleşmeli bir API garantisi yoktur. Portal değişirse entegrasyon güncellenmelidir. Uygulama e-Devlet şifresi veya T.C. kimlik numarası istemez; başvuru resmî sitede yapılır.