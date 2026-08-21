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

**Kural:** `data/` dizini `.gitignore` içinde. ~~Yedekleme: haftalık
`tar czf` ile ikinci bir diske veya birinizin makinesine. Otomatik yedek
kurmayın, elle yeterli.~~

**Güncelleme — 13 Ağustos 2026:** Elle yedek **bir kez alınmadı**. Sekiz gün
sonra arşiv 418 MB'a çıkmıştı ve tek kopyası bir SSD'deydi. "Elle yeterli"
cümlesi, kimsenin yapmadığı bir işi yapılmış saymanın adıydı. Otomatik yedek
D-24'te; nereye yazılacağı ayar, ne zaman yazılacağı zamanlanmış görev.

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

---

## D-13 — Kapsam: Türkiye geneli, Ankara ayrıcalığı yok

**Karar:** İl filtresi yok. EKAP ne getiriyorsa yazılır ve skorlanır.
Alaka kararını yer değil, kelime listesi ve eşik verir.

**Gerekçe.** İki tanesi ölçüldü, biri talep.

`spec.md` Bölüm 2 "önce Ankara" diyordu ve bu bir *çaba* kararıydı: seksen
kurumun sayfasını elle bulmak pahalı, bir ilden başlanır. EKAP'ta böyle bir
maliyet yok — tek kaynak, tüm ülke, aynı istek.

Ölçülen taraf şu: EKAP sayfa başına 10 sonuç veriyor ve il filtresini kendi
tarafımızda uyguluyorduk. "yazılım" için 914 eşleşmenin ilk 10'unda Ankara
çıkmazsa hiçbir şey görmüyorduk. Üstelik 10 satırı sorunsuz ayrıştırıp 0 ilan
bildirdiğimiz için T-15'in "parser bozuk" kuralı boşuna ateşliyordu — kural
doğruydu, ona yalan söyleyen adapter'dı.

Talep tarafı: ekip "Ankara'ya özel olmasına gerek yok, ben zaten filtrelerim"
dedi. Yazılım işi uzaktan teslim edilebilir; coğrafya bu iş kolunda ihale
mevzuatı kadar bağlayıcı değil.

**Sonuç.** `EkapAdapterConfig.Province` alanı kodda ve testte duruyor ama
seed'de kullanılmıyor. Sunucu tarafında il filtresi ancak arayüzdeki açılır
listeyi sürerek yapılabilir; hacim gerektirirse o zaman bakılır.

Kurumların kendi sayfaları hâlâ Ankara — orada kapsam elle toplanan URL'lerle
sınırlı ve bu değişmedi.

---

## D-14 — Bildirim eşiği: tek kelime yetmeli

**Karar:** `notify_threshold` 60'tan **30**'a indirildi.

**Gerekçe.** En ağır tek terim 40, çoğu 30. Eşik 60 iken bir ilanın
duyurulması için **iki ayrı terim** tutması gerekiyordu — yani bir tesadüf.

Bedeli ölçüldü: 572 ilanın 9'u puan aldı, 1'i eşiği geçti. Elenenler arasında
"36 Aylık Hastane Bilgi Yönetim Sistemi" (aylarca sürecek bir iş) ve "F5 Yük
Dengeleme Yazılımı" vardı; ikisi de tek terim tutup 30'da kaldı. Hiçbir şey
bozuk değildi, sistem sessizce bir tesadüf bekleyecek şekilde ayarlanmıştı.

**Koruma:** `RuleScorerTests.One_strong_keyword_is_enough_to_be_told_about`
artık en ağır terimin eşiğe erişebildiğini doğruluyor. Eşik yükseltilirse veya
ağırlıklar düşürülürse test düşer.

**Gürültü ne olacak:** `exclude` listesi 25 terimle donanımı zaten eliyor.
Fazla gürültü gelirse çare eşiği yükseltmek değil, `exclude`'a terim eklemek —
`spec.md` Bölüm 9'un dediği gibi filtre kalitesi kapsamdan önemli.

---

## D-15 — Sonuç takibi: aynı tarayıcı, ayrı ve yavaş saat

**Karar:** `award` tablosu, ihale sonuçlandıktan sonra EKAP'ın yayımladığı
**Sonuç İlanı**'ndan doldurulur. Ayrı bir kaynak, ayrı bir adapter veya ayrı bir
site yok; T-16'daki tarayıcı istemcisi ve `İlan Gör` düğmesi aynen kullanılır.

**Bulgu.** `POST /b_ihalearama/api/Ilan/GetList` bir İKN için ilanları döndürüyor
ve `ilanTip` alanı hangisi olduğunu söylüyor: `1` ihale ilanı, `4` sonuç ilanı.
Sonuçlanmış bir ihalede `sozlesmeId` de doluyor. `veriHtml` içinde ihtiyacımız
olan her şey var — 2026/1166872 için ölçüldü:

| Alan | Değer |
|---|---|
| Yaklaşık maliyet | 3.405.000,00 TRY |
| Sözleşme bedeli | 3.300.000,00 TRY |
| Toplam geçerli teklif | 1 |
| Yüklenici | PROMETUS BİLGİSAYAR ... LİMİTED ŞİRKETİ |
| Sözleşme tarihi | 16.07.2026 |

**Gerekçe.** Bu tablo altı ay sonra elinizde kalacak olan şey (`spec.md` Bölüm 16).
İlan "ne isteniyor" der; sonuç "kaça kapandı, kaç kişi girdi" der. Teklif fiyatını
belirleyen ikincisi. Bir ilan yakalamak bir fırsat, yüz sonuç okumak bir fiyat
politikası.

**Neden yavaş çalışıyor.** Altı saatte bir, beş İKN. Sonuç ilanının aciliyeti yok
— bugün yayımlanan sonuç haftaya da aynı bilgi. Buna karşılık her sorgu, EKAP
aramasının kullandığı nezaket kapısından geçen bir tarayıcı turu; son teklif saati
bu akşam olan bir ilanın o kapıda önceliği var.

**Neden 14 gün.** Sonuç ilanı son teklif tarihinden değil, **sözleşme
imzalandıktan** sonra yayımlanıyor. Ertesi gün sorulursa ihale ilanı geliyor ve
tur boşa gidiyor.

**Neden altı deneme.** Her ihale sonuçlanmıyor; iptal edilen bir süreç hiç sonuç
ilanı üretmiyor. Deneme sayacı olmasa böyle bir İKN arşiv yaşadığı sürece haftada
bir sorulurdu. `winner_name` boş bir `award` satırı "baktık, henüz yok" demektir;
altı denemeden sonra sıradan düşer.

**Yüklenici adı maskelenmiyor.** `CLAUDE.md` iletişim adı ve telefonu yasaklıyor;
yüklenici ise devletin kendi ilanında yayımladığı sözleşme karşı tarafı. Kim
olduğu bilinmeyen bir sonuç kaydının hiçbir değeri yok. Bu yoldan yalnızca dört
alan yazılıyor — ilanın görevli adı ve santral numarası taşıyan gerisi saklanmıyor.

