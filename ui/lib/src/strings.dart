/// Every word the interface shows, in one file.
///
/// Turkish, for the same reason the Telegram messages are Turkish: the reader is
/// the team (spec Bolum 1). The identifiers stay English like everything else in
/// the codebase, so this file reads as a mapping and not as a second language
/// leaking into the code.
///
/// One place rather than scattered literals, for the same reason
/// `config/messages.json` is one place - a wording is changed by opening one
/// file, and nobody has to grep for a sentence.
library;

abstract final class Strings {
  static const String appTitle = 'Procurement Radar';

  // --- navigation ---
  static const String notices = 'İlanlar';
  static const String status = 'Durum';
  static const String system = 'Sistem';
  static const String settings = 'Ayarlar';

  // --- system (yalnızca masaüstü) ---
  static const String partDatabase = 'PostgreSQL · Docker';
  static const String partWorker = 'Worker';
  static const String partApi = 'API';

  static const String start = 'Başlat';
  static const String stop = 'Durdur';
  static const String startsAtLogon = 'Oturum açılınca başlasın';
  static const String dockerAutostart =
      'Docker Desktop girişte başlamalı — Settings → General içinden.';
  static const String projectFolder = 'Proje klasörü';
  static const String projectFolderMissing =
      'Proje klasörü bulunamadı. docker-compose.yml ve ProcurementRadar.sln '
      'dosyalarının olduğu klasörü yazın.';
  static const String projectFolderNotValid =
      'Burada docker-compose.yml ve ProcurementRadar.sln yok.';
  static const String commands = 'Komutlar';
  static const String logTitle = 'Log';
  static const String problemsOnly = 'Yalnız WARN ve ERROR';
  static const String noLog = 'Okunacak log dosyası yok.';
  static const String running = 'çalışıyor…';
  static const String confirmTitle = 'Emin misiniz?';
  static const String confirmWrites =
      'Bu komut veritabanına yazar. Devam edilsin mi?';
  static const String cancel = 'Vazgeç';
  static const String proceed = 'Devam';
  static const String close = 'Kapat';
  static const String systemOnDesktopOnly =
      'Bu bölüm yalnızca masaüstü sürümünde çalışır. Tarayıcı süreç başlatamaz, '
      'Docker\'a veya zamanlanmış görevlere erişemez.';
  static const String tokenFromEnvFound =
      '.env dosyasından okundu';
  static const String readFromEnv = '.env\'den al';

  // --- notice list ---
  static const String openNotices = 'Açık ilanlar';
  static const String searchHint = 'Başlık veya idare ara';
  static const String noNotices = 'Gösterilecek ilan yok.';
  static const String noSearchResults = 'Bu aramaya uyan ilan yok.';
  static const String scoreLabel = 'Puan';
  static const String refresh = 'Yenile';
  static const String clearSearch = 'Aramayı temizle';

  /// Shown where a deadline should be. Not an error: spec Bolum 7 says a notice
  /// whose deadline could not be read is still recorded, and the warning belongs
  /// on the screen rather than in a log nobody reads.
  static const String deadlineUnknown = 'Son teklif tarihi okunamadı';
  static const String deadlinePassed = 'Süresi geçti';
  static const String deadlinePrefix = 'Son teklif';

  // --- notice detail ---
  static const String noticeDetail = 'İlan';
  static const String authority = 'İdare';
  static const String iknLabel = 'İKN';
  static const String rationale = 'Neden seçildi';
  static const String announcement = 'İlan metni';
  static const String noAnnouncement = 'Bu ilan için indirilmiş metin yok.';
  static const String openSource = 'İlanı kaynağında aç';
  static const String couldNotOpenSource = 'Adres açılamadı.';
  static const String noticeNotFound = 'İlan bulunamadı.';

  // --- status buttons ---
  static const String verdict = 'Kararınız';
  static const String statusNew = 'Yeni';
  static const String statusReviewed = 'İncelendi';
  static const String statusBidSubmitted = 'Teklif verildi';
  static const String statusMissed = 'Kaçırıldı';
  static const String statusNotRelevant = 'İlgilenmiyoruz';
  static const String statusSaved = 'Kaydedildi.';

  // --- system status ---
  static const String activeSources = 'Aktif kaynak';
  static const String failingSources = 'Hata veren kaynak';
  static const String totalNotices = 'Toplam ilan';
  static const String noticesToday = 'Son 24 saatte';
  static const String announced = 'Bildirilen';
  static const String lastFetch = 'Son tarama';
  static const String never = 'hiç';

  // --- settings ---
  static const String connection = 'Bağlantı';
  static const String apiAddress = 'API adresi';
  static const String apiAddressHelp =
      'Toplamayı yapan makine. Telefondan açarken localhost değil, o makinenin '
      'ev ağındaki adresi yazılır.';
  static const String apiToken = 'API anahtarı';
  static const String apiTokenHelp =
      '.env dosyasındaki PR_API_TOKEN. Bir kullanıcı hesabı değil; ev ağındaki '
      'başka bir cihazın yanlışlıkla ayar değiştirmesine karşı.';
  static const String apiTokenStorageWarning =
      'Anahtar tarayıcının yerel deposunda, açık metin olarak tutulur. Bu '
      'makineye erişen biri onu görebilir.';
  static const String save = 'Kaydet';
  static const String saved = 'Kaydedildi.';
  static const String theme = 'Görünüm';
  static const String themeSystem = 'Sistem';
  static const String themeLight = 'Açık';
  static const String themeDark = 'Koyu';

  // --- token gate ---
  static const String tokenNeededTitle = 'Anahtar gerekiyor';
  static const String tokenNeeded =
      'API anahtarı girilmeden hiçbir şey okunamaz. Aşağıya yazın.';
  static const String connect = 'Bağlan';
  static const String tokenEmpty = 'Anahtar boş olamaz.';
  static const String addressEmpty = 'Adres boş olamaz.';
  static const String addressNotAbsolute =
      'Adres http:// veya https:// ile başlamalı.';

  // --- failures ---
  static const String unauthorized =
      'Anahtar kabul edilmedi. Ayarlardan kontrol edin.';
  static const String retry = 'Tekrar dene';

  /// Named separately from a plain connection error on purpose. On the web a
  /// blocked cross-origin request and a dead server arrive identically - the
  /// browser refuses to show the response to the page - so a message naming only
  /// one of them sends the reader looking in the wrong place.
  static const String unreachable =
      'API\'ye ulaşılamadı. Adres yanlış olabilir, çalışmıyor olabilir, ya da '
      'tarayıcı isteği engellemiş olabilir. Tarayıcının konsolu hangisi '
      'olduğunu söyler.';

  /// Ağ hatası değil, sürüm uyuşmazlığı. Ayrı yazılmasının sebebi bir kez
  /// pahalıya mal olması: `publish/` altındaki eski API 200 dönüyordu ve ekran
  /// "ulaşılamadı" diyordu.
  static const String responseNotUnderstood =
      'API yanıt verdi ama bu sürümün beklediği alanlar yok. Çalışan API büyük '
      'ihtimalle eski bir derleme — publish klasörünü yenileyin.';

  static String serverSaidNo(int statusCode) =>
      'API $statusCode döndü.';
}
