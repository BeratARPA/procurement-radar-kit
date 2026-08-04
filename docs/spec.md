# Procurement Radar — Teknik Spesifikasyon v1.1

**Durum:** taslak
**Hedef:** Ankara'daki kamu kurumlarının doğrudan temin ve küçük ölçekli bilişim alımlarını, ilan edildikleri andan itibaren 15 dakika içinde yakalayıp değerlendirilebilir hale getirmek.

---

## 0. Neden bu proje

Doğrudan temin duyurularının yayınlanması idarenin takdirinde. Yayınlananların bir kısmı EKAP'ın Doğrudan Temin Arama sayfasında, büyük kısmı kurumların kendi web sitelerinde, her biri farklı formatta. Duyuru ile son teklif saati arasında yasal asgari süre 24 saat.

Bu üç gerçek birleşince ortaya şu çıkıyor: **ilanı geç gören teklif veremez.** Rakiplerin çoğu bu ilanları elle, tesadüfen buluyor. Sistematik tarama tek başına belirleyici bir avantaj.

Proje bir ürün değil, iç araç. Ölçüsü kullanıcı sayısı değil, **teklif verilen iş sayısı.**

---

## 1. Dil ve isimlendirme kuralları

Bu bölüm pazarlığa kapalı. Projede karışıklığın en hızlı kaynağı budur.

| Katman | Dil | Gerekçe |
|---|---|---|
| Şema, tablo, kolon, enum değerleri | **English** | Kod tabanının ortak dili |
| Sınıf, metot, değişken, dosya, klasör adları | **English** | |
| Config anahtarları, environment değişkenleri | **English** | |
| Log mesajları | **English** | Grep edilebilirlik, araç uyumu |
| Commit mesajları, branch adları, PR başlıkları | **English** | |
| Kod içi yorumlar | **English** | |
| Bu doküman ve karar kayıtları | Türkçe | İç doküman, üç kişilik Türk ekip |
| Telegram bildirim metinleri | Türkçe | Son kullanıcı sizsiniz |
| Anahtar kelime / filtre listeleri | **Türkçe** | Kaynak metin Türkçe — bu veri, kod değil |
| LLM prompt talimatları | English | Girdi verisi Türkçe kalır |

**Değiştirilmeyecek özel adlar:** `EKAP`, `IKN`, `OKAS`, `KIK`, `SGK`. Bunlar çeviri değil, kimlik. `ikn` kolonu `tender_registration_number` olmaz.

### Domain sözlüğü (TR → EN)

Parser yazarken Türkçe etiketi görüp İngilizce alana yazacaksınız. Eşleme tek bir yerde tanımlı olsun:

| Türkçe (kaynak metinde geçen) | Kod içindeki karşılığı |
|---|---|
| İdare / İhaleyi yapan idare | `authority_name` |
| İşin adı / İhale konusu | `title` |
| İşin tanımı / Niteliği | `description` |
| Doğrudan temin | `direct_procurement` |
| İhale | `tender` |
| İstisna kapsamı | `exempt` |
| Yaklaşık maliyet | `estimated_cost` |
| Son teklif verme tarihi | `bid_deadline_at` |
| İhale tarihi / Açılış | `opening_at` |
| İdari şartname / Teknik şartname | `document` |
| Zeyilname | `addendum` |
| Sonuç ilanı / Sözleşme bedeli | `award` |
| İhale Kayıt Numarası | `ikn` |

---

## 2. Kapsam

### v1'de var
- Kaynak kayıt defteri ve zamanlanmış tarama
- Kurum bazlı parser adapter mimarisi
- Ham içerik arşivi (yeniden ayrıştırılabilir)
- Normalizasyon, tekilleştirme, ilk-görülme zamanı
- Kural + LLM tabanlı filtreleme ve skorlama
- Şartname/doküman otomatik indirme ve arşivleme
- Telegram bildirimi (aksiyon butonlu)
- Sağlık izleme ve sessiz hata alarmı
- Sonuç takibi (kim kazandı, kaça)

