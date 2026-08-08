# İş Listesi

Her ticket tek oturumluk, tek PR'lık. Sırayı bozmayın — bağımlılıklar gerçek.

Claude Code'a verirken tek ticket verin: *"T-05'i uygula."*
Tüm listeyi birden vermeyin.

**T-13 atlanacak.** Sıra: T-00 → T-12, sonra doğrudan T-14.

---

## Durum — 6 Ağustos 2026

T-00'dan T-16'ya kadar hepsi bitti; T-13 D-03 gereği açılmadı. Zincirin tamamı
canlı sitelere karşı çalıştı: robots.txt → nezaket kuyruğu → gzip arşiv →
kodlama çözümü → ayrıştırma → tekilleştirme → maskeleme → skorlama.

- 33 aktif kaynak, 25 kaynak "kayıtlı ama pasif" (sebebi her birinin `notes`
  alanında; hiçbiri unutulmuş değil)
- 539 ilan, 324'ünde son teklif tarihi
- Aktif her kaynağın kaydedilmiş sayfası fixture olarak depoda, config'i testle
  korunuyor
- 342 test

Sonuç takibi de artık çalışıyor (D-15): son teklif tarihinin üzerinden iki hafta
geçmiş, İKN'si olan ilanlar altı saatte bir EKAP'a soruluyor ve Sonuç İlanı
yayımlandıysa kazanan, sözleşme bedeli ve teklif sayısı `award` tablosuna yazılıyor.
Telegram'dan `/sonuclar` ile okunuyor.

**Yapılmayan iki şey var ve ikisi de kod değil:**

1. **Telegram token** — `.env` içindeki `PR_TELEGRAM_BOT_TOKEN` ve
   `PR_TELEGRAM_CHAT_ID` boş. Bildirim katmanı yazıldı ve test edildi ama
   gerçek bir bota karşı tek satır mesaj gönderilmedi.
2. **Sürekli çalışma** — bkz. `docs/deployment.md`. Sistem şu an yalnızca elle
   başlatıldığında yaşıyor; `first_seen_at` ancak makine açıkken birikir.

---

## T-00 — Yerel altyapı

**Kapsam:** Repo kökünde `docker-compose.yml` (PostgreSQL 16, volume
`./data/pgdata`), `.env.example`, `.gitignore` (`data/`, `.env`, `*.user`).

**Kabul:** `docker compose up -d` ile PostgreSQL ayağa kalkıyor,
`psql` ile bağlanılıyor. `data/` git tarafından izlenmiyor.

**Bağımlılık:** yok

---

## T-01 — Solution iskeleti

**Kapsam:** .NET 8 solution, proje referansları, `Directory.Build.props`
(nullable enable, warnings as errors), Serilog kurulumu.

**Projeler:**
```
src/ProcurementRadar.Domain
src/ProcurementRadar.Persistence
src/ProcurementRadar.Collector
src/ProcurementRadar.Parsers
src/ProcurementRadar.Scoring
src/ProcurementRadar.Notifier
src/ProcurementRadar.Migrator
src/ProcurementRadar.Worker
src/ProcurementRadar.Cli
tests/ProcurementRadar.Parsers.Tests
tests/ProcurementRadar.UnitTests
```

**Kabul:** `dotnet build` ve `dotnet test` sıfır uyarıyla geçiyor. `Worker`
ayağa kalkıp "started" logluyor ve kapanıyor.

**Bağımlılık:** T-00

---

## T-02 — Şema ve migration

**Kapsam:** `docs/spec.md` Bölüm 4'teki SQL'i `001_initial.sql` olarak
DbUp script'ine çevir. İndeksler dahil.

**Kabul:** `dotnet run --project src/ProcurementRadar.Migrator` boş bir
PostgreSQL'de tüm tabloları ve indeksleri oluşturuyor. İkinci çalıştırmada
hiçbir şey yapmıyor, hata vermiyor.

**Bağımlılık:** T-01

---

## T-03 — Domain tipleri ve persistence

**Kapsam:** Entity record'ları, enum'lar, Dapper repository'leri.
Gereken metotlar: `SourceRepository` (list active, update health fields),
`RawFetchRepository` (insert, list unprocessed, mark processed),
`NoticeRepository` (upsert by external_key, get by status, update status).

**Kabul:** Her repository için birim test, in-memory değil gerçek bir test
şemasına karşı çalışmıyorsa SQL'in en azından derlendiğini doğrulayan smoke test.
`DateTime.Now` kullanımı yok.

**Bağımlılık:** T-02

---

## T-04 — Kaynak seed CLI

