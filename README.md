# Programlama Laboratuvarı Sınav Platformu

Yerel ağda çalışan masaüstü sınav platformu. C#, .NET 10, Avalonia UI, ASP.NET Core ve PostgreSQL ile geliştirilir.

Şu anki durum: **Vize — Aşama 11 (çoklu bilgisayar denemesi)**. Tüm vize özellikleri çalışır. Gerçek laboratuvar denemesi için [deneme listesine](docs/laboratuvar-deneme-listesi.md) bakın.

## Projeler

| Proje | Görevi |
| --- | --- |
| `src/ProgLab.Shared` | Ortak DTO'lar, enum'lar, hata kodları, hub olay adları |
| `src/ProgLab.Server` | ASP.NET Core Web API sunucusu |
| `src/ProgLab.Client.Core` | İki masaüstü uygulamasının ortak API iletişimi |
| `src/ProgLab.InstructorApp` | Eğitmen masaüstü uygulaması (Avalonia) |
| `src/ProgLab.StudentApp` | Öğrenci masaüstü uygulaması (Avalonia) |
| `tests/ProgLab.Server.Tests` | xUnit testleri |
| `tests/ProgLab.LoadTest` | 60 sanal öğrenciyle yük testi (VT-37) |

## Kurulum

Eğitmen bilgisayarı aynı zamanda sunucudur. İnternet gerekmez; yalnızca aynı yerel ağ gerekir.

### 1. Kurulum dosyalarını hazırlayın

Geliştirme bilgisayarında (.NET 10 SDK gerekir), bir kez:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1
```

Çıktı `artifacts\win-x64` altına yazılır: `Server`, `InstructorApp`, `StudentApp`. Hedef bilgisayara .NET kurmak gerekmez. Başka sistem için `-Runtime linux-x64` veya `-Runtime osx-arm64` verin.

### 2. Eğitmen bilgisayarı

1. PostgreSQL 16 veya üzerini kurun (kurulumda `postgres` parolası belirlersiniz) ve servisin çalıştığından emin olun.
2. `Server` ve `InstructorApp` klasörlerini kopyalayın.
3. Veritabanını hazırlayın. Betik `postgres` parolasını sorar, parolaları üretir ve `Server\appsettings.Local.json` dosyasına yazar:

   ```bash
   powershell -NoProfile -ExecutionPolicy Bypass -File Server\scripts\setup-local-db.ps1
   ```

   Bu bilgisayarda veritabanı zaten kuruluysa betiği çalıştırmayın, mevcut `src\ProgLab.Server\appsettings.Local.json` dosyasını `Server` klasörüne kopyalayın. Aynı öğrenci parmak izini korumak için `certs` klasörünü de kopyalayın.
4. **Yönetici olarak açılmış** PowerShell'de güvenlik duvarı izinlerini ekleyin (TCP 5000, UDP 47777):

   ```bash
   powershell -NoProfile -ExecutionPolicy Bypass -File Server\scripts\firewall-windows.ps1
   ```

5. `Server\ProgLab.Server.exe` dosyasını **kendi klasöründen** çalıştırın (çift tıklama yeterli). Açılışta tablolar ve demo verisi oluşur, açık kaldığı sürece sınav sürer.
6. `InstructorApp\ProgLab.InstructorApp.exe` ile `egitmen` kullanıcısıyla girin. İlk parola `appsettings.Local.json` içindeki `Seed:InstructorTemporaryPassword` değeridir; ilk girişte değiştirilir. Ana sayfadaki sunucu adresini not edin.

### 3. Öğrenci bilgisayarları

Yalnızca `StudentApp` klasörünü kopyalayın ve `ProgLab.StudentApp.exe` dosyasını çalıştırın. Sunucu listede görünmezse ana sayfadaki adresi elle yazın. Demo öğrenciler **İlk kez giriyorum** ile girer: numara `2026900001`–`2026900030`, e-posta `<numara>@ogr.demo.local`.

### Bağlanamıyorsanız

| Belirti | Olası neden | Çözüm |
|---|---|---|
| Sunucu listede yok, elle adres çalışıyor | UDP yayını engelli | Elle yazın; isterseniz öğrenci bilgisayarında `firewall-student-windows.ps1` |
| Elle adres de zaman aşımına uğruyor | Güvenlik duvarı | Sunucuda `firewall-windows.ps1` çalıştı mı? |
| Güvenlik duvarı doğru, yine de bağlanmıyor | Ağda istemci yalıtımı | Kendi router'ınızı veya telefon erişim noktasını kullanın |
| "Sertifika değişti" uyarısı | Sunucu yeniden kuruldu, `certs` değişti | Öğrencide `%AppData%\ProgLab\StudentApp\client.json` dosyasını silin. Beklemediğiniz bir durumsa önce nedenini araştırın |
| Sunucu açılmıyor | PostgreSQL kapalı veya `appsettings.Local.json` yok | Servisi başlatın, dosyayı kontrol edin; hata `Server\logs` altındadır |

Tam deneme adımları: [docs/laboratuvar-deneme-listesi.md](docs/laboratuvar-deneme-listesi.md).

## Yük testi

60 sanal öğrenciyle katılım, cevap kaydı ve teslimi ölçer. Geçici bir sunucu ve geçici veritabanı şeması kullanır, gerçek veriye dokunmaz. Önce çalışan sunucuyu kapatın.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\load-test.ps1
```

