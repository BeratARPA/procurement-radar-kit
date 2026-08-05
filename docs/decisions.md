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
