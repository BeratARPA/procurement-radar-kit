# Kurulum

D-10 "eski bir laptop + Ubuntu + systemd" diyordu. Gerçekte elimizde tek makine
var: geliştirmenin yapıldığı Windows 11 makinesi, PostgreSQL zaten Docker
Desktop üzerinde çalışıyor. Karar buna göre güncellendi — aşağıdaki **Windows**
bölümü geçerli olan; Linux bölümü ileride ayrı bir makine çıkarsa diye duruyor.

---

## Neden bu iş ertelenmemeli

Sistem çalışmadığı sürece hiçbir şey yakalamıyor. Ve yakalanmayan ilan geri
gelmiyor: bazı kaynaklar listelerinde yalnızca birkaç günlük pencere tutuyor
(Ankara Bilkent Şehir Hastanesi ~4 gün), yani kapalı geçen bir gün kalıcı veri
kaybı demek. `first_seen_at` — projenin bütün değeri — ancak makine açıkken
birikir.

---

## Windows (bu makine)

### Neden servis değil, zamanlanmış görev

Windows'ta Docker motoru Docker Desktop'ın içinde yaşıyor ve Docker Desktop bir
kullanıcı oturumu istiyor: açılışta değil, siz giriş yapınca başlıyor. Windows
Service oturum açılmadan başlar, veritabanını bulamaz ve boş odaya konuşur.
Bu makineyi gerçekten ayağa kaldıran şey sizin giriş yapmanız, o yüzden worker
da oraya bağlanıyor.

Worker'ın veritabanı hazır olmadan başlaması sorun değil — `CollectionWorker`
her turu try/catch içinde çalıştırıyor, bir sonraki turda yeniden dener — ama
görev yine de iki dakika gecikmeli başlıyor ki ilk dakikasını hata basarak
geçirmesin. `docker-compose.yml` içindeki `restart: unless-stopped` de
PostgreSQL'i Docker Desktop ile birlikte geri getiriyor.

### Kurulum

Depo kökünde, PowerShell:

```
.\deploy\install-windows.ps1
```

Yaptıkları: worker'ı `publish/` altına yayınlar, `config/` klasörünü yanına
kopyalar, EKAP için Chromium'u bir kez indirir (~150 MB), görevi oturum
açılışına kaydeder ve çalışmasını engelleyecek şeyleri raporlar.

Kaldırmak için: `.\deploy\install-windows.ps1 -Uninstall`

### Uyku — en kolay unutulan şey

Uyuyan laptop hiçbir şey toplamaz. Kurulum betiği bunu kontrol edip söylüyor;
kapatmak için:

```
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0
```

Kapağı kapatmak da uyutur — Denetim Masası → Güç Seçenekleri → "Kapağı
kapattığımda" → "Hiçbir şey yapma".

### Elle başlatma ve izleme

```
Start-ScheduledTask -TaskName ProcurementRadar
Get-Content publish\logs\procurement-radar-*.log -Tail 20 -Wait
```

Loglar JSON (D-07).

---

## Linux (ileride ayrı bir makine olursa)

## 1. Bağımlılıklar

```bash
sudo apt update
sudo apt install -y dotnet-sdk-8.0 docker.io docker-compose-plugin powershell
sudo usermod -aG docker $USER      # oturumu kapatıp açın
```

`powershell` yalnızca Playwright'ın tarayıcı kurucusu için gerekiyor; worker
çalışırken kullanılmıyor.

## 2. Kullanıcı ve dizin

```bash
sudo useradd --system --create-home --home-dir /opt/procurement-radar radar
sudo mkdir -p /opt/procurement-radar/{data,logs}
sudo chown -R radar:radar /opt/procurement-radar
```

## 3. Yayınlama

Geliştirme makinenizde:

```bash
dotnet publish src/ProcurementRadar.Worker -c Release -o publish
dotnet publish src/ProcurementRadar.Cli -c Release -o publish-cli
```

`publish/`, `publish-cli/`, `config/` ve `docker-compose.yml` dosyalarını
sunucudaki `/opt/procurement-radar/` altına kopyalayın.

## 4. Tarayıcı (yalnızca EKAP için)

```bash
cd /opt/procurement-radar
sudo -u radar PLAYWRIGHT_BROWSERS_PATH=/opt/procurement-radar/.playwright \
  pwsh playwright.ps1 install --with-deps chromium
```

~150 MB iner, bir kez. Kurulmazsa yalnızca iki EKAP kaynağı hata verir —
diğer 31 kaynak çalışmaya devam eder, ki bu kolayca "EKAP çökmüş" diye
yanlış okunur. Servis dosyası bu yüzden `PLAYWRIGHT_BROWSERS_PATH` veriyor.

## 5. Veritabanı

```bash
cd /opt/procurement-radar
cp .env.example .env      # doldurun, sonra: chmod 600 .env
docker compose up -d
dotnet publish-cli/ProcurementRadar.Migrator.dll     # veya Migrator'ı ayrıca publish edin
dotnet publish-cli/ProcurementRadar.Cli.dll seed
```

`.env` içinde doldurulacak beş değişken D-06'da listeli. Telegram token'ı
boş bırakırsanız worker yine çalışır, yalnızca bildirim göndermez — sağlık
izleme ve toplama etkilenmez.

## 6. Servis

```bash
sudo cp deploy/procurement-radar.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now procurement-radar
```

Kontrol:

```bash
systemctl status procurement-radar
journalctl -u procurement-radar -f -o cat | jq -r '"\(.["@t"]) \(.["@mt"])"'
```

Loglar zaten JSON (D-07), o yüzden `jq` ek kurulum istemeden çalışır.

---

## Yedekleme

D-04: otomatik yedek kurmayın, elle yeterli. Haftada bir:

```bash
tar czf radar-$(date +%F).tgz -C /opt/procurement-radar data
```

İkinci bir diske veya birinizin makinesine atın. `data/raw` arşivi kaybolursa
parser'ları düzeltip geçmişi yeniden işleme imkânı da kaybolur — mimari kural
2'nin dayandığı şey o dizin.

## Makine kapanırsa

Sorun değil, `Restart=always` var ve açılışta ilk tarama beklemeden tetiklenir:
`last_fetch_at` geçmişte kalan her kaynak hemen sıraya girer. Ama kapalı geçen
saatlerde yayınlanan ve listeden düşen ilanlar geri gelmez.

## Güncelleme

```bash
sudo systemctl stop procurement-radar
# publish/ ve config/ dosyalarını değiştirin
dotnet publish-cli/ProcurementRadar.Cli.dll seed    # kaynak defteri değiştiyse
sudo systemctl start procurement-radar
```

Migration'lar idempotent; yeni script yoksa hiçbir şey yapmaz.
