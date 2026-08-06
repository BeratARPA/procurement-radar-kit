# Teknik Kararlar

Bu dosya kapalı kararların kaydı. Claude Code bunları sormayacak, uygulayacak.
Bir kararı değiştirirsen buraya gerekçesiyle yaz, eskisini silme — üstünü çiz.

---

## D-01 — Veri erişimi: Dapper, EF Core yok

**Karar:** Npgsql + Dapper. Raw SQL.

**Gerekçe:** Şema sabit ve zaten SQL olarak yazılmış durumda. İş yükü ağırlıklı
olarak toplu insert ve basit select. EF Core'un change tracking'i burada net
maliyet, net fayda yok. Ayrıca raw SQL, parser'ı bozulan kaynağı yeniden
işlerken elle müdahaleyi kolaylaştırıyor.

**Sonuç:** `ProcurementRadar.Persistence` içinde repository sınıfları, her biri
elle yazılmış SQL barındırır. Migration dosyaları ile aynı SQL lehçesi.

---

## D-02 — Migration: DbUp, gömülü SQL script

**Karar:** DbUp. Script'ler `src/ProcurementRadar.Migrator/Scripts/` altında,
embedded resource olarak, `001_initial.sql` biçiminde numaralandırılmış.

**Gerekçe:** D-01 ile tutarlı. EF migrations olmadan versiyonlu şema yönetimi
için en az sürtünmeli yol. Script'ler Supabase SQL editöründe de elle
çalıştırılabilir — ilk kurulumda işe yarar.

**Kural:** Uygulanmış bir script asla düzenlenmez. Yeni numara açılır.

---

## D-03 — LLM skorlama: v1'de YOK

**Karar:** v1'de LLM çağrısı yapılmayacak. Yalnızca kural tabanlı filtre.
T-13 ertelendi.

**Gerekçe:** Sıfır maliyet kısıtı sebebiyle değil — hacim sebebiyle. 80 kaynak,
sadece Ankara, sadece yazılım filtresi → günde tahminen 2-5 ilan. Beş satırı
elle okumak beş dakika sürer. Günde beş satırı okumamak için kurulan bir LLM
katmanı, o hacimde anlamsız karmaşıklık.

**Ne zaman eklenir:** Günlük ilan sayısı 30'u geçtiğinde veya kapsam Türkiye
geneline çıktığında. O noktada D-03'ü revize edin.

**Uygulama notu:** `notice_score.llm_score` kolonu şemada kalır, `null` durur.
Şemayı sonradan değiştirmek zorunda kalmayın.

---

## D-04 — Depolama: yerel disk

**Karar:** Object storage yok. Ham gövdeler ve dokümanlar yerel diskte.

```
data/raw/{source_id}/{yyyy}/{MM}/{dd}/{raw_fetch_id}.html.gz
data/doc/{notice_id}/{sha256_first8}_{sanitized_file_name}
```

Kök dizin `PR_DATA_DIR` env değişkeninden okunur.

**Gerekçe:** Hacim hesabı: 80 kaynak, günde ~40 değişen sayfa, gövde başına
~60 KB, gzip sonrası ~12 KB → günde yarım MB, yılda ~180 MB. Bunun için bulut
depolama kurmak gereksiz.

**Kural:** `data/` dizini `.gitignore` içinde. Yedekleme: haftalık
`tar czf` ile ikinci bir diske veya birinizin makinesine. Otomatik yedek
kurmayın, elle yeterli.

---

## D-05 — Veritabanı: Docker'da yerel PostgreSQL

**Karar:** Supabase yok. `docker compose` ile PostgreSQL 16, yerel volume.

**Gerekçe:** Hesap yok, kota yok, kart yok, ağ gecikmesi yok. Veri hacmi
yıllık birkaç yüz MB — ücretsiz katman limitleriyle uğraşmaya değmez.

**Kural:** `docker-compose.yml` repo kökünde. `POSTGRES_PASSWORD` env'den.
Volume `./data/pgdata`, o da `.gitignore` içinde.

---

## D-06 — Secrets

**Karar:** Geliştirmede `dotnet user-secrets`, çalışırken `.env` dosyası
(`.gitignore` içinde) veya systemd `EnvironmentFile`.

