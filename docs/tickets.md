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