### v1'de yok
- Kullanıcı yönetimi, çok kiracılı yapı, ödeme
- Web/mobil arayüz (v2)
- Otomatik teklif hazırlama
- Türkiye geneli kapsam (önce Ankara)

---

## 3. Mimari

Dört bağımsız süreç. Aralarındaki tek bağ veritabanı.

```
[Scheduler] -> [Collector] -> raw_fetch (archive)
                                  |
                             [Parser] -> procurement_notice
                                  |
                             [Scorer] -> notice_score
                                  |
                             [Notifier] -> Telegram
```

**Kritik tasarım kararı:** ağ erişimi (Collector) ile ayrıştırma (Parser) kesin olarak ayrılır. Collector ham gövdeyi object storage'a yazar, Parser dosyadan okur. Sonuç:

- Parser saf fonksiyon olur → fixture ile unit test edilir
- Bir kurumun HTML'i değişip parser kırıldığında, düzeltip **geçmiş arşivi yeniden işlersiniz** — kaybettiğiniz veri geri gelir
- Parser geliştirirken sunucuya tek istek atmazsınız

Bu ayrımı yapmayan her scraper projesi 3 ay sonra veri kaybıyla ölür.

---

## 4. Veri modeli

```sql
-- A single page to be polled
CREATE TABLE source (
    id                    bigserial PRIMARY KEY,
    authority_name        text        NOT NULL,
    authority_type        text        NOT NULL,   -- university | municipality | provincial_directorate
                                                  -- | hospital | soe_subsidiary | ekap
    province              text        NOT NULL,
    district              text,
    url                   text        NOT NULL,
    adapter_key           text        NOT NULL,   -- which parser class handles this
    poll_interval_minutes int         NOT NULL DEFAULT 15,
    is_active             boolean     NOT NULL DEFAULT true,
    robots_checked_at     timestamptz,
    last_fetch_at         timestamptz,
    last_success_at       timestamptz,
    last_new_notice_at    timestamptz,
    consecutive_failures  int         NOT NULL DEFAULT 0,
    notes                 text
);

-- Raw record of every fetch. Never deleted.
CREATE TABLE raw_fetch (
    id             bigserial PRIMARY KEY,
    source_id      bigint      NOT NULL REFERENCES source(id),
    fetched_at     timestamptz NOT NULL,
    http_status    int,
    etag           text,
    last_modified  text,
    content_hash   text        NOT NULL,          -- sha256, change detection
    body_path      text        NOT NULL,          -- object storage key
    parser_version text,
    is_processed   boolean     NOT NULL DEFAULT false
);

-- Normalized notice
CREATE TABLE procurement_notice (
    id               bigserial PRIMARY KEY,
    source_id        bigint      NOT NULL REFERENCES source(id),
    raw_fetch_id     bigint      NOT NULL REFERENCES raw_fetch(id),
    external_key     text        NOT NULL,        -- IKN if present, otherwise computed hash
    ikn              text,                        -- Turkish tender registration number
    authority_name   text        NOT NULL,
    title            text        NOT NULL,
    description      text,
    procurement_type text,                        -- direct_procurement | tender | exempt
    procedure_code   text,                        -- art22d | art21f | open | restricted
    okas_codes       text[],                      -- Turkish equivalent of CPV codes
    estimated_cost   numeric,
    currency         char(3)     NOT NULL DEFAULT 'TRY',
    bid_deadline_at  timestamptz,                 -- MOST CRITICAL FIELD
    opening_at       timestamptz,
    source_url       text        NOT NULL,
    first_seen_at    timestamptz NOT NULL,        -- this is your competitive edge, never drop it
    last_seen_at     timestamptz NOT NULL,
    status           text        NOT NULL DEFAULT 'new',
                                                  -- new | reviewed | bid_submitted | missed | not_relevant
    UNIQUE (external_key)
);

CREATE TABLE notice_document (
    id             bigserial PRIMARY KEY,
    notice_id      bigint NOT NULL REFERENCES procurement_notice(id),
    url            text   NOT NULL,
    file_name      text,
    mime_type      text,
    size_bytes     bigint,
    storage_path   text,
    downloaded_at  timestamptz,
    extracted_text text                           -- from pdf / docx / rar contents
);

CREATE TABLE notice_score (
    notice_id         bigint PRIMARY KEY REFERENCES procurement_notice(id),
    rule_score        int,
    llm_score         int,
    rationale         text,
    estimated_man_days int,
    risk_note         text,
    model             text,
    scored_at         timestamptz
);

CREATE TABLE notification (
    id         bigserial PRIMARY KEY,
    notice_id  bigint NOT NULL REFERENCES procurement_notice(id),
    channel    text   NOT NULL,                   -- telegram
    sent_at    timestamptz,
    status     text
);

CREATE TABLE award (
    notice_id       bigint PRIMARY KEY REFERENCES procurement_notice(id),
    winner_name     text,
    contract_amount numeric,
    bid_count       int,
    awarded_at      timestamptz
);

CREATE INDEX ix_notice_deadline ON procurement_notice (bid_deadline_at)
    WHERE status = 'new';
CREATE INDEX ix_notice_first_seen ON procurement_notice (first_seen_at DESC);
CREATE INDEX ix_raw_fetch_unprocessed ON raw_fetch (source_id, fetched_at)
    WHERE is_processed = false;
```

