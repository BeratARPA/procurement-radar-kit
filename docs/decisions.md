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

**Erişim: şimdilik ev ağı.** API `http://0.0.0.0:5080` dinler, telefondan
`http://<makine-ip>:5080` ile açılır. Dışarıdan erişim (Tailscale veya
Cloudflare Tunnel) sonraya bırakıldı ve API'de değişiklik gerektirmiyor.
Yine de istek başlığında tek bir statik token aranır (`PR_API_TOKEN`) —
kullanıcı hesabı değil, ev ağındaki başka bir cihazın yanlışlıkla ayar
değiştirmesine karşı.

**iOS yok.** Derlemesi Mac istiyor, ekipte Mac yok. Safari'den web sürümü
açılır ve ana ekrana eklenir; bu bir eksiklik, gizlenmiyor.

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