## Çalıştırma (geliştirme)

Visual Studio 2026'da `ProgLab.sln` dosyasını açın veya terminalden:

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/ProgLab.Server
```

Sunucu `https://0.0.0.0:5000` adresini dinler; sağlık kontrolü `https://localhost:5000/api/health` adresindedir. Sertifika öz imzalıdır ve ilk açılışta `src/ProgLab.Server/certs/server.pfx` olarak üretilir (Git'e girmez).

## Yerel ağda kullanım

Eğitmen bilgisayarı sunucudur. Öğrenci uygulaması sunucuyu UDP duyurusuyla kendiliğinden bulur; bulamazsa eğitmen ana sayfasındaki adres elle yazılır.

Eğitmen bilgisayarında, yönetici olarak açılmış PowerShell'de güvenlik duvarı izinleri (TCP 5000, UDP 47777):

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\firewall-windows.ps1
```

Linux için `scripts/firewall-linux.sh`. Öğrenci bilgisayarında sunucu listede görünmüyorsa isteğe bağlı olarak `scripts/firewall-student-windows.ps1` çalıştırılabilir veya adres elle yazılır.

Öğrenci, sunucunun sertifika parmak izini ilk bağlantıda kaydeder ve sonraki bağlantılarda yalnızca ona güvenir. Sertifika değişirse bağlantı reddedilir. Öğrenci kayıtları `%AppData%\ProgLab\StudentApp\client.json` dosyasındadır.

## Gizli ayarlar

Gizli değerler `src/ProgLab.Server/appsettings.Local.json` dosyasında durur ve Git'e girmez. Örnek biçim için `appsettings.Local.example.json` dosyasına bakın.

## Veritabanı kurulumu

PostgreSQL 16 veya üzeri kurulu olmalıdır. Aşağıdaki betik sınırlı yetkili `proglab_app` kullanıcısını, `proglab` ve `proglab_test` veritabanlarını oluşturur, parolaları üretip `appsettings.Local.json` dosyasına yazar. Betik yönetici (`postgres`) parolasını sorar, hiçbir şeyi silmez. PostgreSQL yolu betikte 18 sürümüne göre yazılıdır.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\setup-local-db.ps1
```

Sunucu her açılışta migration'ları uygular ve eksikse demo verisini ekler. Testler `proglab_test` içinde geçici şemalarda çalışır, `proglab` veritabanına dokunmaz.

Şema değişikliği için yeni migration:

```bash
dotnet ef migrations add MigrationAdi --project src/ProgLab.Server --output-dir Data/Migrations
```