**Uygulama:** `AwardParser` (saf, fixture'ı `ekap_award/2026-1166872-sonuc.json`),
`AwardSweeper` + `AwardWorker`, `008_award_sweep.sql` (`checked_at`, `attempts`),
Telegram'da `/sonuclar`.

---

## D-16 — Arayüz: Flutter + yerel HTTP API, ayarlar veritabanında

**Karar:** Tek Flutter kod tabanı (önce Web, sonra Windows masaüstü, sonra
Android), aynı makinede çalışan bir ASP.NET Core minimal API üzerinden konuşur.
Ayarlar `config/*.json` dosyalarından `setting` tablosuna taşınır; dosyalar
ilk değer (seed) olarak kalır.

**Güncelleme — 7 Ağustos 2026:** ~~minimal API~~ Controller tabanlı ASP.NET Core
Web API. Kararın özü değişmedi (aynı solution, aynı Dapper repository'leri, yeni
SQL yok); değişen, uçların nasıl yazıldığı.

Sebep minimal API'nin yetersizliği değil — verdiği hiçbir şey kaybolmadı. Sebep
eksik olanlardı: **OpenAPI dokümanı yoktu**, dolayısıyla uçları görmenin veya
denemenin bir yolu yoktu; on iki uç tek bir `Program.cs`'te kurulumla iç içeydi;
repository kayıtları olduğu gibi dışarı veriliyordu, yani iç şema dışarı
sızıyordu; hata biçimi tutarsızdı.

Şimdi: konu başına bir controller, `Contracts/` altında istek ve yanıt tipleri,
her uçta `[ProducesResponseType]`, hatalar RFC 7807 `ProblemDetails`, token
kontrolü middleware yerine bir filtre (böylece dokümanda görünüyor ve Swagger'da
"Authorize" düğmesi oluyor). Swashbuckle ile `/swagger` arayüzü ve
`/swagger/v1/swagger.json`.

Somut karşılığı Flutter'da: **Dart istemcisi bu dokümandan üretilecek**, model
sınıfları elle yazılmayacak, uç değişince istemci de değişecek. Controller'ların
XML yorumları dokümanın kendisi — ikinci bir kopya yok, dolayısıyla eskiyecek bir
kopya da yok.

**Neden API var.** Flutter veritabanına doğrudan bağlanamaz: tarayıcıda ham TCP
soketi yok, mobilde de bağlantı dizesini uygulamanın içine gömmek demek.
`ProcurementRadar.Api` aynı solution'da, aynı Dapper repository'lerini kullanır,
yeni SQL yazmaz. ASP.NET Core .NET 8'in içinde geliyor — yeni bağımlılık, hesap
veya gider yok.

`CLAUDE.md` "arayüz, auth, çok kiracılılık yapma" diyordu. Bu madde ekibin
kendi kararıyla açıldı; auth ve çok kiracılılık hâlâ kapsam dışı — tek takım,
tek token.

**Neden ayarlar veritabanında.** Bugün ayarlar üç yerde ve hiçbiri çalışırken
değişmiyor: `config/*.json` (yeniden başlatma ister), `source` tablosu (elle
SQL), koddaki sabitler (yeniden derleme ister). Ayar ekranının "Kaydet" tuşunun
bir anlamı olması için ayarların tek yerde ve sıcak okunabilir olması gerekiyor.

`setting` tablosu anahtar + `jsonb` tutar. Worker 30 saniyede bir yalnızca en
son `updated_at`'e bakar; değişmişse ilgili ayarı tazeler. Dosyalar silinmiyor:
tablo boşsa dosyadan doldurulur, doluysa dosyaya bakılmaz. Böylece temiz kurulum
da, kurulmuş sistem de aynı kodla çalışır.

Her değişiklik `setting_history`'ye yazılır. Yanlış bir kelime listesi
bildirimleri sessizce kesebilir; geri alınamayan bir ayar ekranı, ayarı
olmayan bir sistemden kötüdür.

**Nezaket ayarlarının alt sınırı sunucuda.** Host başına asgari bekleme, gece
penceresi ve `robots.txt` uyumu ekranda görünür ve düzenlenebilir, ama API
5 saniyenin altını ve robots'un kapatılmasını reddeder. `CLAUDE.md`'nin
dediği gibi kazanılacak hız yok, kaybedilecek IP var; bir ayar ekranı bunu
tek yazı hatasıyla mümkün kılmamalı.

**Erişim: şimdilik ev ağı.** API `http://0.0.0.0:60080` dinler, telefondan
`http://<makine-ip>:60080` ile açılır. Dışarıdan erişim (Tailscale veya
Cloudflare Tunnel) sonraya bırakıldı ve API'de değişiklik gerektirmiyor.
Yine de istek başlığında tek bir statik token aranır (`PR_API_TOKEN`) —
kullanıcı hesabı değil, ev ağındaki başka bir cihazın yanlışlıkla ayar
değiştirmesine karşı.

~~**iOS yok.** Derlemesi Mac istiyor, ekipte Mac yok. Safari'den web sürümü
açılır ve ana ekrana eklenir; bu bir eksiklik, gizlenmiyor.~~

**Güncelleme — 9 Ağustos 2026: iOS ve macOS projeleri eklendi, ama derlenmedi.**
Kararın değişen tarafı şu: *derleyememek* ile *proje dosyalarının hiç olmaması*
aynı şey değil. İkincisi, bir gün Mac'i olan biri geldiğinde bir akşamlık iş
çıkarır ve o akşam bulunacak şeyler tam olarak Android'de bulunanlar oldu.

Beş hedef de aynı `ui/` kod tabanından çıkıyor: web, Android, Windows, ve artık
iOS ile macOS. Değişmeyen: bu makinede `flutter build ios` ve `flutter build
macos` **alt komutları bile yok** — Flutter onları Windows'ta hiç tanımıyor.
Yani iOS/macOS "hazır", "doğrulandı" değil ve bu ayrım burada duruyor.

Eklerken iki şey çıktı, ikisi de Android'in `INTERNET` izniyle aynı biçimde —
yalnızca gerçek cihazda, yayın derlemesinde görünürdü:

| Platform | Şablonun bıraktığı | Sonucu |
|---|---|---|
| macOS | `com.apple.security.network.client` yok | Sandbox'lı uygulama **tek bir giden istek yapamaz** |
| iOS | `NSAppTransportSecurity` yok | ATS düz http'yi reddeder, api ise `http://<ip>:60080` |

macOS'ta `network.server` var ama o Dart VM servisi için; giden istekle ilgisi
yok. İkisi de eklendi.

iOS tarafında `NSAllowsArbitraryLoads` değil **`NSAllowsLocalNetworking`**
kullanıldı: yalnızca yerel adlara ve ayrılmış özel IP aralıklarına düz http
izni verir, internetin geri kalanı kapalı kalır. Android'de blanket izin
verilmesinin sebebi o platformun daha darını sunmaması; iOS sunuyor, o yüzden
dar olan seçildi. iOS 14'ün yerel ağ izni için `NSLocalNetworkUsageDescription`
de eklendi — olmadığında istek sessizce başarısız olur.

Bundle kimliği üç platformda da `com.procurementradar.radar`. `flutter create`
iOS/macOS'a `com.procurementradar.procurementRadar` yazmıştı; sonradan
değiştirmek uygulamayı silip yeniden kurmayı gerektirirdi.

**Sistem ekranı yalnızca Windows'ta.** `isSupported => Platform.isWindows`, yani
`dart:io`'su olan iOS/macOS/Android derlemelerinde de gizli. Doğrusu bu: o ekran
Docker Desktop'ı ve Windows zamanlanmış görevlerini sürüyor.

**Linux masaüstü eklenmedi.** İsteyen olmadı ve derlenmeyen bir hedef eskiyen
bir hedeftir; gerekirse tek komut.

---

## D-17 — D-09 gözden geçirildi: gerçek veritabanına karşı bir uçuş öncesi kontrol

**Karar:** D-09'un "entegrasyon testi yok" maddesi duruyor — ama yanına
`preflight` komutu ekleniyor: her deploy'da, gerçek veritabanına ve gerçek
`setting` tablosuna karşı çalışan on saniyelik bir kontrol.

**Gerekçe — ölçüldü, tartışılmadı.** D-09 "maliyeti faydasından fazla" derken
haklıydı; bir yıl sonra veri şu:

| Kırılma | Birim testler | Nerede yakalandı |
|---|---|---|
| Pozisyonel record + Dapper (2 kez) | geçti | canlıda |
| `ExecuteScalarAsync<DateTimeOffset?>` | geçti | 33 hata sonra logda |
| Eksik `@` parametresi | geçti | 2 saat sonra logda |
| Ayar tablosu ile dosyanın ayrışması | geçti | kullanıcı sorunca |

Dördü de aynı boşluktan geçti: **arayüzün arkasına sahte konmuş bir test kodu
doğrular, sürücüyü değil.** Bunların hiçbiri mantık hatası değildi.

**Neden hâlâ entegrasyon testi değil.** xUnit içinde veritabanı isteyen testler
`dotnet test`'i bir kuruluma bağımlı kılar ve CI'sı olmayan bir projede
çalıştırılmayan teste dönüşür. `preflight` ayrı bir komut: kurulum betiği
çağırır, başarısız olursa deploy durur, ve makinesi olmayan biri `dotnet test`
çalıştırmaya devam eder.

**Kapsam:** her repository'nin okuma yolu, `procurement_notice` upsert'ü
(transaction içinde, geri alınarak), her ayarın tablodan kendi yükleyicisiyle
okunması, ve tablo ile `config/*.json` arasındaki ayrışma.

---

## D-18 — Kontrol paneli: aynı uygulama, ayrı yol, işletim sistemine doğrudan

**Karar:** Sistemi yönetme ekranı (Docker, worker, api, CLI, log) ayrı bir
uygulama değil, `ui/` kod tabanının **Windows masaüstü derlemesinde** görünen
bir bölüm. Web derlemesinde hiç yok.

**Neden ayrı uygulama değil.** Tema, yönlendirme ve üretilmiş api istemcisi
zaten burada; ikinci bir kod tabanı bunların ikinci bir kopyasını yaşatmak
demekti. T-22 zaten Windows derlemesini istiyordu, bu onu öne çekti. Ve tek
uygulama olması gerçek kullanımla örtüşüyor: sabah açıp hem ne ilan geldiğine
hem her şeyin ayakta olduğuna aynı yerden bakılıyor.

**Neden web'de yok.** Tarayıcı süreç başlatamaz, Docker'a dokunamaz, zamanlanmış
görev okuyamaz, dosya sistemine bakamaz. Bu bir eksiklik değil, tarayıcının
güvenlik sınırı. `dart:io` koşullu import'un (`dart.library.io`) arkasında; web
derlemesi `system_control_stub.dart`'ı alır ve `isSupported` false döner, ekran
da hiç görünmez. Aynı sınır token'ın neden elle girildiğini de açıklıyor —
masaüstü sürümü `.env`'i okuyabildiği için orada bir "`.env`'den al" düğmesi var.

**Panel api'den geçmez.** Kontrol paneline en çok ihtiyaç duyulan an api'nin
cevap vermediği andır; api'ye soran bir panel tam o anda kararır. Bu yüzden
doğrudan `docker`, PowerShell ve log dosyaları okunur.

**Yerel dil tuzağı.** `schtasks /query` durumu görüntü dilinde basıyor — bu
makinede "Çalışıyor", "Running" değil. Görev durumu bu yüzden
`(Get-ScheduledTask).State` enum'undan okunuyor; enum adı her dilde aynı.

**`Process.run` yerine `Process.start`.** `Process.run(...).timeout(...)` doğru
okunuyor ve yanlış şeyi yapıyor: zaman aşımı future'ı tamamlar, çocuk süreç
yaşamaya devam eder. Panel 12 saniyede bir yenilendiği için, Docker Desktop
açılırken takılan bir `docker` her turda üç öksüz süreç bırakırdı.

**Kabuk komutu çalıştırdığının farkındayız.** Argümanlar liste olarak geçiyor,
`runInShell` kapalı, ve komutlar sabit listeden geliyor. Yazma yapan CLI
komutları (`seed`) önce onay soruyor. Tek kullanıcılı kendi makinemizde bu kabul
edilebilir; bu uygulama ağdan erişilebilir hale gelirse karar yeniden bakılmalı.

---

## D-20 — Son teklif tarihi için detay sayfası çekilmeyecek

**Karar:** Tarihi olmayan bir ilanın kendi sayfasını ayrıca çekip tarihi oradan
okuma fikri (T-23 diye önerilmişti) **yapılmayacak**. Ölçüldü, karşılığı yok.

**Sorun gerçek.** 9 Ağustos 2026 itibarıyla 23 aktif kaynak ilan üretiyor ama
tek bir son teklif tarihi üretmiyor; 282 ilan tarihsiz. Son teklif tarihi
`spec.md` Bölüm 7'nin "en önemlisi" dediği alan.

**Neden liste sayfasından okunamıyor.** 108 kaynağın hepsi arşivlenmiş kendi
sayfasına karşı denendi (T-21'in `check` ucu, ağa çıkmadan). 17 sayfanın
16'sında "son teklif" ifadesi hiçbir yerde geçmiyor — ne hücrede, ne paragrafta.
Sayfada görünen tarih çoğunlukla **yayın tarihi**: Çubuk'ta WordPress post
tarihi, Elmadağ'da `duyurutarihc`. Onu `bid_deadline_at`'e yazmak boş bırakmaktan
kötü — `ListOpen` `bid_deadline_at > now()` filtreliyor, yani ilan açık listeden
düşer ve Telegram "süresi geçti" der. Denendi ve geri alındı.

**Neden detay sayfasından da okunamıyor — asıl bulgu.** Yedi kaynağın birer ilan
sayfası nezaket kurallarıyla (robots kontrolü, doğru User-Agent, 6 sn aralık)
çekildi ve incelendi:

| Ne arandı | Sonuç |
|---|---|
| Tarihli "son teklif" cümlesi | 7 sayfanın 0'ında (biri 2015 tarihli eski ilan) |
| PDF/doküman eki | 7 sayfanın 1'inde |
| İlan gövdesi HTML'de var mı | Bala: başlık + "Modal content" + çerez politikası. Ayaş: yalnız menü |

Yani bu sayfalar ilanı **taşımıyor**. Gövde ya JS ile yükleniyor, ya taranmış
görsel (Elmadağ'da ilan bir PNG), ya da hiç yok. Bir ilan için bir istek daha
harcayıp zaten elde olan başlığı geri almak, nezaket bütçesini boşa yakmak olur.

**Bunun yerine ne var:**

1. **Olduğu gibi kabul.** `spec.md` Bölüm 7 bunu zaten tarif ediyor: tarih
   çıkarılamıyorsa kayıt yine yazılır ve bildirimde "son teklif tarihi
   okunamadı" uyarısı basılır. Mevcut davranış bu ve dürüst olan bu.
2. **EKAP zaten kapatıyor.** Aynı ihale çoğu zaman EKAP'ta da yayımlanıyor ve
   orada İKN'siyle, `fiyatTeklifiSonTarihSaati` alanıyla geliyor (D-12).
   Tekilleştirme (Bölüm 8) ikisini birleştiriyor — 38 kaynağın tarihli olmasının
   sebebi büyük ölçüde bu.
3. **OCR** — taranmış ilan görsellerini okumak. Sıfır maliyet kısıtına ve
   "gereksiz karmaşıklık" ilkesine aykırı; günde birkaç ilan için kurulmaz.

**Ne zaman yeniden bakılır:** Bir kurum ilan gövdesini düz HTML olarak yayımlamaya
başlarsa, o kaynak için `date_selector` zaten yeter — nitekim ODTÜ'de öyle oldu:
tarih özet paragrafının içinde geçiyordu (`"...13/08/2026 saat 14:00 tarihine
kadar..."`), `date_selector: p` ile 10/10 okundu.

---

## D-19 — İki exe bir klasörde: `appsettings.json` tek bir projeye ait olamaz

**Karar:** Worker'ın ayarları `appsettings.worker.json` adında. `Program.cs` onu
açıkça, en son kaynak olarak ekliyor.

**Bulgu — ölçüldü.** `deploy/install-windows.ps1` worker'ı ve api'yi bilerek aynı
`publish/` klasörüne yayınlıyor, çünkü `config/` ve `.env` paylaşılıyor. Ama iki
proje `appsettings.json` adını paylaşamaz: ikinci yayın birincisinin üstüne
yazıyordu. Worker bu yüzden api'nin ayarlarıyla çalışıyordu — kendi log yolunu,
`Application` adını ve saklama süresinin üçte ikisini (30 gün → 14) kaybetmiş
durumdaydı.

Asıl bedeli o değil: **ikisi de aynı dosyaya yazmaya çalışıyordu.** Serilog
dosyayı kilitli bulunca yanına `_001`, `_002` açıyor. 8 Ağustos'ta bir güne ait
yedi log dosyası vardı ve hiçbiri bunu söylemiyordu.

**Sonuç:** `publish/` içinde artık `appsettings.json` (api'nin) ve
`appsettings.worker.json` (worker'ın) yan yana duruyor, iki ayrı log dosyası
yazılıyor, numaralı kopya yok.

---

## D-21 — Yeniden puanlama elle tetiklenir, kelime değişince kendiliğinden değil

**Karar:** Arşivi yeniden puanlayan bir yol var — `rescore` komutu ve Filtre
ekranındaki **Yeniden puanla** düğmesi — ama kelime listesi kaydedilince
kendiliğinden çalışmaz. Basan kişi ne olacağını okuyup onaylar.

**Sorun gerçek ve ölçüldü.** Bir ilan yalnızca ayrıştırıldığı anda puanlanır.
Yani bugün eklenen bir kelime yalnızca yarın gelecek ilanlara işler; arşivdeki
988 ilan artık var olmayan bir listenin puanını taşımaya devam eder. 9 Ağustos
2026'da 26 include terimi eklendi ve önizleme "45 ilan bildirilirdi" dedi;
listede hâlâ 39 vardı ve yeni terimlerin tuttuğu CRM ilanı **0** okuyordu.
Son teklif tarihi üç hafta sonraydı.

Yeniden puanlama sonrası: 988 ilan, 37 puan değişti, 8'i eşiği geçti, 2'si
altına düştü. Düşen ikisi de doğruydu — biri Daikin chiller onarımı, ki
`chiller` exclude'unun eklenme sebebi zaten oydu.

**Neden otomatik değil.** İki sebep, ikisi de tek yönlü:

1. **Bildirim gider.** `ListUnannounced` son teklif tarihine bakmıyor; eşiği
   yeni geçen her ilan Telegram'a düşer. Eşik kaydırıcısını denemek için
   oynatan biri, kaydettiği anda telefonuna bir yığın mesaj alır — bir kısmı
   süresi geçmiş ilanlar için. Denemenin bedeli, denemeyi bırakmaya yeter.
2. **Yazma işi.** 988 satır üzerine yazılır. Kaydet tuşunun bir de bunu
   yapması, tuşun ne yaptığını gizler.

**Neden `reparse` değil.** `reparse` arşivlenmiş sayfaları yeniden okuyup
ilanları baştan kurar; cevabı olduğu soru "parser yanlıştı". `rescore` yalnızca
`notice_score`'a dokunur; cevabı olduğu soru "filtre değişti". İkincisi için
birincisini çalıştırmak, tekilleştirmeyi ve `first_seen_at`'i gereksiz yere
riske atmak olur.

**Kayıtlı listeyle çalışır, ekrandaki taslakla değil.** Uç gövdesinde kelime
listesi almıyor. Sebebi D-16: worker'ın kullandığı liste `setting` tablosunda,
ve arşivle worker'ın farklı listelerde anlaşamaması mümkün olmamalı. Ekranda
kaydedilmemiş değişiklik varken düğme kilitli ve sebebini yazıyor. Bir listeyi
denemek isteyen için zaten önizleme var — o hiçbir şey yazmaz.

**`llm_score`'a dokunulmuyor.** D-03 v1'de onu `null` bırakıyor; dolduğu gün
ucuz filtreyi yeniden çalıştırmak pahalı cevabı silmemeli. Testi var.

**Ne rapor edilir.** En çok yükselenler ve **ayrıca, kesilmeden, eşiğin altına
düşenler.** Yükselenler yükselme miktarına göre sıralı, yani sesi kesilen ilan
listenin sonuna düşüp kırpılırdı — ve bir kelime değişikliğinin kimsenin
bakmadığı yarısı tam olarak orası.

---

## D-22 — Pasife alma tek yönlü kapı değil: karantina

**Karar:** Bir kaynak kapatılırken **neden** kapatıldığı yazılır
(`source.deactivated_reason`), ve sebebi kendiliğinden ortadan kalkabilecek
olanlar günde bir kez yeniden yoklanır. Cevap verirse kendiliğinden açılır ve
Telegram'a haber gider.

**Sorun ölçüldü, tahmin edilmedi.** ANFA A.Ş. 8 Ağustos 2026'da pasife alındı;
sebebi kurumun barındırma hesabının askıya alınmasıydı. O gün yazılan not aynen
şöyle bitiyordu: *"Site geri açıldığında yapılacak iş yalnızca
`is_active = true`."* Site geri açıldı ve **üç gün kimse bakmadı.**

Bakamazdı da. Mekanizma kapalı bir döngüydü:

| Adım | Sonuç |
|---|---|
| `ListActive` yalnız `is_active = true` döner | Kapalı kaynak bir daha çekilmez |
| `consecutive_failures` yalnız başarıyla sıfırlanır | Başarı ancak çekmeyle olur |
| Çekme hiç olmaz | Sayaç 10'da kalır, sonsuza kadar |

Geri dönüşün tek yolu bir insanın fark etmesiydi. 11 Ağustos'ta ANFA'yı bulmak
24 pasif kaynağın Türkçe serbest metin notlarını tek tek okumayı gerektirdi.

**Kural şansa bağlıydı, ayrıca.** ODTÜ (`yitdb.metu.edu.tr`) 10-11 Ağustos'ta
**30 kez** düştü — Beypazarı'yla birebir aynı sayı. ODTÜ yaşadı çünkü hataları
arasına başarı girdi ve sayaç sıfırlandı; Beypazarı'nınkiler aralıksız geldiği
için öldü. İkisi arasındaki fark sorunun ciddiyeti değil, hataların dizilişiydi.

**Sebep kapalı küme, cümle değil.** Sorulmak istenen soru "bunlardan hangisinin
sebebi ortadan kalkmış olabilir" ve `notes` alanına bu sorulamaz.

| `deactivated_reason` | Yeniden yoklanır mı | Neden |
|---|---|---|
| `health_rule` | **evet** | Kimsenin seçmediği tek sebep; 10 hata koydu |
| `site_unreachable` | **evet** | Başkasının sunucusunda, kendiliğinden düzelebilir |
| `manual` | hayır | Sebebi `notes`'ta; sebep verilmemiş bir kapatma trafik planlamamalı |
| `no_tender_list` | hayır | Sayfada ihale listesi yok; bir gecede oluşmaz |
| `robots` | hayır | Bu bizim gözden geçireceğimiz bir şey değil |
| `superseded` | hayır | Dönerse yerine geçen kaynağı mükerrer eder |

**Kurtarma durum koduna değil, ilana bakar.** Park edilmiş alan adı, askıya
alınmış hesap sayfası ve giriş duvarı — üçü de neşeyle 200 döner. Hiçbiri
ihaleye ayrışmaz. En az bir ilan şartı, ANFA'yı `suspendedpage.cgi` servis
ederken geri açmayı da engeller. Alarmın okunmaya değer olmasının sebebi de bu:
"soket açıldı" değil, "tekrar topluyor" der.

**Yavaş: günde bir, beş kaynak.** Her yoklama koleksiyoncunun kullandığı aynı
nezaket kapısından geçen gerçek bir istek. Son teklif saati bu akşam olan bir
ilanın o kapıda, ağustostan beri yok olan bir kaynaktan önceliği var. Buraya
düşüren şartlar da (biten hosting, çözülmeyen DNS, 522 dönen origin) gün
ölçeğinde düzelir; saatlik sormak yalnızca trafiği sabırsız gösterirdi.

**Yoklama arşive yazmaz.** Tek bir soruya cevap arar — bu kaynak canlı mı — ve
toplamayı koleksiyoncuya bırakır. Kaynak rotasyona döndüğü anda normal döngü
zaten alır.

**`quarantine_probed_at` denemeden ÖNCE damgalanır.** Beypazarı'nın DNS'i çözmüyor
ve her deneme exception atıyor; yalnızca başarıda damgalamak onu sonsuza kadar
kuyruğun başında tutar ve her süpürme yalnızca onu denerdi.

**Uygulama:** `011_source_quarantine.sql`, `DeactivationReason`,
`QuarantineSweeper` (Notifier), `QuarantineWorker`, `POST /api/sources/{id}/active`
artık `reason` alıyor. 8 test.

**Güncelleme — 13 Ağustos 2026: arayüz tarafı iki gün eksik kaldı.** Api 11
Ağustos'ta `reason` alanını kazandı ama Dart istemcisi yeniden üretilmedi, yani
Kaynaklar ekranındaki anahtar alanı hiç göndermiyordu. Api boş gelen sebebi
`manual` sayıyor ve `manual` yeniden yoklanmayan üç sebepten biri — **arayüzden
kapatılan her kaynak bu maddenin var olma sebebi olan mekanizmadan sessizce
çıkıyordu.** Hiçbir yerde hata görünmedi; 400 dönmüyordu, çünkü alan zorunlu
değil.

Şimdi anahtar beş sebebi soruyor ve her birinin yanına sonucunu yazıyor
(*yeniden yoklanır* / *bir daha bakılmaz*), sebep seçilmeden istek gitmiyor,
ve kapalı satır sebebini ile son yoklama zamanını gösteriyor. `health_rule`
seçilemiyor: on ardışık hata demek, onu yalnızca izleyici gözlemleyebilir.

Ders, D-16'nın kurduğu düzenin bir maliyeti: **istemci şemadan üretiliyorsa, api
değiştiğinde üretmek işin parçasıdır.** Depodaki `swagger.json` ile canlı
şemayı karşılaştıran bir kontrol bunu ilk gün söylerdi.

**Geriye dönük doldurma 11 Ağustos denetiminden geldi**, kuraldan değil: 19 pasif
kaynağın her biri canlı sayfasına ve tüm arşivine karşı tek tek denetlendi.
`consecutive_failures` üzerinden bir kural yalnızca tek otomatik vakayı tanırdı.
Sonuç: 1 `health_rule`, 2 `site_unreachable`, 13 `no_tender_list`, 3 `superseded`.

**Ayrıca düzeltildi:** `deploy/install-windows.ps1 -Rebuild` api'yi durdurmuyordu.
D-19 gereği iki exe aynı klasöre yayınlanıyor, dolayısıyla Domain, Collector,
Parsers, Scoring ve Notifier dll'lerini paylaşıyorlar. Ayakta duran api hepsini
kilitli tutuyordu; `-Rebuild` hiç çalışamazdı ve hata hiç dokunmadığı beş dll için
MSB3021 diyordu.

---

## D-23 — Portlar 60000'in üstünde, çünkü altındaki her port kapılabilir

**Karar:** API `60080`, PostgreSQL host tarafında `60432`. Konteynerin içi 5432
olarak kalıyor; değişen yalnızca dışarı açılan port.

**Sorun ölçüldü.** 13 Ağustos 2026'da sistem öğleden sonra tamamen durdu. Docker
konteyneri çalışıyor ve "healthy" görünüyordu, Postgres içeride sağlıklıydı, ama
worker'ın her iş kolu 30 saniyede bir `Failed to connect to 127.0.0.1:5432`
diyordu. Konteynerin yapılandırmasında eşleme vardı; Windows tarafında yoktu:

```
ports are not available: exposing port TCP 127.0.0.1:5432 ->
listen tcp4 127.0.0.1:5432: bind: erişim izinlerince izin verilmeyen...
```

Sebebi Hyper-V. `netsh int ipv4 show excludedportrange protocol=tcp` o açılışta
`5342-5441` aralığını rezerve etmişti ve 5432 onun içindeydi.

**Asıl mesele tek bir port değil.** Bu makinede dinamik port aralığı:

```
Start Port      : 1024
Number of Ports : 58977      ->  1024 - 60000
```

Yani **1024 ile 60000 arasındaki her port**, her açılışta Hyper-V/HNS tarafından
kapılabilir. O bantta "güvenli port" diye bir şey yok; 5432'yi 5433'e almak aynı
kumarı bir kez daha oynamak olurdu — nitekim 5433 de aynı rezerve aralığın
içinde.

**Neden `netsh add excludedportrange` değil.** Kalıcı rezervasyon yönetici hakkı
ve `winnat` servisini durdurup başlatmayı gerektiriyor. Bir port çakışmasını
sistemin ağ yığınını yeniden başlatarak çözmek, bu projenin başka bir makineye
taşınmasını da bir kurulum adımına bağlar. Ayrıca 5432 kullanılıyorsa bir sebebi
vardır; sıradaki kişinin de aynı porta ihtiyacı olabilir.

**Neden 60000 üstü.** Dinamik aralığın dışı. 60001-65535 hiçbir açılışta
dinamik olarak rezerve edilemez, çünkü tahsis edilebilir havuz 60000'de bitiyor.
Rakamlar bilerek tanıdık: `60080` api'nin 5080'i, `60432` Postgres'in 5432'si.

**Kural:** Bu portlar `.env` ve `.env.example` içinde, `docker-compose.yml`'ın
varsayılanında, api'nin `appsettings.json`'ında ve arayüzün varsayılan adresinde
aynı anda durur. Birini değiştiren hepsini değiştirir. `1024-60000` arasına geri
çekmek bu maddeyi geçersiz kılar — o bandın tamamı kapılabilir.

**Not:** Kurulu bir arayüzde adres cihazın kendi deposunda saklı; yeni sürüm
eskisinin üstüne yazmaz. Masaüstü, telefon ve tarayıcıda Ayarlar ekranından bir
kez güncellenir.

---

## D-24 — Yedek: D:'ye, veritabanı her gece, dosya arşivi haftada bir

**Karar:** `backup` komutu ve gece 03:00'te çalışan üçüncü bir zamanlanmış görev.
Hedef klasör `setting` tablosunda, yani ayarlardan değiştirilebilir; varsayılan
`D:\ProcurementRadarBackup`.

**Neden şimdi.** D-04 haftalık `tar czf` istemişti ve bunu bir insana bırakmıştı.
**Bir kez alınmadı.** 13 Ağustos 2026'da depo GitHub'a gitti ve kod risk altında
olmaktan çıktı — ama bu projenin biriktirdiği şey kod değil. `spec.md` Bölüm 16
bunu açıkça söylüyor: asıl değer arşivde. Onun tek kopyası bir SSD'deydi.

**Ölçüm kararı verdi:**

| | Boyut |
|---|---|
| `pg_dump -Fc` | **1,1 MB** |
| `data/raw` + `data/doc` | 418 MB |

Veritabanı bir megabayt: kaynak listesi selector'larıyla, 2.386 ilan, puanlar,
`first_seen_at` (mimari kural 3'ün "ürünün bütün değeri" dediği alan) ve ayarlar.
Bu kadar küçük olunca "haftada bir" demek için sebep kalmıyor — her gece.

**Dosya arşivi anlık görüntü değil ayna.** Arşivde hiçbir şey değişmiyor:
`raw_fetch` satırları hiç silinmiyor (D-04, mimari kural 2) ve altındaki dosyalar
bir kez yazılıyor. Dolayısıyla ayna zaten tam yedek; dört haftalık anlık görüntü
aynı 418 MB'ın dört kopyası olurdu ve ikinci tur dakikalar değil saniyeler sürer.
İlk tur ölçüldü: 11.468 dosya, 12 saniye.

**D: aynı fiziksel diskte ve bu dürüstçe yazılıyor.** Makinede tek SSD var; C: ve
D: onun bölümleri. Bu yedek kaza silmesine, bozuk bir migration'a, yanlış bir
SQL'e karşı korur — **disk arızasına karşı korumaz.** Gerçek koruma için hedefi
takılı bir USB diske veya makine dışına almak gerekir; adres ayar olduğu için
o gün tek satır değişir.

OneDrive konuşuldu ve seçilmedi: CLAUDE.md "no cloud account" diyor ve kaynak
listesini üçüncü tarafa yüklemek ayrı bir karar. Kural kalkarsa 1,1 MB'lık
döküm için bedava katman fazlasıyla yeter.

**Neden worker'da değil komutta.** `pg_dump` konteynerin içinde, bu makinede
kurulu değil — yani yedek `docker exec` çağırmak zorunda. Panel zaten komut
çalıştırıyor (D-18) ve dağıtım zaten görev kaydediyor (D-10), dolayısıyla hem
düğme hem gecelik tur bedavaya geliyor; sürekli çalışan worker ise konteyner
çalışma zamanından uzak kalıyor.

**`StartWhenAvailable` bir ayrıntı değil.** D-10 makinenin çoğu zaman kapalı veya
uykuda olduğunu yazıyor. O bayrak olmasa kapak kapalıyken denk gelen her yedek
hiç alınmamış olurdu; onunla kaçan tur makine döner dönmez çalışıyor.

**Boş hedef ve olmayan klasör hata değil.** USB takılı olmayan gün normal bir
gün: komut söyler ve sıfır kodla çıkar. Her gece alarm veren bir görev,
okunmayan bir görevdir.

**Doğrulandı, sadece yazılmadı.** Alınan döküm geçici bir veritabanına geri
yüklendi: 108 kaynak, 2.386 ilan, 2.386 puan, 11.206 arşiv satırı, 9 ayar ve
`first_seen_at` 5 Ağustos'a kadar geri geldi. Denenmemiş yedek yedek değildir.

**Uygulama:** `config/backup.json`, `BackupSettings`, `BackupCommand`,
`ProcurementRadarBackup` görevi, Sistem ekranında düğme, **Ayarlar ekranında
Yedek bölümü**. 5 .NET + 7 Dart testi.

Ekran bölümü sonradan eklendi ve sebebi kayda değer: ayar ilk günden beri
api'den değiştirilebiliyordu, ama "api'den değiştirilebilir" ile
"değiştirilebilir" aynı şey değil — pratikte `curl` demekti. Hedefi
değiştirmek bu ayarın tek varlık sebebi (bugün D:, USB takıldığı gün o),
dolayısıyla ekranı olmayan bir alan yarım iş.

---

## D-25 — Alım türü ve madde kaydediliyor; EKAP'ın yıl boyu gönderdiği veri artık kolonda

**Karar:** `procurement_notice.procurement_type` ve `procedure_code` kolonları
EKAP'ın iki arama yanıtından doldurulur. Türkçe usul adlarını kodlara çeviren
eşleme `config/tr-procedure-codes.json` içinde, yani veri. Kurumların kendi
sayfaları için ikisi de **boş bırakılır** — tahmin edilmez.

**Bulgu — şemada olan, hiç yazılmayan alanlar.** 2.666 ilan üzerinde ölçüldü:

| Alan | Doluluk |
|---|---|
| `title` | %100 |
| `bid_deadline_at` | %89 |
| `description` | %8 |
| `procedure_code` | **%0** |
| `procurement_type` | **%0** |
| `estimated_cost` | %0 |
| `okas_codes` | %0 |

Sondaki ikisi erişilemiyor (aşağıda). Ortadaki ikisi **arşivde duruyordu.**
437 arşivlenmiş EKAP kaleminin 422'si `ihaleUsulAciklama`, 15'i
`maddeDayanagiAdi` taşıyor. İki adapter de bunları zaten okuyup `Description`
alanına düz metin olarak yazıyordu — yani bilgi kaydediliyordu, ama bir insanın
okuyabileceği, bir sorgunun okuyamayacağı biçimde.

**Neden önemli.** `spec.md` Bölüm 0 bu projeyi baştan sona **doğrudan temin**
üzerine kuruyor. Buna rağmen ne arayüzde, ne Telegram'da, ne filtrede bir ilanın
22/d doğrudan temin mi yoksa açık ihale mi olduğu görünüyordu. Akşamları çalışan
üç kişilik bir ekip için bu ayrım kararın kendisi: bir bakanlığın açık ihalesi
ile parasal limit kapsamındaki bir alım aynı iş değil. 14 Ağustos'ta bildirim
gitmiş 69 ilanın 48'i hâlâ cevapsız bekliyordu; karar için gereken bilgi ekranda
yoktu.

**Fixture beklenmedik bir şey söyledi.** İhale aramasının `İhale Usulü: Pazarlık
(MD 21 E)` yazdığı görüldü — yani **bent harfi ihale tarafında da var.**
`spec.md` Bölüm 4'ün `art21f` örneği tam olarak bu. Eşleme parantezli maddeyi
de okuyor; okuyamazsa `negotiated`'a düşüyor, yani EKAP parantezi yazmayı
bırakırsa kolon boşalmıyor, kabalaşıyor.

**Kurumların sayfaları için tahmin yok.** 2.480 ilanın başlığında ipucu arandı:
12'sinde "doğrudan temin", 62'sinde "ihale ilanı" geçiyor — %3. Url'e veya
kaynak adına bakıp tür atamak D-20'nin ölçüp reddettiği şeyin aynısı olurdu.
Boş kalıyor ve arayüz **"Belirtilmemiş (ilanda yazmıyor)"** yazıyor; boş bir
satır, sıradan bir ihale gibi okunurdu.

**Erişilemeyen ikisi ve sebepleri:**

- `okas_codes` — iki EKAP liste yanıtının **hiçbirinde yok.** Almak için ilan
  başına ayrı bir detay isteği gerekir; D-20 tam olarak o fikri ölçüp reddetti.
- `estimated_cost` — EKAP liste yanıtında yok, ve `amount_selector` **90 aktif
  kaynağın sıfırında** ayarlı. `spec.md` Bölüm 10'un mesaj biçimindeki
  `💰 ~180.000 TL` satırı bu yüzden hiç görünmedi. Tablosunda tutar sütunu olan
  kaynaklar için kaynak kaynak yazılabilir; bu ayrı bir iş.

**Dolgu, üzerine yazma değil.** Upsert çakışmada yalnızca `last_seen_at`'i
oynatıyor ve bu kural yerinde duruyor (`first_seen_at` ürünün bütün değeri, spec
Bölüm 8). Ama **kimsenin kaydetmediği bir alan, ilk kaydın kazandığı bir alan
değil.** `FillProcedureAsync` yalnızca `NULL` olanı yazar, dolu olana dokunmaz,
iki kez çalıştırılırsa ikincisinde hiçbir şey yapmaz. İki testi var; biri
dolduran, biri dolduranın var olanı ezmediğini gösteren.

**Sonuç:** 2.673 ilanın 193'ü türünü taşıyor — 138 açık ihale, 49 madde 22/d,
3 belli istekliler, 2 madde 21/e, 1 madde 22/c. Oran düşük (%7) ve dürüst olan
bu: türü söyleyen tek kaynak EKAP, ve EKAP'ın doğrudan temin uygulaması hâlâ
pilot (D-12), yani bu oran zamanla kendiliğinden büyüyecek.

---

### D-25'in yan bulguları — ikisi de mekanizmanın kendisiydi

**1. `reparse` EKAP arşivini hiç yeniden işleyemiyormuş.** Komut adapter
kayıt defterini elle kuruyor ve içine **yalnızca `GenericTableAdapter`**
koyuyordu. Yani `ekap_tender`, `ekap_direct_procurement` ve `json_list`
kaynaklarına ait her arşiv satırı sessizce atlanıyordu.

Mimari kural 2 arşivin var olma sebebini "parser kırılınca düzeltir, arşivi
yeniden işleriz" diye yazıyor. `spec.md` Bölüm 13 EKAP'ı "en kırılgan bileşen"
ilan ediyor. İkisi bir araya gelince ortaya çıkan şey şu: **en çok ihtiyaç
duyacak parser, kuralın erişemediği tek parserdı.** Hiçbir test bunu göremezdi;
`ParsePipelineTests` bir `StubAdapter` kullanıyor, yani kayıt defterinin
gerçekte ne içerdiğini sınayan hiçbir şey yoktu.

Dört adapter de kayıtlı artık. EKAP adapter'ları burada tarayıcı istemiyor:
istemci yalnızca `FetchAsync`'ten çağrılıyor, `Parse` arşivlenmiş yanıt
üzerinde saf bir fonksiyon (D-12) — testlerinin Playwright'sız çalışmasını
sağlayan aynı ayrım.

**2. `reparse` zaten işlenmiş satırı hiç okumuyormuş.** Yalnızca
`is_processed = false` olanları alıyordu, yani "hiç ayrıştırılamamış" satırları.
Bir kere ayrıştırılmış her sayfa elle SQL yazmadan erişilemezdi. D-21 bu komutu
*"arşivlenmiş sayfaları yeniden okuyup ilanları baştan kurar"* diye tarif
ediyordu; etmiyordu.

`--again` bayrağı eklendi, `--source-id` zorunlu: 12.000 satırı bir yazım hatası
yüzünden yeniden işlemek bir saatlik iş ve iyi olan ilanlarda gereksiz çalkantı.
İlk denemede kayda değer bir şey buldu — 131 numaralı kaynağın arşivinde **hiç
kaydedilmemiş 7 ilan** vardı.

**3. Yeni bir ayar eklemek kurulum betiğini kilitliyormuş.** `preflight`,
`setting` tablosunda satırı olmayan her anahtar için **ölümcül** hata veriyordu;
`install-windows.ps1` ise preflight'ı worker'ı başlatmadan önce çalıştırıyor.
Ama tabloyu dolduran şey worker'ın açılışındaki seeder (D-16). Yani yeni bir
ayar eklemek, onu ekleyecek süreci başlatmayı reddeden bir betik demekti —
çıkışı elle satır yazmak olan bir kilit, ki D-16 tam olarak onu engellemek için
var.

Artık: satırı **olmayan** anahtar uyarı ("worker açılışta dosyadan
dolduracak"), satırı olup **dosyadan farklı** olan hâlâ uyarı, satırı olup
**yüklenemeyen** hâlâ ölümcül. Yalnızca "henüz satır yok" zararsız, çünkü seeder
dolu olanın üstüne yazmıyor.

**Uygulama:** `config/tr-procedure-codes.json`, `ProcedureCodes`,
`SettingKeys.ProcedureCodes`, `RawNotice`'a iki alan,
`INoticeRepository.FillProcedureAsync`, `NoticeSql.FillProcedure` +
`RawFetchSql.MarkSourceUnprocessed`, `reparse --again`, api sözleşmelerinde
`procurementType` / `procedureCode`, yeniden üretilmiş Dart istemcisi,
`ui/lib/src/data/procurement_kind.dart`, ilan listesinde ve detayında görünüm.
15 .NET + 9 Dart testi.

---

## D-26 — İlan metni: kaybolan satır sonları geri konur, markdown'a çevrilmez

**Karar:** `notice_document.extracted_text` veritabanında **olduğu gibi** durur.
Okunur hale getirme ekranda, `announcement_outline.dart` ile yapılır: metindeki
numaralandırma okunup satır sonları geri konur. Markdown veya HTML'e
çevrilmez.

**Sorun.** Detay ekranındaki İlan metni binlerce karakterlik tek bir paragraf
olarak duruyordu ve okunmuyordu. Ama metin düzensiz değil — **düzeni ezilmiş.**
Numaralandırma yerinde:

```
1- İdarenin 1.1. Adı : ... 1.2. Adresi : ... 2- İhalenin 2.1. Tarih ve Saati : ...
```

Yani yapılan iş metni biçimlendirmek değil, hâlâ orada duran ama görünmez hale
gelmiş bir dizini okumak. D-15 aynı problemi sonuç ilanı için zaten çözmüştü:
*"bir değer, bir sonraki harf başladığı yerde biter."*

**Neden markdown değil — ölçüldü.** İlk akla gelen fikir buydu ve burada
**aktif olarak yanlış:** saklanan 267 metnin **236'sı `***` içeriyor**, çünkü
`Parse` iletişim adlarını ve telefonları veritabanına girmeden maskeliyor
(CLAUDE.md). Markdown'da üç yıldız kalın+italik açar ve kapanışını arayarak
belgenin geri kalanını yutar. Kaynakta zaten hiç işaretleme yok; bir
render'cı eklemek yalnızca metni yanlış okumanın bir yolunu eklerdi.

**Neden api'de değil ekranda.** D-11'in kuralı: ham olanı sakla, okurken
yorumla. `extracted_text`'i veritabanında yeniden yazmak, saklanan kopyayı
bugünkü "nasıl görünmeli" fikrine bağlardı ve sonraki bir düzeltme zaten
yazılmış olana erişemezdi. Bu değişiklik hiçbir veriye dokunmuyor.

**Bugün ne yaptığıysa ona düşer.** 267 metnin 16'sı EKAP ilanı değil — pdf'ten
çıkarılmış, taranmış bir belediye ilanının içinde dizin yok. İki bölüm
başlığından azını bulursa metni olduğu gibi geri verir, yani ekranın önceki
davranışı.

**Yan bulgu — maskeleme bir sonraki maddenin ilk hanesini yiyor.** Fixture
okunarak bulundu, akıl yürüterek değil. `PersonalDataMasker` iletişim
etiketinden **cümle sonuna** kadar her şeyi siliyor ve cümle ilk noktada
bitiyor — o nokta da bir sonraki maddenin numarasının içindeki nokta:

```
1.3. Telefon: 0312 ... 1.4. İhale dokümanının ...
1.3. Telefon: ***.4. İhale dokümanının ...        <- saklanan
```

Bu belgede iki kez oluyor (`1.4` ve `4.1.2.1`). Numarası kalmayan madde bir
üsttekinin değerinin içinde kalıyor. Dizin bunu **onarmaya çalışmıyor**:
metinde olmayan bir hane geri getirilemez, uydurulan bir hane ise ait
olmadığı bir başlığın altına içerik koyardı. Maskelemeyi düzeltmek ayrı bir
iş — hem maskeleyiciyi hem de 267 belgenin metin çıkarımını yeniden
çalıştırmayı gerektirir, ve fazla maskelemek eksik maskelemekten iyi
(`config/tr-personal-data.json`).

**Uygulama:** `ui/lib/src/data/announcement_outline.dart`,
`ui/test/fixtures/ekap-announcement-2026-1340114.txt` (gerçek ilan, D-09),
detay ekranında `_Announcement` + `_Item`. Tamamı tek bir `SelectionArea`
içinde: seçim maddeler arasında akmalı, çünkü bu ekranda en sık yapılan şey
bir adresi kopyalamak. 13 Dart testi.

---

## D-27 — Bir tur kapatma: yeniden başlatma, boş liste, maskeleme ve log seviyeleri

Bu madde tek bir karar değil, 18 Ağustos 2026'da logdan çıkan dokuz kusurun
kaydı. Ortak noktaları var: **hiçbiri bir testi kırmıyordu.** Sekizi ancak
çalışan sistemin logu okunarak, biri de fixture okunarak bulundu.

### 1. Yeniden başlatma politikası yazılıydı ve çalışmıyordu

Görevde `RestartCount 999`, `RestartInterval 5 dakika` duruyordu. 15 Ağustos'ta
worker 06:06'da öldü ve **24 saat sonra hâlâ ölüydü**; yaklaşık 1.500 sayfa
çekmesi ve bir günlük `first_seen_at` doğruluğu kayboldu.

Windows'un bu ayarı, görev **başlatılamazsa** devreye giriyor. Başlayıp sonra
ölen bir süreç görevi "tamamlandı" bırakıyor ve hiçbir şey yeniden denemiyor.
Bu worker'ı ayağa kaldıran tek şey oturum açılışıydı ve bir gün boyunca oturum
açılmadı.

İlk düzeltme de çalışmadı ve bu kayda değer: tekrar **oturum açılışı
tetikleyicisine** bağlandı, `Get-ScheduledTask` `PT5M` diye geri okudu, ve
hiçbir şey olmadı — bir tekrar, tetikleyicisinin *ateşlenmesine* ait; oturum
günler önce açılmıştı, dolayısıyla tekrarlayacak bir şey yoktu. `NextRunTime`
boştu. Doğru çözüm **ikinci ve zaman tabanlı bir tetikleyici**.

Doğrulama ayarı okuyarak değil, **worker öldürülerek** yapıldı: 174 saniye
sonra kendiliğinden geri geldi.

`MultipleInstances = IgnoreNew` artık açıkça yazılıyor. Varsayılan zaten oydu,
ama beş dakikada bir ikinci bir koleksiyoncu başlatmak tek bir nezaket kapısını
paylaşan iki süreç demek — yazılmamış bir varsayılana dayanacak yer değil.
`-Rebuild` de `Stop` yerine `Disable` kullanıyor: aksi halde yayın ortasında
gelen bir tik paylaşılan beş dll'i yeniden kilitlerdi (D-19, D-22).

### 2. "Parser bozuk" alarmı boş listeyi ayırt edemiyordu

spec Bölüm 11'in kuralı — HTTP 200 + 0 kayıt + hash değişmiş — bir sitenin
yeniden tasarlandığını saatler içinde yakalayan tek kural. Aynı zamanda o hafta
açık ihalesi olmayan bir belediyeyi de yakalıyor ve veritabanından ikisi
birbirinin aynı görünüyor. Kahramankazan üç gün boyunca her gün alarm çaldı;
selector'ları baştan sona doğruydu.

Ayrımı yalnızca adapter yapabilir, çünkü satırların nerede olması gerektiğini
yalnızca o bilir. `ISourceAdapter.ListWasEmpty` varsayılan gövdesiyle eklendi
(`Parse`'ın imzası spec Bölüm 6 ve kırk testten çağrılıyor; sıfır-ilan
durumunda sorulan bir soru her çağıranın aldığı şeyi değiştirmemeli).
`GenericTableAdapter` satır selector'ının son adımını atarak kabı arıyor:
`div.common-list ul li` satırları, `div.common-list ul` içinde durdukları
listeyi tarif eder. Kap var ve satır yoksa sayfa sağlam ve boş; kap da yoksa
sayfa değişmiş ve alarm doğru.

Varsayılan `false` — bilemeyen adapter alarm çalmaya devam eder. Kaçırılan bir
alarmın bedeli spec Bölüm 11'in açılış cümlesi.

Kolon: `012_raw_fetch_empty_list.sql`. Testler kaynağın **kendi arşivlenmiş iki
gövdesine** karşı (D-09): 105 KB içinde tek bir `li` kadar farklılar.

### 3. Şartname metinleri hiç maskelenmiyormuş

Bu turun en ciddisi ve aranarak değil, yolda bulundu. `EkapAnnouncementEnricher`
maskeliyordu; `DocumentDownloader` — indirilen her pdf'in metnini yazan yol —
hiç maskelemiyordu. Ölçüm: saklanan 270 metnin **188'inde e-posta adresi**,
17'sinde telefon görünümlü dizi.

CLAUDE.md ve spec Bölüm 12 bunu açıkça yasaklıyor. Kodu düzeltmek yalnızca
bundan sonrasını durdurur; `remask` komutu diskteki dosyalardan metni yeniden
çıkarıp maskeleyerek geçmişi de kapattı — 210 doküman, e-posta **188 → 0**.
Diskteki baytlara dokunulmadı: onlar arşiv (D-11), maskelenen kolona giden kopya.

### 4. Maskeleme sonraki maddenin ilk hanesini yiyordu

D-26'da bulunmuştu, burada kapandı. Değer "cümle sonuna kadar" gidiyordu ve
cümle bir sonraki madde numarasının içindeki noktada bitiyordu:
`1.3. Telefon: ***.4. İhale`. Desene dar bir koruma eklendi — sonunda nokta
isteyen bir madde numarası görürse durur, `2.5 milyon` gibi ondalık onu erken
kesmez. Erken kesmek maskeyi eksiltir, o yüzden koruma dar olan.

### 5-8. Log seviyeleri: beklenen durum uyarı değildir

D-07 uyarı ve hataları haftalık raporda sayıyor. Dört beklenen durum onları
dolduruyordu:

| Satır | Eskiden | Şimdi | Sebep |
|---|---|---|---|
| EKAP arama zaman aşımı | Error, ~150/gün | ilk denemede Debug | Yeniden deneme çoğu zaman tutuyor; **başarılı bir aramanın ardında Error duruyordu** |
| "ilk deneme sonuç vermedi" | Warning, ~140/gün | Debug | Aynı olayın ikinci yarısı |
| "50 satır isteyemedik, 10 aldık" | Warning, ~150/gün | Debug | EKAP'ın kendi sınırı, D-13'te zaten ölçülü |
| "N satır atlandı, M tutuldu" | Warning, her çekmede | yalnız **hiç tutulmadıysa** Warning | Bazı satır selector'ları geniş (Tailwind `.transition-all`, `.col-md-4`); atlananlar sayfa çerçevesi, ilan değil |
| "tarih okunamadı" | Warning, 211/3 gün | tarih-biçimli değilse Debug | Metin başlıksa bu başarısız ayrıştırma değil, tarih olmayan bir alan |

Hiçbiri gizlenmedi: gerçekten başarısız olan arama hâlâ Error, hepsi atlanan
sayfa hâlâ Warning, okunamayan **tarih** hâlâ Warning ve spec Bölüm 7'nin
haftalık gözden geçirmesine düşüyor.

### 9. Config'i olmayan kaynak arayüzden düzenlenemiyordu

`PUT /api/sources/{id}` generic_table için geçerli config şart koşuyordu —
yani config'i olmadığı için kapalı duran on üç kaynağa config yazmak da, neden
kapalı olduğunu not düşmek de imkânsızdı. 11 Ağustos denetiminde o notlar elle
SQL ile yazılmak zorunda kalmıştı, ki D-16 tam olarak bunu engellemek için var.

Artık **boş** config serbest, **bozuk** config hâlâ 400 ve sebebini yazıyor.
İlk işi 99 ve 101 numaralı kaynakları kurtarmak oldu (aşağıda).

### Yan bulgular

**Kaynak 99'un adresi bir ara sayfaymış.** Karantinada "site erişilemiyor" diye
duruyordu; arşivlenmiş gövdesinde içerik bölümü boş ve yalnızca iki alt sayfaya
link var. Adres doğrudan temin alt sayfasına çekildi, selector'lar kardeş kurum
Bilkent Şehir Hastanesi'nden alındı. 101'in arşivinde tek sayfa yoktu, aynı
şablon yazıldı. **İkisi de karantinada bırakıldı** — günlük yoklama nezaketle
çekecek, en az bir ilan ayrışırsa D-22 kendiliğinden geri açacak. Doğrulamayı
mekanizma yapıyor, biz değil.

**Son teklif tarihi geriye dönük doldurulamıyor — ve sebebi kimliğin kendisi.**
`FillMissingAsync` tarihi de dolduracak şekilde genişletildi, ama bu yalnızca
İKN taşıyan ilanlarda işe yarar: İKN yoksa `external_key` tarihin gününü içerir
(spec Bölüm 8), dolayısıyla tarihsiz kaydedilmiş bir ilan sonradan tarihle
okunduğunda **aynı ilan olarak tanınmaz, ikinci kez yazılır**. ODTÜ'nün 12
tarihsiz ilanı tam olarak bu durumda: selector 9 Ağustos'ta yazıldı, ilanlar
5-7 Ağustos'tan. `reparse` onları düzeltmez, çoğaltır. Bunu yazan bir test var.
Düzeltmek arşivi bir kez daha işlemekle değil, kimlik hakkında bir kararla olur.

**EKAP filtresi yeniden adlandırılmamış.** 582 satır sanılmıştı; ölçünce 561
başarı, 31 başarısızlık çıktı — %5. Panel bazen zamanında çizilmiyor. Bir kez
yeniden deneniyor, mesaj da artık doğru şeyi söylüyor.

**Güncelleme — 21 Ağustos 2026: kural bir kez daha daraltıldı.** Boş liste
ayrımı (madde 2) `list_was_empty` damgası koyabilen adapter'lar için çalıştı,
ama tek adımlı satır selector'ı olan kaynaklarda kap yok, dolayısıyla damga da
yok. Çamlıdere o gün **96 kez 13 ilan okudu ve bir kez 0 döndü**; kural 24
saatlik pencerede tek bir boş ayrıştırma görünce çaldığı için alarm verdi.

Ölçüt değişti: sayılan şey artık **son başarılı okumadan sonraki** boş
ayrıştırmalar ve en az **üçü** gerekiyor. Bir sıçrama başarıların arasında
kalır ve hiç sayılmaz; yeniden tasarlanmış bir site ise şimdiye kadar uzanan
kesintisiz bir boş dizi üretir. On beş dakikalık tarama aralığında tespit ~45
dakika, yani spec Bölüm 11'in istediği "saatler içinde" aralığında.

Geçmişe karşı doğrulandı: yeni kural son 24 saatte **hiç** alarm vermiyor (eski
kural iki tane veriyordu, ikisi de yanlış), ama 16 Ağustos'ta Kahramankazan'ın
gerçek kırılmasını **63 ardışık boş** ile yakalıyor.

**Ölçüldü ve değiştirilmedi:** Gölbaşı'nın `date_selector: span`'ı denenen
alternatiflerin en iyisi (8/10, diğerleri 6/10). ODTÜ'nün config'i arşive karşı
10/10 okuyor. Sanatoryum'un `date_selector: a`'sı doğru — tarih bağlantı
metninin içinde parantezde.