**Env değişkenleri:**

```
PR_DB_CONNECTION_STRING
PR_DATA_DIR                # varsayılan ./data
PR_TELEGRAM_BOT_TOKEN
PR_TELEGRAM_CHAT_ID
PR_CONTACT_EMAIL           # User-Agent içinde kullanılır
```

Beş değişken. Supabase ve Anthropic anahtarları listeden çıktı.

**Kural:** Commit öncesi `git diff --staged` ile kontrol. `.env` asla
commit edilmez.

---

## D-07 — Loglama

**Karar:** Serilog, konsola JSON, dosyaya günlük rolling. Log mesajları İngilizce.

**Seviye kuralı:**
- `INFO` — kaynak tarandı, N yeni ilan, bildirim gönderildi
- `WARN` — tarih ayrıştırılamadı, doküman indirilemedi, LLM parse hatası
- `ERROR` — HTTP hatası, DB hatası, parser exception

`WARN` ve `ERROR` haftalık raporda sayılır. Sessiz kalan kaynak `WARN` değil,
ayrı bir Telegram alarmıdır (bkz. spec Bölüm 11).

---

## D-08 — Zaman

**Karar:** DB'de `timestamptz`, kodda `DateTimeOffset`, her şey UTC.
Kaynak sayfalar `Europe/Istanbul` — dönüşüm `Parse` içinde yapılır.

**Gerekçe:** Son teklif saati bu projenin en kritik alanı. Yaz saati veya
sunucu timezone'u yüzünden bir saatlik kayma, kaçırılmış iş demek.

**Kural:** `DateTime.Now` ve `DateTime.UtcNow` kod tabanında yasak.
`TimeProvider` inject edilir, testte sahtelenir.

---

## D-09 — Test