`first_seen_at` alanını silmeyin. Altı ay sonra "biz bu ilanı rakiplerden kaç saat önce gördük" sorusunun cevabı orada.

---

## 5. Kaynak kayıt defteri

Projenin gerçek işi burada. Kod değil, veri toplama.

### Hedef liste (Ankara, ~80 kaynak)

| Grup | Adet | `authority_type` | Not |
|---|---|---|---|
| Devlet üniversiteleri | ~10 | `university` | Rektörlük + İdari Mali İşler + Bilgi İşlem sayfaları ayrı olabilir |
| Büyükşehir + ilçe belediyeleri | 26 | `municipality` | |
| İl müdürlükleri | ~15 | `provincial_directorate` | Sağlık, MEB, Tarım, Çevre-Şehircilik, Kültür-Turizm, Gençlik-Spor |
| Kamu hastaneleri | ~10 | `hospital` | Çoğu İl Sağlık Müdürlüğü üzerinden ilan veriyor |
| Belediye iştirakleri | ~6 | `soe_subsidiary` | BELKA, ANFA, EGO, ASKİ vb. |
| EKAP Doğrudan Temin Arama | 1 | `ekap` | Tüm Türkiye, il filtresiyle |
| EKAP İhale Arama | 1 | `ekap` | Şimdilik sadece pazar istihbaratı için |

**Vakıf üniversitelerini listeye koymayın.** Kamu İhale Kanunu kapsamında değiller; onlara satış yapılır ama bu tarayıcının konusu değil.

### Kaynak ekleme prosedürü
1. Kurumun sitesinde "İhale", "Duyuru", "Doğrudan Temin", "Teklife Davet" başlıklarını ara
2. Sayfanın HTML'ini kaydet, tablo/liste yapısını incele
3. `robots.txt` kontrol et, `robots_checked_at` alanına tarih yaz
4. Adapter yaz veya `GenericTableAdapter`'a config ile bağla
5. 3 gün gözlem, sonra `is_active = true`

Kaynakların %70'i "tarih + başlık + link" üçlüsünden oluşan basit bir liste. Bunlar için **tek bir konfigüre edilebilir `GenericTableAdapter`** yazın (CSS selector'lar konfigde). Sadece geri kalan %30 için özel sınıf yazın.

---

## 6. Collector

```csharp
public interface ISourceAdapter
{
    string Key { get; }

    // Network access only. Returns the raw body, parses nothing.
    Task<FetchResult> FetchAsync(Source source, CancellationToken cancellationToken);

    // Pure function. No network, no database. Unit-testable against fixtures.
    IReadOnlyList<RawNotice> Parse(string body, Source source);
}

public sealed record FetchResult(
    int HttpStatus,
    string? Body,
    string? ETag,
    string? LastModified,
    string ContentHash);

public sealed record RawNotice(
    string Title,
    string SourceUrl,
    string? AuthorityName,
    string? Description,
    DateTimeOffset? BidDeadlineAt,
    decimal? EstimatedCost,
    IReadOnlyList<string> DocumentUrls);
```

