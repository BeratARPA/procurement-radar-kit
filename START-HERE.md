# Başlangıç

Bu klasör Claude Code'a verilecek her şeyi içeriyor. Sıra önemli.

**Toplam nakit maliyet: 0 TL.** Tek gider elektrik, ayda 25-35 TL.

---

## Ne ücretsiz, ne değil

| Bileşen | Ücretli yol | Bizim yolumuz | Maliyet |
|---|---|---|---|
| Sunucu | VPS, ~150 TL/ay | Eski laptop, Ubuntu Server | Elektrik |
| Veritabanı | Supabase / RDS | Docker'da PostgreSQL | 0 |
| Dosya arşivi | Object storage | Yerel disk (~180 MB/yıl) | 0 |
| Bildirim | SMS / e-posta servisi | Telegram Bot API | 0 |
| LLM skorlama | Anthropic API | **v1'de yok** — bkz. aşağıda | 0 |
| Domain / SSL | — | Gerekmiyor, dışa açık servis yok | 0 |

**LLM'i çıkarmak kayıp değil.** 80 kaynak, sadece Ankara, sadece yazılım
filtresi → günde tahminen 2-5 ilan. Beş satırı elle okumak beş dakika sürer.
Günde beş satırı okumamak için LLM katmanı kurmak, o hacimde anlamsız.
Günlük ilan sayısı 30'u geçince ekleyin.

**Tek gerçek kısıt:** makine kapalıyken duyuru kaçırırsınız. Ama doğrudan
temin duyuruları mesai saatlerinde çıkıyor ve pratikte 2-5 günlük pencere
veriliyor — 24 saat yasal taban, tipik değer değil. Günde 12-16 saat açık bir
makine işinizi görür.

---

## Adım 1 — Repo (10 dakika)

```bash
mkdir procurement-radar && cd procurement-radar
git init
```

Bu klasördeki her şeyi repo köküne kopyala. `START-HERE.md` hariç — o sende
kalsın, repo'ya girmesin.

---

## Adım 2 — Makine (bir akşam)

Eski bir laptop ideal. Yoksa birinizin masaüstü PC'si de olur.

1. Ubuntu Server 24.04 kur (masaüstü sürümüne gerek yok)
2. Laptop ise: kapak kapanınca uyumasın →
   `/etc/systemd/logind.conf` içinde `HandleLidSwitch=ignore`
3. Docker kur: `curl -fsSL https://get.docker.com | sh`
4. .NET 8 SDK kur: `sudo apt install dotnet-sdk-8.0`
5. Sabit yerel IP ver, SSH aç — üçünüz uzaktan bağlanabilesiniz

Elektrik: 10-15 W sürekli tüketim, ayda ~7-10 kWh, yaklaşık 25-35 TL.
Tek giderin bu.

---

## Adım 3 — Telegram botu (5 dakika)

Tamamen ücretsiz, kart istemiyor.

1. Telegram'da `@BotFather` → `/newbot` → bot token'ı al
2. Üçünüzün olduğu bir grup aç, botu ekle
3. Grup id'sini al: gruba `@RawDataBot` ekle, id'yi gör, sonra çıkar

---

## Adım 4 — Kaynak toplama (bir akşam, üçünüz)

`docs/source-collection.md` dosyasını okuyun, `config/sources.seed.json`
dosyasını doldurun. **Önce 10 kaynak yeter.**

Bu adım atlanamaz. Claude Code URL uyduramaz; uydurursa 404 dolu bir seed
alırsın ve haftalarca "neden ilan gelmiyor" diye ararsın.

Paralel yapabilirsin: sen kaynak toplarken Claude Code T-00 ile T-05 arasını
yazabilir, o ticket'lar kaynak verisine ihtiyaç duymuyor.

---

## Adım 5 — Claude Code

Repo kökünden `claude` çalıştır. İlk mesaj olarak tam olarak şunu yapıştır:

---

> Bu repo'yu sıfırdan kuracağız. Önce `CLAUDE.md`, `docs/spec.md`,
> `docs/decisions.md` ve `docs/tickets.md` dosyalarını oku.
>
> Kurallar:
> - Kararlar `docs/decisions.md` içinde kapalı. Sorgulamana veya alternatif
>   önermene gerek yok, uygula.
> - **Sıfır maliyet kısıtı var.** Ücretli servis, bulut hesabı, metered API
>   ekleme. Gerektiğini düşünüyorsan dur ve bana sor.
> - Aynı anda tek ticket üzerinde çalış. Hangisi olduğunu ben söyleyeceğim.
> - Bir ticket'ın kabul kriterlerinin tamamı sağlanmadan bir sonrakine geçme.
> - Belirsizlik varsa uydurma, bana sor.
>
> Şimdi T-00'ı uygula.

---

## Adım 6 — Ticket ticket

Her ticket bitince:

```bash
dotnet build && dotnet test
git add -A && git commit -m "T-00: local infrastructure"
```

Sonraki oturumda tek cümle yeter: **"T-01'i uygula."**

Sıra: T-00 → T-12, **T-13 atlanır**, T-14 → T-15 → T-16.

Tüm listeyi birden verme. Claude Code kapsamı dar görevlerde iyi, 16 maddelik
listede dağılır.

---

## Paralellik

```
Sen                          Claude Code
──────────────────────       ──────────────────────────
Makine kurulumu        ║     T-00 → T-01 → T-02 → T-03
Telegram botu          ║     T-05 → T-08
10 kaynak topla        ║
                       ╬     T-04 → T-06 → T-07 → T-09 → T-10
                       ║     T-11 → T-12 → T-14 → T-15
80 kaynağa çıkar       ║
                       ║     T-16 (EKAP, en son)
```

İlk gerçek Telegram bildirimi T-14'te düşer. Öncesi çalışıyor ama sessiz.

---

## Not: Claude Code'un kendisi

Altyapı tarafı sıfır maliyet. Claude Code'un kendisi ayrı bir konu — bir
abonelik veya API kredisi gerektiriyor. Zaten kullanıyorsan sorun yok.
Kullanmıyorsan aynı iş elle de yapılabilir; spec ve ticket'lar bunun için
yeterince ayrıntılı yazıldı, sadece daha uzun sürer.

---

## Uyarı

`docs/decisions.md` içindeki kararları değiştirmek istersen **önce dosyayı
güncelle, sonra Claude Code'a söyle.** Sohbette söyleyip dosyayı
güncellemezsen bir sonraki oturumda eski karara döner.