**Kapsam:** `config/sources.seed.json` okuyup `source` tablosuna idempotent
yazan CLI komutu. `url` alanı `TODO` olan kayıtlar atlanır ve sayısı loglanır.

**Kabul:** `dotnet run --project src/ProcurementRadar.Cli -- seed` iki kez
çalıştırıldığında kayıt sayısı değişmiyor. TODO'lu kayıtlar atlanıyor.

**Bağımlılık:** T-03

---

## T-05 — HTTP fetch katmanı

**Kapsam:** `IHttpFetcher`. Host başına `SemaphoreSlim`, 5 sn minimum aralık +
0-3 sn jitter, `If-None-Match`/`If-Modified-Since`, Polly ile 3 deneme
exponential backoff, `robots.txt` kontrolü ve önbelleği, gece penceresi
(02:00-05:00 Europe/Istanbul → `poll_interval_minutes` × 3).

**Kabul:** Aynı host'a arka arkaya 3 istek → aralarında en az 5 sn geçtiğini
doğrulayan test (`TimeProvider` sahtelenmiş). 304 dönüşünde gövde indirilmiyor.
`robots.txt` disallow ise `FetchAsync` atlıyor ve loglayıp dönüyor.

**Bağımlılık:** T-01

---

## T-06 — Ham arşiv yazıcı

**Kapsam:** `StoragePathBuilder` (D-04), gzip sıkıştırma, `PR_DATA_DIR`
altına yazma, `raw_fetch` kaydı. `content_hash` önceki kayıtla aynıysa yeni satır
yazılmaz, yalnızca `source.last_fetch_at` güncellenir.

**Kabul:** Aynı içerik iki kez çekildiğinde `raw_fetch` tek satır.
İçerik değişince ikinci satır. Yol şeması D-04'e birebir uyuyor.

**Bağımlılık:** T-03, T-05

---

## T-07 — Adapter sözleşmesi ve GenericTableAdapter

**Kapsam:** `ISourceAdapter` (spec Bölüm 6). Config'ten CSS selector alan
`GenericTableAdapter`: satır selector'ı, başlık, link, tarih, tutar alanları.
Selector konfigürasyonu `source.notes` yerine ayrı bir `adapter_config jsonb`
kolonunda tutulur — bu kolonu T-02'ye ekleyin veya `002_adapter_config.sql`
migration'ı açın.

**Kabul:** En az 3 farklı fixture ile `Parse` doğru sayıda `RawNotice` dönüyor.
`Parse` içinde `HttpClient`, DB veya `DateTime.Now` referansı yok — bunu
doğrulayan bir mimari test yazın.

**Bağımlılık:** T-05

---

## T-08 — TurkishDateParser

**Kapsam:** `config/tr-date-patterns.json` okuyan tarih ayrıştırıcı.
Desteklenecek biçimler dosyada. `Europe/Istanbul` → UTC dönüşümü.
Ayrıştırılamayan girdi `null` döner ve `WARN` loglar.

**Kabul:** Config'teki her desen için en az bir test. Yaz saati sınırında
(son Mart pazarı) doğru UTC üretiliyor. Bilinmeyen format exception atmıyor.

**Bağımlılık:** T-01

---

## T-09 — Normalizasyon ve tekilleştirme

