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
  static const String filter = 'Filtre';
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
  static const String filters = 'Filtreler';
  static const String more = 'Diğer';
  static const String clearFilters = 'Sıfırla';

  static String filtersActive(int count) => 'Filtreler · $count';

  // --- notice list filters ---
  static const String scopeFilter = 'Kapsam';
  static const String scopeOpen = 'Açık olanlar';
  static const String scopeAll = 'Tüm arşiv';

  static const String scoreFilter = 'Puan';
  static const String scoreAny = 'Tümü';
  static const String scoreSome = 'Puan almış';

  /// The number is read from the stored setting, not written here.
  ///
  /// This label used to say 30 whatever the worker was actually running on, so
  /// it stopped being true the moment anybody moved the slider on the Filtre
  /// screen — a chip that names itself after a number it does not know.
  static String scoreNotified(int threshold) => 'Bildirim eşiği ($threshold+)';

  static const String deadlineFilter = 'Son teklife kalan';
  static const String anyTime = 'Farketmez';
  static const String within24 = '24 saat';
  static const String within72 = '3 gün';
  static const String withinWeek = '1 hafta';

  static const String sortBy = 'Sırala';
  static const String sortDeadline = 'Aciliyete göre';
  static const String sortScore = 'Puana göre';

  static const String deadlineKnown = 'Son teklif tarihi';
  static const String deadlineKnownAll = 'Olsa da olmasa da';
  static const String deadlineKnownOnly = 'Sadece okunabilenler';
  static const String clearFilter = 'Filtreyi temizle';
  static const String noneMatchFilter =
      'Bu filtreye uyan ilan yok. Filtreyi gevşetin.';

  static String noticeCount(int n) => '$n ilan';

  static String noticeCountFiltered(int shown, int total) =>
      '$total ilandan $shown tanesi gösteriliyor';

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
  static const String addressLoopbackElsewhere =
      'localhost bu cihazın kendisi demek. Toplamayı yapan makinenin ev '
      'ağındaki adresini yazın, örneğin http://192.168.1.10:5080';
  static const String addressNotAbsolute =
      'Adres http:// veya https:// ile başlamalı.';

  // --- filter ---
  static const String threshold = 'Bildirim eşiği';
  static const String thresholdHelp =
      'Bu puanın altındakiler veritabanına yazılır ama bildirim üretmez. '
      'En ağır tek terim 40, çoğu 30 — eşik 60 olsaydı bir ilanın duyurulması '
      'için iki ayrı terim tutması gerekirdi (D-14).';
  static const String includeTerms = 'Aranan terimler';
  static const String excludeTerms = 'Elenen terimler';
  static const String excludeHelp =
      'Eşleşen ilan, kaç tane aranan terim tutarsa tutsun elenir. Bu liste '
      'olmadan bildirimlerin çoğu donanım alımı olur.';
  static const String addTerm = 'Terim ekle';
  static const String weight = 'Ağırlık';
  static const String preview = 'Önizleme';
  static const String previewCaught = 'Bildirilecekler';
  static const String previewMissed = 'Kıl payı kaçanlar';
  static const String previewMissedHelp =
      'Puan almış ama eşiği geçememiş ilanlar. Eşiğin doğru yeri buraya '
      'bakılarak bulunur.';
  static const String previewNothing = 'Bu ayarla hiçbir ilan bildirilmezdi.';
  static const String previewIdle = 'Önizleme için bir şey değiştirin.';
  static const String unsavedChanges = 'Kaydedilmemiş değişiklik var';
  static const String revert = 'Geri al';
  static const String savedAndLive =
      'Kaydedildi. Worker en geç 30 saniye içinde yeni ayarla çalışır.';
  static const String untouchedKeysNote =
      'Bu ekranın göstermediği ayarlar (okas listeleri, notlar) olduğu gibi '
      'korunur.';

  static String previewSummary(int considered, int wouldNotify) =>
      '$considered ilandan $wouldNotify tanesi bildirilirdi';

  // --- rescore ---
  static const String rescore = 'Yeniden puanla';
  static const String rescoreHelp =
      'Önizleme yalnızca hesaplar. Bir ilan yalnızca toplandığı anda '
      'puanlanır, yani bugün eklenen bir kelime yalnızca yarın gelecek '
      'ilanlara işler; arşivdekiler artık var olmayan bir listenin puanını '
      'taşımaya devam eder. Bu düğme arşivin tamamını kayıtlı listeyle '
      'yeniden puanlar.';
  static const String rescoreUnsaved =
      'Yeniden puanlama kayıtlı listeyle çalışır. Önce kaydedin.';
  static const String rescoreConfirmTitle = 'Arşivin tamamı yeniden puanlansın mı?';
  static const String rescoreConfirmBody =
      'Kayıtlı kelime listesi tüm ilanlara uygulanır ve puanlar üzerine '
      'yazılır. Eşiği yeni geçen ilanlar için Telegram bildirimi gider — '
      'bir kısmının son teklif tarihi geçmiş olabilir.';
  static const String rescoreConfirm = 'Yeniden puanla';
  static const String rescoreRunning = 'Puanlanıyor…';
  static const String rescoreGained = 'Eşiği yeni geçenler';
  static const String rescoreDropped = 'Artık eşiğin altında kalanlar';
  static const String rescoreNothing =
      'Hiçbir puan değişmedi. Arşiv zaten kayıtlı listeyle puanlanmış.';

  static String rescoreSummary(int considered, int changed, int threshold) =>
      '$considered ilan yeniden puanlandı, $changed tanesinin puanı değişti '
      '(eşik $threshold)';

  // --- awards ---
  static const String awards = 'Sonuçlar';
  static const String winner = 'Yüklenici';
  static const String contractAmount = 'Sözleşme bedeli';
  static const String estimatedCost = 'Yaklaşık maliyet';
  static const String bidCount = 'Geçerli teklif';
  static const String awardedAt = 'Sözleşme tarihi';
  static const String byAuthority = 'İdare bazlı';
  static const String concluded = 'sonuçlanan';
  static const String averageRatio = 'Ortalama sözleşme/tahmin';
  static const String averageBidCount = 'Ortalama teklif';
  static const String noAwardsYet = 'Henüz sonuçlanmış ihale yok.';

  /// The empty state carries the reason. spec Bolum 16 says the archive is the
  /// real value of this system, and it fills slowly on purpose — a screen with
  /// nothing on it and no explanation reads as broken.
  static const String noAwardsWhy =
      'Sonuç ilanı, teklifler açıldıktan sonra değil, sözleşme imzalandıktan '
      'sonra yayımlanıyor. Bu yüzden yalnızca son teklif tarihinin üzerinden '
      'iki hafta geçmiş ve gerçek bir İKN taşıyan ihaleler soruluyor (D-15). '
      'Doğrudan teminlerin sonuç ilanı olmuyor, onlar bu listeye hiç girmiyor.';

  static String awaitingResult(int count) => count == 0
      ? 'Şu an sonucu beklenen ihale de yok — henüz hiçbiri olgunlaşmadı.'
      : '$count ihale sonuç bekliyor, altı saatte beşi soruluyor.';

  static String ratioLabel(double ratio) =>
      '${(ratio * 100).round()}% (tahminin)';

  // --- sources ---
  static const String sources = 'Kaynaklar';
  static const String addSource = 'Kaynak ekle';
  static const String editSource = 'Kaynağı düzenle';
  static const String sourceActive = 'Taranıyor';
  static const String sourceInactive = 'Kapalı';
  static const String noSources = 'Kayıtlı kaynak yok.';
  static const String authorityName = 'İdare adı';
  static const String authorityTypeLabel = 'Tür';
  static const String province = 'İl';
  static const String district = 'İlçe';
  static const String sourceUrlLabel = 'Sayfa adresi';
  static const String adapterKeyLabel = 'Adapter';
  static const String pollInterval = 'Tarama aralığı (dakika)';
  static const String notes = 'Notlar';
  static const String notesHelp =
      'Bu kaynak neden burada, ya da neden kapalı. Kapalı kaynakların sebebi '
      'burada duruyor.';
  static const String selectors = 'Selector\'lar';
  static const String rowSelector = 'Satır (row_selector)';
  static const String titleSelector = 'Başlık (title_selector)';
  static const String linkSelector = 'Bağlantı (link_selector)';
  static const String dateSelector = 'Tarih (date_selector)';
  static const String timeSelector = 'Saat (time_selector)';
  static const String amountSelector = 'Tutar (amount_selector)';
  static const String documentSelector = 'Doküman (document_selector)';
  static const String baseUrlLabel = 'Kök adres (base_url)';
  static const String testAgainstArchive = 'Kayıtlı sayfaya karşı dene';
  static const String checkIdle =
      'Selector\'ları yazın ve düğmeye basın. Ağa çıkılmaz: sayfa arşivden '
      'okunur, yani denemek idareye hiçbir şeye mal olmaz.';
  static const String testResult = 'Sonuç';
  static const String pasteHtml = 'Sayfanın HTML\'ini yapıştırın';
  static const String pasteHtmlHelp =
      'Hiç taranmamış bir kaynak için. Tarayıcıda sayfayı açıp kaynağını '
      'kopyalayın — buradan ağa çıkılmıyor.';
  static const String newSourceIsOff =
      'Yeni kaynak kapalı olarak eklenir. spec Bölüm 5 üç gün gözlem sonrası '
      'açmayı söylüyor; selector\'ları denemeden açmak o adımı atlamak olur.';
  static const String lastNewNotice = 'Son yeni ilan';
  static const String failures = 'Üst üste hata';
  static const String openSourcePage = 'Sayfayı aç';
  static const String rowsFound = 'satır bulundu';
  static const String withDeadlineLabel = 'tarihi okundu';
  static const String withDocumentsLabel = 'doküman bağlantılı';
  static const String fromArchive = 'Arşivden';
  static const String fromPasted = 'Yapıştırılan sayfadan';

  /// The api answers with a code, not a sentence — Turkish does not live in a
  /// .cs file. This is where the sentence is.
  static String checkProblem(String code) => switch (code) {
        'config_unusable' => 'Selector ayarları okunamadı.',
        'no_archived_page' =>
          'Bu kaynağın arşivinde sayfa yok. Sayfayı yapıştırın ya da kaynağı bir kez taratın.',
        'nothing_to_check' => 'Denenecek bir sayfa verilmedi.',
        _ => code,
      };

  static String checkWarning(String code) => switch (code) {
        'no_rows' =>
          'Hiçbir satır okunamadı. Ya selector\'lar bu sayfaya uymuyor, ya da '
              'sayfada şu an gerçekten ilan yok. Açmadan önce sayfaya bakın.',
        'no_deadline_read' =>
          'Tek bir son teklif tarihi okunamadı. Bu, projenin en kritik alanı '
              '(spec Bölüm 7). date_selector\'ı markup\'a, biçimi de '
              'tr-date-patterns.json\'a karşı kontrol edin — ya da bu liste '
              'sayfasında gerçekten tarih yoktur.',
        'few_deadlines_read' => 'Satırların yarısından azında tarih okundu.',
        _ => code,
      };

  // --- politeness ---
  static const String politeness = 'Nezaket';
  static const String politenessHeader =
      'Alt sınırlar sunucuda zorlanıyor: bu ekrandan da, elle de aşılamaz. '
      'Kazanılacak hız yok, kaybedilecek IP var.';
  static const String hostInterval = 'Aynı host\'a istekler arası (saniye)';
  static const String jitter = 'Rastgele ek gecikme (saniye)';
  static const String requestTimeout = 'İstek zaman aşımı (saniye)';
  static const String maximumAttempts = 'Deneme sayısı';
  static const String nightWindow = 'Gece penceresi';
  static const String nightStart = 'Başlangıç';
  static const String nightEnd = 'Bitiş';
  static const String nightMultiplier = 'Katsayı';
  static const String respectRobots = 'robots.txt\'ye uy';
  static const String notOnThisScreen = 'Bu ekranda olmayanlar';
  static const String notOnThisScreenHelp =
      'Telegram anahtarı .env dosyasında ve bilerek burada değil — bir parolayı '
      'ekrana koymak onu tarayıcının deposuna koymak demek. Tarama aralıkları '
      've süpürme henüz ayar değil, kodda sabit (T-17 bir schedule anahtarı '
      'planlamıştı, açılmadı).';

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