### Nezaket kuralları (pazarlık konusu değil)
- Host başına aynı anda tek istek (`SemaphoreSlim` per host)
- Aynı host'a istekler arasında minimum 5 saniye + 0–3 sn rastgele jitter
- `ETag` / `If-Modified-Since` gönder, 304 dönerse gövdeyi indirme
- User-Agent'ta proje adı ve iletişim e-postası olsun
- `robots.txt` engelliyorsa o kaynağı listeden çıkar
- 02:00–05:00 arası `poll_interval_minutes` değerini üçe katla (bakım pencereleri)

Bunları atlarsanız IP'niz banlanır ve proje ölür. Kazanılacak hız da yok — 15 dakikalık aralık zaten fazlasıyla yeterli.

### Değişim tespiti
`content_hash` önceki kayıtla aynıysa parser'ı hiç çalıştırma. Sayfaların çoğu günlerce değişmez.

---

## 7. Parser

Her adapter için `tests/ProcurementRadar.Parsers.Tests/fixtures/{adapter_key}/` altında gerçek HTML örnekleri tutulur. Parser değişikliği fixture testlerini geçmeden merge edilmez.

### Zorunlu alanlar
- `title` — her zaman var
- `source_url` — her zaman var
- `bid_deadline_at` — **en önemlisi.** Çıkaramıyorsanız kaydı yine yazın ama bildirimde "son teklif tarihi okunamadı" uyarısı basın
- `authority_name`, `estimated_cost`, `document_urls` — best effort

### Tarih ayrıştırma
Türkçe tarih formatları tutarsız: `12.03.2026`, `12/03/2026 saat 10:00`, `12 Mart 2026 Perşembe`, `12.03.2026 tarihinde saat 10:00'a kadar`.

Ayrı bir `TurkishDateParser` sınıfı yazın. Ay adları ve regex katalogu **veri** olarak dışarıda dursun (`config/tr-date-patterns.json`), sınıf İngilizce. Ayrıştırılamayan her formatı `WARN` seviyesinde logla ve haftalık gözden geçir.

---

## 8. Tekilleştirme

Aynı ilan hem EKAP'ta hem kurumun sitesinde çıkabilir. `ikn` varsa iş kolay: `external_key = ikn`. Yoksa:

```
external_key = sha256(
    normalize(authority_name) + "|" +
    normalize(title) + "|" +
    bid_deadline_at.Date.ToString("yyyy-MM-dd")
)
```

`normalize()`: lowercase, Türkçe karakterleri ASCII'ye indir, noktalama ve çoklu boşlukları kaldır, yaygın Türkçe ekleri at (`işi`, `alımı`, `hizmeti`, `hizmet alımı`). Bu ek listesi de config'te durur, kodda gömülü olmaz.

Çakışma olursa **ilk kaydı koru**, sonrakinde sadece `last_seen_at` güncelle.

---

## 9. Skorlama

### Aşama 1 — rule-based (ucuz, hızlı, önce bu çalışır)

Kelime listeleri Türkçe kalır çünkü kaynak metin Türkçe. Bunlar veri; `config/keywords.json` içinde tutulur, kodda hardcode edilmez.

```json
{
  "include": ["yazılım", "web sitesi", "web tasarım", "mobil uygulama",
              "otomasyon", "bilgi sistemi", "portal", "entegrasyon",
              "veri tabanı", "yazılım bakım", "yazılım destek",
              "lisans yenileme", "dijital dönüşüm", "api"],
  "exclude": ["bilgisayar alımı", "dizüstü", "toner", "kartuş", "yazıcı",
              "sunucu donanımı", "switch", "güvenlik kamerası",
              "kamera sistemi", "ağ altyapısı", "kablolama"]
}
```

`exclude` listesi olmadan bildirimlerinizin %80'i donanım alımı olur ve iki haftada Telegram'ı sessize alırsınız. **Filtre kalitesi, kapsamdan daha önemli.**

