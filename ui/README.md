# Arayüz (T-19)

Flutter. Şimdilik yalnızca Web; Windows masaüstü ve Android T-22'de aynı koddan
derlenecek. D-16'nın tarif ettiği arayüz bu.

## Çalıştırma

Önce api ayakta olmalı — arayüz veriyi ondan alır, veritabanına bağlanmaz:

```bash
dotnet run --project src/ProcurementRadar.Api
```

Sonra, `ui/` içinde:

```bash
flutter run -d chrome
```

İlk açılışta iki şey sorulur: **api adresi** ve **api anahtarı**
(`.env` içindeki `PR_API_TOKEN`). İkisi de tarayıcının yerel deposunda saklanır.

Telefondan açarken adres `localhost` **değil**, toplamayı yapan makinenin ev
ağındaki adresidir — `http://192.168.1.x:5080` gibi. `localhost` telefonun
kendisi demektir ve orada api yok.

## Dart istemcisi elle yazılmaz

`lib/api/` tamamen üretilmiştir; içindeki hiçbir dosya elle düzenlenmez.
Kaynağı `swagger.json` — o da api'nin `/swagger/v1/swagger.json` ucunun
kaydedilmiş hali. Api değiştiğinde:

```bash
curl http://localhost:5080/swagger/v1/swagger.json -o swagger.json
dart run swagger_parser
dart run build_runner build
```

İki adım da gerekli: ilki modelleri ve istemcileri yazar, ikincisi onların
işaret ettiği `part` dosyalarını üretir. İlkinden sonra proje derlenmez.

`build_runner`'ın ilk çalışması ~2,5 dakika sürer (builder'ları AOT derliyor);
takılmış gibi görünür, takılmamıştır. `--delete-conflicting-outputs` bayrağı
2.15 ile kaldırıldı, vermeyin.

`swagger.json` depoya bilerek konuyor: bu dizinin girdisi o dosya, dolayısıyla
api'de bir değişiklik incelemede *şema değişikliği* ve *üretilmiş istemci
değişikliği* olarak yan yana görünür. Üretilmiş kod da depoda — klonlayan biri
kod üretmeden çalıştırabilsin diye.

### Metot adları C# tarafından geliyor

Controller'lardaki `[HttpGet(Name = "listNotices")]` Swashbuckle'a `operationId`
yazdırır, üretici de Dart metodunu ondan adlandırır: `api.notices.listNotices()`.
İsim silinirse üretici yola düşer ve metot adı diff'te görünür şekilde değişir.

Aynı şekilde `SupportNonNullableReferenceTypes` sayesinde C#'ta null olamayan
bir alan Dart'ta da null olamaz. O satır olmadan `title` bile `String?` gelir.

## Neler var

| Ekran | Ne yapar |
|---|---|
| İlanlar | Açık ilanlar, son teklif tarihine göre. Arama tüm arşivi tarar. |
| İlan detayı | İlan metni, kaynağa bağlantı ve **durum butonları** |
| Durum | Kaç kaynak, kaç ilan, son tarama ne zaman |
| Filtre | Kelime listeleri, eşik, ve yanında canlı önizleme |
| Kaynaklar | Kaynak ekle/düzenle, selector'ları kayıtlı sayfaya karşı dene, aç/kapa |
| Sistem | **Yalnızca masaüstü.** Docker/worker/api durumu, başlat-durdur, açılışta başlatma, CLI komutları, log |
| Ayarlar | Api adresi, anahtar, açık/koyu tema |

Dar ekranda gezinme altta (`NavigationBar`), geniş ekranda yanda
(`NavigationRail`); sınır Material 3'ün 600 px'i.

## Kaynaklar ekranı (T-21)

Kaynak eklemek, spec Bölüm 5'e göre, başkasının markup'ına karşı CSS selector
yazmak demek — ve doğru yazıp yazmadığınızı öğrenmenin tek yolu kaynağı açıp
beklemekti. O da ya ilan üretir ya hiçbir şey, ve "hiçbir şey" tam olarak sessiz
bir haftası olan sağlıklı bir kaynağa benzer.

Düzenleyicideki **dene** düğmesi bunu bir saniyede cevaplıyor. **Ağa çıkmaz:**
sayfa arşivden gelir — koleksiyoncunun gerçekten indirdiği sayfa — ya da
yapıştırılır. Yani bir selector denemek idareye hiçbir şeye mal olmaz.

Yeni kaynak **kapalı** eklenir. spec Bölüm 5 üç gün gözlemden sonra açmayı
söylüyor; açmak ayrı ve bilerek yapılan bir hareket.

Sonuçta kaç satır bulunduğu, kaçının tarihi okunduğu ve kaçının doküman bağladığı
yazar. Tarih sayısı sıfırsa kırmızıdır: satırlar doğru gelip tek bir tarih
okunmaması, genellikle `date_selector`'ın yanlış hücreyi göstermesidir — ve son
teklif tarihi spec Bölüm 7'nin en önemli alanı.

## Filtre ekranı (T-20)

Kelime listesini ve eşiği düzenler, her değişiklikten yarım saniye sonra
arşivin tamamına sorar: *bu ayarla 966 ilanın kaçı bildirilirdi, hangileri.*
Yakalananların yanında **kıl payı kaçanlar** da listelenir — eşiğin doğru yeri
oraya bakılarak bulunur, api'nin kendi yorumunun dediği gibi.