**Kapsam:** `normalize()` (lowercase, Türkçe→ASCII, noktalama temizliği,
config'teki Türkçe ek listesinin atılması), `external_key` üretimi (spec Bölüm 8),
`ikn` varsa doğrudan kullanımı.

**Kabul:** Aynı ilanın iki farklı yazımı aynı `external_key`i üretiyor.
Farklı ilanlar çakışmıyor. Upsert'te `first_seen_at` korunuyor, `last_seen_at`
güncelleniyor — bunu doğrulayan test zorunlu.

**Bağımlılık:** T-03, T-08

---

## T-10 — Parse pipeline

**Kapsam:** `is_processed = false` olan `raw_fetch` satırlarını alıp
storage'dan gövdeyi çekip ilgili adapter ile parse eden ve `procurement_notice`
yazan worker. Kişisel veri maskeleme burada uygulanır (ad-soyad, telefon).

**Kabul:** Uçtan uca: fixture → raw_fetch → notice. `--source-id` ile tek
kaynağı yeniden işleyen CLI komutu çalışıyor. Maskeleme testi var.

**Bağımlılık:** T-06, T-07, T-09

---

## T-11 — Kural tabanlı skorlayıcı

**Kapsam:** `config/keywords.json` yükleyici, include/exclude eşleşmesi,
`okas_codes` doluysa önceliklendirme, `rule_score` hesabı.

**Kabul:** "Bilgisayar alımı" içeren ilan exclude ile eleniyor.
"Yazılım bakım" include ile yüksek skor alıyor. Kod içinde tek bir Türkçe
string yok — bunu doğrulayan grep testi.

**Bağımlılık:** T-10

---

## T-12 — Doküman indirici

**Kapsam:** `document_urls` listesini indirme, storage'a yazma, metin çıkarma.
PDF için PdfPig, DOCX için OpenXML, RAR/ZIP için SharpCompress ile açıp içindeki
dosyaları işleme. Toplam boyut sınırı 50 MB, aşan atlanır ve loglanır.

**Kabul:** Örnek bir PDF ve bir ZIP'ten metin çıkarılıyor. Bozuk dosya
exception atmıyor, `WARN` loglayıp devam ediyor.

**Bağımlılık:** T-10

---

## T-13 — LLM skorlayıcı — ERTELENDİ

D-03 gereği v1'de yapılmayacak. `notice_score.llm_score` kolonu şemada duruyor
ama `null` kalıyor. Günlük ilan sayısı 30'u geçtiğinde bu ticket açılır.

**Bu ticket'ı Claude Code'a vermeyin.** T-12'den sonra doğrudan T-14'e geçin.

---

## T-14 — Telegram notifier

**Kapsam:** Spec Bölüm 10'daki mesaj formatı (metin Türkçe), inline keyboard,
callback handler `procurement_notice.status` güncelliyor. Aynı `notice_id` için
ikinci bildirim yok. `bid_deadline_at` - 6 saat hatırlatması.

**Kabul:** Aynı ilan iki kez bildirilmiyor. "İlgilenmiyoruz" butonu `status`u
`not_relevant` yapıyor. `bid_deadline_at` null ise mesajda uyarı satırı var.

**Bağımlılık:** T-12

---

## T-15 — Sağlık izleme

**Kapsam:** Spec Bölüm 11'deki beş alarm. Haftalık özet raporu (kaç kaynak,
kaç ilan, kaç bildirim, kaç teklif, `WARN`/`ERROR` sayıları).

**Kabul:** HTTP 200 + 0 kayıt + hash değişmiş senaryosu "parser broken"
alarmı üretiyor. 30 gün sessiz kalan kaynak alarm üretiyor.
`consecutive_failures >= 10` kaynağı pasife alıyor.

**Bağımlılık:** T-10

---

## T-16 — EkapAdapter

**Kapsam:** EKAP Doğrudan Temin Arama ve İhale Arama için adapter.
En kırılgan bileşen, en son yapılır. `saidsurucu/ihale-mcp` deposunu referans
alın, gerekiyorsa fork'layın.