`okas_codes` doluysa kelime eşleşmesinden önce onu kullanın — çok daha güvenilir.

### Aşama 2 — LLM (yalnızca rule filtresini geçenler için)

Prompt talimatları İngilizce, girdi verisi Türkçe:

```
You are screening Turkish public procurement notices for a 3-person
full-stack software team (.NET, Flutter). They work evenings and
weekends and cannot take on projects longer than 3 months.

Return ONLY valid JSON, no markdown fences:
{
  "score": 0-100,
  "rationale": "one sentence, Turkish",
  "estimated_man_days": number,
  "risk_note": "unclear_scope | hardware_mixed | deadline_too_short | none"
}
```

Girdi: `title` + `description` + varsa `extracted_text` ilk 8000 karakter.

---

## 10. Bildirim

Telegram. Metin Türkçe — okuyan sizsiniz.

```
🟢 84/100 — Yazılım Bakım ve Destek Hizmeti Alımı
🏛 Çankaya Belediyesi
💰 ~180.000 TL
⏰ Son teklif: 14 Mart 14:00 (kalan 31 saat)
📎 Şartname indirildi (2 dosya)

Mevcut personel takip sistemine modül eklenmesi.
Kapsam net, .NET tabanlı, tahmini 12 adam-gün.

[İncele] [İlgilenmiyoruz]
```

**Kurallar:**
- Skor eşiğinin altındakiler bildirim üretmez ama veritabanına yazılır
- Aynı `notice_id` için ikinci bildirim yok
- `bid_deadline_at`'e 6 saat kala ve `status = 'new'` ise tek bir hatırlatma
- Butonlar `procurement_notice.status` alanını günceller — bu veri sonradan filtre kalitesini ölçmek için kullanılır

---

## 11. Sağlık izleme

En tehlikeli hata, sistemin çökmesi değil; **sessizce boş dönmesi.** HTTP 200 alırsınız, parser 0 kayıt döner, hiçbir alarm çalmaz, üç ay boyunca hiçbir ilan görmezsiniz.

Zorunlu alarmlar:

| Koşul | Aksiyon |
|---|---|
| 3 kez üst üste HTTP hatası | Telegram uyarısı, `consecutive_failures++` |
| `consecutive_failures >= 10` | `is_active = false`, manuel inceleme iste |
| HTTP 200 + parser 0 kayıt + `content_hash` değişmiş | **Parser broken** uyarısı |
| Geçmişte ayda ≥1 ilan varken 30 gündür `last_new_notice_at` güncellenmemiş | Silent source uyarısı |
| Son 24 saatte sistem genelinde 0 yeni kayıt | Kritik alarm (hafta içi normal değil) |

Haftalık özet: kaç kaynak tarandı, kaç yeni ilan, kaç bildirim, kaç teklif, filtre isabet oranı.

---

## 12. Sınırlar

- İlan verisi kamuya açık; sorun veriden değil trafik hacminden çıkar. Nezaket kurallarına uyun.
- `robots.txt` ve site kullanım şartlarına bakın, `source.robots_checked_at` alanına tarih atarak kaydedin.
- Kişisel veri toplamayın. İlanlarda geçen görevli ad-soyad ve telefonlarını veritabanına yazmayın, `Parse()` içinde maskeleyin.
- Arşivlediğiniz şartnameleri üçüncü kişilerle paylaşmayın; bu araç iç kullanım için.

---

## 13. Teknoloji ve proje yapısı

| Katman | Seçim | Gerekçe |
|---|---|---|
| Worker | .NET 8 Worker Service | Ekip zaten .NET biliyor; öğrenme maliyeti sıfır |
| HTML parsing | AngleSharp | CSS selector desteği, HtmlAgilityPack'ten temiz |
| JS-rendered pages | Playwright (yalnızca gerektiğinde) | Kaynakların ~%10'u |
| Database | PostgreSQL (Supabase) | Ekip Supabase biliyor, ücretsiz katman yeter |
| Raw body archive | Supabase Storage | Gzip'li HTML, aylık klasörleme |
| Scheduling | `PeriodicTimer` (worker içinde) | Ayrı scheduler'a gerek yok |
| Notification | Telegram Bot API | Bedava, anlık, inline keyboard var |
| Hosting | Tek küçük VPS | Aylık birkaç dolar |