Yazma `PUT /api/settings/keywords` üzerinden. Satırı elle düzenlemeyin: api onu
okuyacak yükleyiciyle doğrular ve önceki değeri `setting_history`'de tutar.
Kaydettikten sonra worker en geç 30 saniyede yeni ayarla çalışır, yeniden
başlatmak gerekmez (T-17).

**Ekran `keywords` belgesinin üç anahtarını düzenler** — `include`, `exclude`,
`notify_threshold`. Geri kalanı (`okas` listeleri, notlar, T-13'ten kalan iki
ölü eşik) olduğu gibi taşınır. Bu bir incelik değil: yalnızca bilinen alanları
yazan bir ekran, biri eşiği ilk kaydırdığında okas yapılandırmasını silerdi ve
dosya hâlâ geçerli json olurdu.

## Masaüstü sürümü (D-18)

```bash
flutter run -d windows
# veya
flutter build windows --release
```

Çıktı `build\windows\x64\runner\Release\procurement_radar.exe`.

Masaüstünde web'de olmayan iki şey var:

**Sistem ekranı.** Docker kapsayıcısı, worker ve api görevleri, oturum açılışında
başlama anahtarları, `preflight`/`telegram-check`/`seed` düğmeleri ve okunabilir
log. Panel **api'den geçmez** — api'nin çalışmadığı anda ona sormak işe yaramaz,
o yüzden doğrudan `docker`, PowerShell ve log dosyaları okunur.

İki uyarı kendiliğinden çıkar: makine uykuya geçiyorsa (D-10'un dediği gibi
uyuyan makine hiçbir şey toplamaz) ve **`publish/` klasörü kaynaktan eskiyse**.
İkincisi bir akşama mal oldu: portta cevap veren api `publish/` altındaki eski
kopyaydı, her istek 200 dönüyordu ve arayüz "api'ye ulaşılamadı" diyordu.

**`.env`'den anahtar okuma.** Ayarlar ekranındaki düğme `PR_API_TOKEN`'ı dosyadan
alır. Tarayıcıda o düğme yok, çünkü orada dosya okunamıyor.

### Web bozulmasın

`dart:io` web derlemesine **asla** girmemeli. Koşullu import bunu sağlıyor:

```dart
import 'system_control_stub.dart' if (dart.library.io) 'system_control_native.dart';
```

Sistem katmanına dokunduktan sonra `flutter build web` çalıştırın — sızma olursa
orada patlar, `flutter analyze` yakalamaz.

## Anahtar nasıl saklanıyor (ve saklanmıyor)

Anahtar `localStorage`'da, **açık metin**. Tarayıcının geliştirici araçlarını
açan biri onu görür. Ayarlar ekranı bunu söylüyor.

`flutter_secure_storage` denendi ve alınmadı, iki sebeple:

1. Web'de değeri AES-GCM ile şifreliyor ama **çözme anahtarını aynı
   localStorage'a, şifreli metnin yanına** yazıyor. Sayfada çalışan bir script
   ikisini de okur. Bu bir sınır değil, bir örtme.
2. `crypto.subtle` istiyor, tarayıcılar onu yalnızca güvenli bağlamda veriyor.
   `http://localhost` güvenli sayılır, `http://192.168.1.x` sayılmaz — yani
   geliştirme makinesinde çalışır, **telefonda çalışmaz**. D-16'nın anlattığı
   kullanım tam olarak odur.

Anahtar zaten her istekte, ev ağı üzerinde, düz http başlığında gidiyor. Diskte
şifreleyip telde açık yollamak tiyatro olurdu. D-16 da anahtarın ne olduğunu
açıkça yazıyor: kullanıcı hesabı değil, ev ağındaki başka bir cihazın
yanlışlıkla ayar değiştirmesine karşı.

Api dışarı açıldığında (Tailscale/Cloudflare Tunnel) veya TLS aldığında bu karar
yeniden bakılmalı.

## Test

```bash
flutter analyze
flutter test
```

Widget testleri dio'nun **adapter**'ını değiştiriyor, `RadarApi`'yi değil.
Sebebi bu depoda zaten bir kez ödendi: arayüzün arkasına sahte konmuş bir test
kodu doğrular, sürücüyü değil (D-17). İstemciyi sahtelemek widget'ları kanıtlar
ve asıl kırılan iki katmanı — retrofit'in istek kurması ile üretilmiş
`fromJson` — atlar.

`test/fixtures/` içindekiler çalışan api'den alınmış gerçek yanıtlar, elle
yazılmış değil (D-09). Şema ile üretilmiş model ayrışırsa test orada patlar.

`flutter test` de tek başına yeterli değil. Sürüm derlemesi gerçek tarayıcıda,
gerçek api'ye karşı açılıp bakıldı: CORS ön kontrolü 204, istek 200, 200 ilan
Türkçe karakterleriyle geldi.

## Sürüm derlemesi

```bash
flutter build web --release
```

Çıktı `build/web/`. Herhangi bir statik sunucudan verilebilir; api'ye ayrı
adresten gittiği için api'nin CORS ayarı (D-16) bunu zaten karşılıyor.