**Kabul:** Fixture testleri var. EKAP yapısı değiştiğinde alarm üretiyor
(T-15'teki parser broken kuralı bu adapter için de geçerli).

**Bağımlılık:** T-15

---

# v2 — Arayüz

Sıra gerçek: T-17 olmadan ayar ekranının kaydet tuşu hiçbir şey yapmaz,
T-18 olmadan Flutter veritabanını göremez.

---

## T-17 — Ayar omurgası

**Kapsam:** `setting` (key, jsonb value, updated_at, updated_by) ve
`setting_history` tabloları. `SettingRepository`. `SettingSeeder`: tabloda
karşılığı olmayan her anahtarı `config/*.json` dosyasından doldurur, dolu
olanlara dokunmaz. `SettingProvider`: 30 saniyede bir yalnızca `max(updated_at)`
sorar, değiştiyse ilgili ayarı tazeler ve bağımlıları yeniden kurar.

Taşınan anahtarlar: `keywords`, `messages`, `date_patterns`, `normalization`,
`personal_data`, `award_labels`, `ekap_ui`, ve yeni `politeness` ile `schedule`
(bugün kodda sabit olan aralıklar, batch boyutları, olgunluk günü).

**Kabul:** Eşiği veritabanından değiştirince worker yeniden başlatılmadan yeni
eşiğe göre bildirim üretiyor — bunu doğrulayan test zorunlu. Boş bir
veritabanında seed dosyalardan doluyor, ikinci çalıştırmada üzerine yazmıyor.
Her değişiklik `setting_history`'de.

**Bağımlılık:** yok

---

## T-18 — HTTP API

**Kapsam:** `src/ProcurementRadar.Api`, ASP.NET Core minimal API. Uçlar:
ilanlar (liste, filtre, detay, durum güncelleme), kaynaklar (liste, ekle,
düzenle, sağlık), ayarlar (oku, yaz, geçmiş), sonuçlar, sağlık/alarm, ve
filtre önizleme (`POST /api/filter/preview` — verilen kelime listesi ve eşikle
arşivin kaçı bildirilirdi). `PR_API_TOKEN` başlığı zorunlu. Nezaket ayarlarında
alt sınırlar burada zorlanır.

**Kabul:** Token'sız istek 401. 5 saniyenin altında bir bekleme veya kapalı
robots kabul edilmiyor — testi var. Önizleme ucu veritabanını değiştirmiyor.

**Bağımlılık:** T-17

**Durum:** Bitti. Uçlar, token, filtre önizleme, kaynak listeleme/açma-kapama ve
nezaket alt sınırları çalışıyor ve canlı doğrulandı. Kaynak *ekleme* ucu T-21'e
bırakıldı: selector'ları kaydedilmiş sayfaya karşı test etmeden yeni kaynak
eklemek, üç gün gözlem kuralını (spec Bölüm 5) atlamak olur.

---

## T-19 — Flutter iskeleti (Web)

**Kapsam:** `ui/` altında tek kod tabanı. Riverpod + go_router, duyarlı yerleşim
(dar ekranda alt gezinme, geniş ekranda yan ray), API istemcisi, token saklama,
karanlık/aydınlık tema. İlk ekran: ilan listesi + detay + durum butonları.

**Kabul:** `flutter run -d chrome` ile açılıyor, telefonun tarayıcısından ev
ağı üzerinden de açılıyor ve iki yerleşim de kullanılabilir.

**Bağımlılık:** T-18

**Durum:** Bitti. `ui/` altında tek kod tabanı, `lib/api/` tamamen
`swagger.json`'dan üretiliyor (bkz. `ui/README.md`). Ekranlar: ilan listesi,
ilan detayı + durum butonları, sistem durumu, ayarlar. Dar ekranda alt gezinme,
geniş ekranda yan ray. 18 test, sürüm derlemesi gerçek tarayıcıda gerçek api'ye
karşı doğrulandı.

**Api tarafında üç değişiklik gerekti** — üçü de bu ekranlar olmadan
görülemezdi:

1. `GET /api/notices/by-id/{id}` eklendi. Detay ucu yalnızca İKN ile
   çalışıyordu, ama İKN'yi yalnızca EKAP veriyor: 924 ilanın **814'ünde İKN
   yok**. Liste zaten İKN döndürmüyordu, yani listeden detaya geçmek mümkün
   değildi. Telegram için doğru olan anahtar bir liste için yanlış anahtardı.
2. `CommandNotice`'a `authorityName`, `NoticeDetail`'e `sourceUrl` ve `status`
   eklendi. Elli satırlık bir listede idare adı olmadan iki ilan ayırt
   edilmiyor; durumu bilmeyen durum butonları hangisinin basılı olduğunu
   gösteremiyor.
3. `SupportNonNullableReferenceTypes` açıldı ve rotalara `Name` verildi.
   İkisi de yalnızca üretilmiş istemcinin kalitesini belirliyor: birincisi
   olmadan C#'ta null olamayan `title` bile Dart'a `String?` geliyor,
   ikincisi olmadan metot adı `apiNoticesByIdIdGet` oluyor.

Kelime listesi, eşik ve kaynak ekranları hâlâ yok — T-20 ve T-21.

---

## T-20 — Filtre ve ayar ekranları

**Kapsam:** Kelime ekle/sil/ağırlıkla, eşiği kaydır ve **canlı önizleme**:
"bu ayarla arşivdeki N ilanın M'si bildirilirdi, şunlar". Ayar ekranı:
aralıklar, Telegram, süpürme, nezaket (alt sınırlar görünür ve gerekçeli).

**Kabul:** Eşik değişikliği kaydedildikten sonra worker yeniden başlatılmadan
etkili oluyor. Önizleme gerçek arşiv üzerinden sayı veriyor.

**Bağımlılık:** T-19

**Durum:** Filtre ekranı bitti. Kelime ekle/sil/ağırlıkla, eşiği kaydır, ve
yanında canlı önizleme: geniş ekranda sebep solda, sonuç sağda.

İki kabul kriteri de canlı sistemde ölçüldü:

- **Önizleme gerçek arşivden sayı veriyor.** 966 ilan üzerinde aynı kelime
  listesiyle: eşik 25 → 55 bildirim, eşik 30 → 39, eşik 60 → **3**. D-14'ün
  "eşik 60 iken bir tesadüf bekleniyordu" gerekçesi burada rakamla görünüyor.
- **Worker yeniden başlatılmadan etkileniyor.** Eşik api üzerinden 31 yapıldı,
  worker'ın PID'i değişmeden logda `Settings refreshed: 6 of 6 reloaded` çıktı,
  sonra 30'a geri alındı. `setting_history` her iki adımı da tuttu.

**Ekranın göstermediği ayarlar korunuyor.** `keywords` tek bir json belgesi ve
bu ekran onun üç anahtarını biliyor; `okas` listeleri, notlar ve T-13'ten kalan
iki ölü eşik olduğu gibi taşınıyor. Bunu doğrulayan test gerçek
`config/keywords.json` üzerinde çalışıyor — aksi halde biri eşiği kaydırdığında
okas yapılandırması sessizce silinirdi ve hiçbir şey bozuk görünmezdi.

**Nezaket ayarları da bitti.** Ayarlar ekranında: host aralığı, jitter, zaman
aşımı, deneme sayısı, gece penceresi ve `robots.txt`. Alt sınırlar hem görünür
hem gerekçeli — gerekçeleri uydurulmadı, `config/politeness.json` içindeki
Türkçe notlardan okunuyor, yani ayarı ayarlayan kişinin kendi cümlesi.

`robots.txt` anahtarı kilitli bir switch olarak duruyor: gizlemek kuralı
görünmez yapardı, çalışan bir switch ise yalan olurdu.

Sunucunun da tuttuğu doğrulandı — ekran değil, asıl kilit orada:

| Denenen | Sonuç |
|---|---|
| `minimum_host_interval_seconds = 4` | 400, *"cannot be below 5 (CLAUDE.md)"* |
| `respect_robots = false` | 400, *"robots.txt is obeyed"* |
| `night_interval_multiplier = 0` | 400, *"cannot be below 1"* |
| meşru bir değişiklik | 204 |

**Yapılmayan ve sebebi:** Telegram anahtarı `.env`'de ve bilerek ekranda değil —
bir parolayı ekrana koymak onu tarayıcının deposuna koymak demek. Tarama
aralıkları ve süpürme henüz ayar değil, kodda sabit; T-17 bir `schedule`
anahtarı planlamıştı ama açılmadı, ve olmayan bir ayarı düzenleyen ekran
yapılmadı. Ayarlar ekranı bu ikisini de yazıyor.

---

## T-21 — Kaynak yönetimi ekranı

**Kapsam:** Kaynak ekleme/düzenleme, adapter seçimi, CSS selector alanları ve
**kaydedilmiş sayfaya karşı test** ("bu ayarla ne bulurdu" — `check-source`
komutunun ekran hali). Sağlık, hata sayacı, pasife alma.

**Kabul:** Yeni bir kaynak tek satır SQL yazmadan eklenip test edilebiliyor.

**Bağımlılık:** T-19

---

## T-22 — Sonuçlar ve masaüstü/Android derlemeleri

**Kapsam:** Sonuç ekranı (kazanan, sözleşme/tahmin oranı, kurum bazlı geçmiş).
Aynı koddan Windows masaüstü ve Android derlemesi, kurulum notları.

**Kabul:** Üç platformda da aynı ekranlar çalışıyor.

**Bağımlılık:** T-20, T-21

**Durum: Windows kısmı erken indi.** Sıra bozulmadı, sebebi ayrı bir talep:
sistemi (Docker, worker, api, CLI, otomatik başlatma) yönetecek bir masaüstü
uygulaması istendi ve bu tarayıcıda mümkün değil — süreç başlatmak, `docker`'a
dokunmak ve zamanlanmış görev okumak tarayıcı sınırının dışında.

Ayrı bir uygulama yerine aynı kod tabanına Windows hedefi eklendi ve yalnızca
masaüstünde görünen bir **Sistem** ekranı yazıldı (D-18). Web derlemesi
etkilenmedi; `dart:io` koşullu import'un arkasında.

Bu ekranın yazılması iki şeyi ortaya çıkardı, ikisi de düzeltildi:

1. **`publish/` kaynaktan eskiydi ve bunu söyleyen hiçbir şey yoktu.** Portta
   cevap veren api `ProcurementRadarApi` görevinin başlattığı eski kopyaydı,
   `authorityName` alanı onda yoktu, arayüz "api'ye ulaşılamadı" diyordu — oysa
   api ayakta ve 200 dönüyordu. Panel artık bunu bir uyarı olarak gösteriyor,
   arayüz de sürüm uyuşmazlığını ağ hatasından ayırıyor.
2. **İki exe bir `appsettings.json`'ı paylaşamaz** (D-19). Worker api'nin
   ayarlarıyla çalışıyor, ikisi aynı log dosyasına yazmaya çalışıyordu.

Geriye kalan: sonuç ekranı ve Android derlemesi.