```
src/
  ProcurementRadar.Domain/          # entities, enums, value objects
  ProcurementRadar.Persistence/     # EF Core / Dapper, migrations
  ProcurementRadar.Collector/       # ISourceAdapter, HttpClient policies, politeness
  ProcurementRadar.Parsers/         # GenericTableAdapter + per-authority adapters
  ProcurementRadar.Scoring/         # rule engine + LLM client
  ProcurementRadar.Notifier/        # Telegram
  ProcurementRadar.Worker/          # host, scheduling, health checks
tests/
  ProcurementRadar.Parsers.Tests/
    fixtures/{adapter_key}/*.html
config/
  keywords.json
  tr-date-patterns.json
  sources.seed.json
```

**EKAP tarafını sıfırdan yazmayın.** `saidsurucu/ihale-mcp` açık kaynak MCP sunucusu EKAP v2 üzerinde ihale arama, ihale detayı, duyuru çekme, OKAS kodu arama ve idare arama sunuyor. Önce onu bağlayıp veriyi görün, sonra ihtiyacınıza göre fork'layın.

EKAP'ın resmi kamu API'si yok, RSS akışları son ~50 ilanla sınırlı ve HTML yapısı sık değişiyor. Bu yüzden `EkapAdapter` en kırılgan bileşen olacak — testleri en sıkı orada tutun.

---

## 14. Yol haritası

Akşam ve hafta sonu çalışması varsayımıyla.

**Hafta 0 — kod yazmadan**
EKAP MCP'sini bağlayın, son 90 günün Ankara ilanlarını elle tarayın. 100 ilan okuyun. *Gerçekten teklif verebileceğiniz iş var mı?* Cevap hayırsa proje burada biter ve bir hafta kaybetmiş olursunuz, altı ay değil.

**Hafta 1** — Şema, `source` seed'i, `GenericTableAdapter`, ilk 3 kurum. `raw_fetch` arşivi çalışıyor.

**Hafta 2** — 25 kaynak. Tekilleştirme. `TurkishDateParser`.

**Hafta 3** — Rule-based filtre + Telegram bildirimi. **Bu noktada sistem işe yaramaya başlar.**

**Hafta 4** — Doküman indirme, metin çıkarma, LLM skorlama.

**Hafta 5** — Health check, silent source alarmları, haftalık rapor.

**Hafta 6** — 80 kaynak. `award` takibi.

S�rayı bozmayın. En sık yapılan hata Hafta 4'ün işlerine Hafta 1'de başlamak; sonuçta güzel bir LLM skorlayıcı ve 3 kaynak olur.

---

## 15. Başarı ölçüsü

Üç ay sonunda bakılacak tek tablo:

| Metrik | Hedef |
|---|---|
| Aktif kaynak (`is_active = true`) | ≥ 60 |
| Uygun ilan / ay | ≥ 8 |
| Ortalama yakalama gecikmesi (`first_seen_at` − yayın anı) | < 30 dk |
| Teklif verilen iş / ay | ≥ 3 |
| Kazanılan iş | ≥ 1 |

Son satır 0 kalırsa sorun tarayıcıda değil, teklif fiyatınızda veya kurumlarla ilişkinizde demektir. Araç doğru veriyi getiriyorsa hatayı orada aramayın.

---

## 16. Sonraki sürüm için not

Bu sistem altı ay çalıştıktan sonra elinizde Türkiye'de kimsede olmayan bir veri seti olur: hangi kurum ne sıklıkla ne alıyor, hangi işler kaça kapanıyor, hangi şartnameler tekrar ediyor. Asıl değer tarayıcıda değil, o arşivde.

O noktada bunu ürüne çevirme kararı verebilirsiniz — ama önce en az bir iş kazanmış olun.
