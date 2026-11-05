# Programlama Laboratuvarı Sınav Platformu

Yerel ağda çalışan masaüstü sınav platformu. C#, .NET 10, Avalonia UI, ASP.NET Core ve PostgreSQL ile geliştirilir.

Şu anki durum: **Vize — Aşama 7 (öğrenci sınav ekranı)**. Cevapların sunucuya kaydı ve teslim Aşama 8'de eklenir.

## Projeler

| Proje | Görevi |
| --- | --- |
| `src/ProgLab.Shared` | Ortak DTO'lar, enum'lar, hata kodları, hub olay adları |
| `src/ProgLab.Server` | ASP.NET Core Web API sunucusu |
| `src/ProgLab.Client.Core` | İki masaüstü uygulamasının ortak API iletişimi |
| `src/ProgLab.InstructorApp` | Eğitmen masaüstü uygulaması (Avalonia) |
| `src/ProgLab.StudentApp` | Öğrenci masaüstü uygulaması (Avalonia) |
| `tests/ProgLab.Server.Tests` | xUnit testleri |

## Çalıştırma

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
