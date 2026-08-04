# Kaynak Toplama Kılavuzu

Bu projenin tek gerçek darboğazı burası. Kod değil, elle toplanacak 80 URL.
Üç kişi bölüşürseniz bir akşamda biter. Yapılmadan Claude Code'un yazacağı
hiçbir parser işe yaramaz.

## Nasıl bulunur

Her kurumun sitesinde şu başlıklardan biri var:

- İhale İlanları
- Doğrudan Temin İlanları
- Duyurular → İhale
- Teklife Davet
- Satın Alma İlanları

Bulamazsanız site içi aramada `doğrudan temin` deneyin. Hâlâ yoksa kaynağı
listeye `is_active: false` ile ekleyin ve `notes` alanına "ilan sayfası
bulunamadı" yazın — üç ay sonra tekrar bakarsınız, kurumlar sayfa açıyor.

## Her kaynak için toplanacaklar

1. **Tam URL** — liste sayfasının kendisi, ana sayfa değil
2. **Bir HTML örneği** — sayfayı kaydedip
   `tests/ProcurementRadar.Parsers.Tests/fixtures/{adapter_key}/{authority_slug}.html`
   altına atın. Fixture olmadan parser yazılamaz.
3. **CSS selector'ları** — tarayıcıda F12 → ilan satırına sağ tık → Inspect:
   - `row_selector` — her ilanı saran eleman (`table tbody tr`, `.ilan-item`, `li.duyuru`)
   - `title_selector` — satır içinde başlık (`td:nth-child(2)`, `.baslik a`)
   - `link_selector` — detay linki (`a`)
   - `date_selector` — tarih alanı
   - `amount_selector` — tutar varsa, çoğunda yok, `null` bırakın
   - `base_url` — göreli linkleri tamamlamak için (`https://ornek.gov.tr`)
4. **robots.txt kontrolü** — `{domain}/robots.txt` açın. İlan sayfası
   `Disallow` altındaysa o kaynağı **listeye almayın**, notlara sebebini yazın.

## Hedef liste

| Grup | Adet | authority_type |
|---|---|---|
| Devlet üniversiteleri | ~10 | `university` |
| Büyükşehir + ilçe belediyeleri | 26 | `municipality` |
| İl müdürlükleri | ~15 | `provincial_directorate` |
| Kamu hastaneleri | ~10 | `hospital` |
| Belediye iştirakleri | ~6 | `soe_subsidiary` |
| EKAP | 2 | `ekap` |

**Vakıf üniversitelerini almayın** — Kamu İhale Kanunu kapsamında değiller.

## Sıra

Önce 10 kaynak toplayın, T-07'yi o 10'u ile test edin, çalıştığını görün.
Sonra 80'e çıkın. Baştan 80 toplamak, yanlış selector şemasını 80 kez
tekrarlamak demek olabilir.
