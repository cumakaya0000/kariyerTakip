# Güvenlik

Güvenlik düzeltmeleri güncel `main` ve en yeni sürüm için hazırlanır.

Bir açık bulursanız GitHub deposunun **Security → Advisories → Report a vulnerability** bölümünü kullanın. Özel bildirim özelliği kapalıysa önce kişisel veri veya açık ayrıntısı içermeyen bir issue ile özel iletişim yolu isteyin. Token, şifreli token dosyası, kişisel profil, veritabanı veya kullanıcı günlüklerini herkese açık issue'lara eklemeyin.

DPAPI dosyası mevcut Windows hesabına bağlıdır; aynı hesabın ele geçirilmesine karşı ek bir güvenlik sınırı sağlamaz. Zamanlanmış görev uygulamayı ayarları kaydeden kullanıcı hesabıyla çalıştırmalıdır.

Yayın paketlerinde SHA256 değerleri sağlanır. Bunlar bütünlük kontrolüdür; yayıncının kimliğini kanıtlayan kod imzasının yerine geçmez. Sertifika yapılandırılmadığında exe imzasızdır. Windows güvenlik uyarılarını aşmak için korumayı kapatmayın.

CI, doğrudan ve geçişli NuGet bağımlılıklarında bilinen açıkları kontrol eder. Dependabot NuGet ve GitHub Actions güncellemelerini haftalık olarak önerir.