**Karar:** xUnit. Parser'lar için fixture tabanlı, geri kalan için birim test.
Entegrasyon testi yok (v1'de maliyeti faydasından fazla).

**Kural:** Her `ISourceAdapter` implementasyonu için en az bir gerçek HTML
fixture ve bir test. Fixture'lar gerçek sayfadan indirilmiş olmalı, elle
yazılmış sahte HTML kabul edilmez.

---

## D-10 — Barındırma: kendi makineniz

**Karar:** VPS yok. Üçünüzden birinin makinesinde, tercihen eski bir laptop
üzerinde Ubuntu Server + systemd service.

**Gerekçe ve dürüst maliyet:** Sıfır TL değil — elektrik var. 10-15 W sürekli
tüketim ayda ~7-10 kWh, yaklaşık 25-35 TL. Bunun dışında hiçbir gider yok.

**Neden VPS değil:** Aylık 150-200 TL. Şu an gereksiz.

**Neden GitHub Actions değil:** Zamanlanmış iş akışlarında 5 dakikalık taban
sınır var, yoğun saatlerde 10-30 dakika gecikme yaşanıyor, 60 gün commit
olmayan depoda otomatik devre dışı kalıyor, özel depoda ayda 2.000 dakika
sınırı var. Genel depoya alırsanız kaynak listeniz — tek rekabet avantajınız —
herkese açık olur. Zamana duyarlı iş için uygun değil.

**Neden Oracle Cloud Always Free değil (şimdilik):** Gerçekten ücretsiz ve
kalıcı, ama kayıt için kredi kartı doğrulaması istiyor ve bazı bölgelerde
ARM kapasitesi bulunamıyor. Denemeye değer, ama v1 buna bağlanmasın.

**Uptime gerçeği:** Makine kapalıyken duyuru kaçırırsınız. Ama doğrudan temin
duyuruları mesai saatlerinde yayınlanıyor ve pratikte 2-5 günlük pencere
veriliyor — 24 saat yasal taban, tipik değer değil. Günde 12-16 saat açık bir
makine işinizi görür. Mükemmel uptime v2 konusu.

**Kural:** `systemd` service `Restart=always` ile. Makine açıldığında worker
otomatik başlar. Kapanma/açılma sonrası ilk tarama hemen tetiklenir, bir
sonraki periyot beklenmez.

**Güncelleme — 6 Ağustos 2026:** ~~Ubuntu Server + systemd~~ Ekipte ikinci bir
makine yok; sistem geliştirmenin yapıldığı Windows 11 makinesinde çalışacak,
PostgreSQL zaten orada Docker Desktop üzerinde. Kararın özü değişmedi (kendi
makinemiz, VPS yok, aylık gider yok), yalnızca işletim sistemi değişti.

Windows'ta servis değil **oturum açılışına bağlı zamanlanmış görev**
kullanılıyor. Sebebi teknik: Docker motoru Docker Desktop'ın içinde ve Docker
Desktop bir kullanıcı oturumu istiyor — açılışta değil, giriş yapınca başlıyor.
Windows Service oturumdan önce başlar, veritabanını bulamaz ve boşa döner. Bu
makineyi gerçekten ayağa kaldıran şey kullanıcının giriş yapması.

Bunun dürüst maliyeti: makine kapalıyken veya **uykudayken** hiçbir şey
toplanmıyor, ve laptop varsayılan olarak uyur. Kurulum betiği güç ayarlarını
kontrol edip uyarıyor ama değiştirmiyor — makinenin sahibi o kararı kendi verir.
Kurulum: `deploy/install-windows.ps1`, ayrıntılar `docs/deployment.md`.

---

## D-11 — Arşiv ham bayt tutar, çözülmüş metin değil

**Karar:** `raw_fetch` arşivi sunucudan gelen baytları olduğu gibi gzip'leyip
saklar. Karakter kodlaması çözümü ayrıştırma anında yapılır, indirme anında
değil. `spec.md` Bölüm 6'daki `FetchResult.Body` alanı bu yüzden `string`
değil `byte[]`.

**Gerekçe:** Mimari kural 2 "parser kırılınca düzeltip arşivi yeniden işleriz"
diyor. Kodlama çözümü indirme anında kalıcılaşırsa bu kural o alan için
geçersiz olur: yanlış çözülmüş bir sayfa, arşiv yeniden işlenerek kurtarılamaz,
çünkü kaybolan bilgi arşivin içinde değildir.

Bu teorik bir risk değil. Türk kamu kurumlarının sayfaları hâlâ yaygın olarak
`windows-1254` ve `iso-8859-9` ile sunuluyor, önemli bir kısmı kodlamayı
yalnızca `<meta>` etiketinde bildiriyor. Yanlış tahmin, ilan başlığındaki her
`ş`, `ğ`, `İ` karakterini bozar — ve başlık, T-11'in anahtar kelime filtresinin
okuduğu tek alandır.

İkinci gerekçe tutarlılık: `content_hash` zaten baytlar üzerinden
hesaplanıyor. Arşiv metin tutsaydı hash ile arşiv farklı şeyleri tarif ederdi.

**Sonuç:** `raw_fetch.content_type` kolonu eklendi (`003`). HTTP başlığındaki
`charset` bilgisi baytların içinden geri kazanılamaz — `<meta>` etiketi
gövdededir ama HTTP başlığı değildir — bu yüzden ayrıca saklanır.

**Kural:** `Parse(string body, Source source)` imzası değişmedi. Çözümleme
parse hattında, `BodyDecoder` ile, `Parse` çağrılmadan hemen önce yapılır.
Parser'lar hâlâ string üzerinde çalışan saf fonksiyonlar.

---

## D-12 — EKAP: Playwright, imza taklidi yok

**Karar:** EKAP'a yalnızca gerçek bir tarayıcı üzerinden erişilir. Uygulamanın
API imzası yeniden üretilmez. Doğrudan Temin araması kapsam dışıdır.

**Bulgu.** `ekapv2.kik.gov.tr` bir Angular uygulaması ve arama sonuçları
`POST /b_ihalearama/api/Ihale/GetListByParameters` adresinden JSON olarak
geliyor. Yanıt tam ihtiyacımız olan şey: `ihaleAdi`, `ikn`, `idareAdi`,
`ihaleIlAdi`, `ihaleTarihSaat`, `dokumanListe`.

Ama bu uç nokta imzasız isteği kabul etmiyor. Uygulama her çağrıya dört başlık
ekliyor — `X-Custom-Request-Ts`, `-Siv`, `-R8id`, `-Guid` — ve bunlar istemci
tarafında şifrelenmiş zaman damgası, IV ve istek kimliği. Bunlarsız sunucu
`401 İstek doğrulanamadı. HataKodu: 1200` dönüyor. Çerez, `Origin` ve `Referer`
eklemek yetmiyor; deneyerek doğrulandı.

**Gerekçe.** Bu başlıkları C#'ta yeniden üretmek, gömülü anahtarı çıkarıp
imzalama şemasını taklit etmek demek. 401 bir hata değil, sitenin "bu API'yi
yalnızca benim istemcim çağırsın" demesi. Aşmak bir erişim korumasını devre dışı
bırakmaktır ve bu depoda aynı karar iki kez daha verildi: Ankara Büyükşehir'in
otomatik isteklere döndüğü 403 aşılmadı, `dosyaism.saglik.gov.tr`'nin
`Disallow: /` kuralına uyuldu. Üçüncüsünde farklı davranmanın gerekçesi yok.

Playwright'ta böyle bir sorun yok: uygulamanın kendi JavaScript'i çalışır,
istekleri kendisi imzalar, biz yalnızca sonucu okuruz. Sitenin robots.txt'si de
taramaya izin veriyor (`ekap.kik.gov.tr/ekap/robots.txt` → `Disallow:` boş).
Zaten `spec.md` Bölüm 13 Playwright'ı "yalnızca gerektiğinde, kaynakların ~%10'u"
diye stack'e koymuştu; burası tam olarak o durum.

**Mimari.** Tarayıcı `Collector` katmanında kalır. `Parsers` içindeki
`EkapAdapter` yalnızca `IEkapSearchClient` portunu tanır; `Parse` ise arşivlenen
JSON üzerinde saf bir fonksiyondur. Sonuç: parser fixture ile, tarayıcısız test
edilir ve kırılganlık `FetchAsync`'te toplanır. Arşiv HTML değil JSON tutar —
D-11 ile tutarlı, çünkü saklanan yine sunucudan gelen ham gövdedir.

**Doğrudan Temin de var, kapsamı sınırlı.** Bu bölümün ilk hali "halka açık
doğrudan temin araması yok" diyordu — yanlıştı ve düzeltildi. Üst menü ilk
yüklemede render olmuyor; arama yapıldıktan sonra beliriyor ve içinde
"Doğrudan Teminler" var. Adres `https://ekapv2.kik.gov.tr/ekap-dt/search`,
sonuçlar `POST /b_dogrudantemin/api/DogrudanTemin/GetListByParametersElastic`
adresinden geliyor ve aynı imza zorunluluğu burada da işliyor: sayfanın kendi
içinden imzasız `fetch` denendi, `406` döndü. Yani karar aynı — tarayıcı.

Filtreleri projenin istediği her şeyi taşıyor: son teklif verme tarihi, idarenin
ili, OKAS kodu, doğrudan temin maddesi. Yanıt da ihale aramasından daha zengin:
`alimAdi`, `dogrudanTeminNumarasi`, `idareAdi`, `il.adi`, `maddeDayanagiAdi`
(`22/d*` gibi) ve en önemlisi `fiyatTeklifiSonTarihSaati` — ISO 8601, offset'li,
yani `TurkishDateParser`'a hiç uğramadan okunuyor.

**Ama:** sayfa "17.07.2026 tarihi itibarıyla Yeni Doğrudan Temin uygulaması
pilot olarak kullanıma açılmıştır" diyor. Şu an yalnızca pilot idarelerin ve o
tarihten sonraki duyuruları görünüyor. Yani EKAP doğrudan temin kaynağı bugün
kapsamın küçük bir bölümünü veriyor ve zamanla büyüyecek. Kurumların kendi
sayfaları bu yüzden emekliye ayrılmıyor; ikisi birlikte çalışır ve `ikn` /
doğrudan temin numarası üzerinden tekilleştirme çakışmayı zaten halleder.

**Sonuç:** T-16 iki adapter demek — `ekap_tender` ve `ekap_direct_procurement` —
ve ikisi de aynı tarayıcı istemcisini kullanır, çünkü etkileşim ikisinde de aynı:
sayfayı aç, tek görünür `input[placeholder='Ara']` alanını doldur, Enter'a bas,
uygulamanın kendi imzaladığı isteğin yanıtını oku.
