# Vize Kabul Durumu

Tasarım dokümanı Bölüm 12.3'teki kabul kriterleri ve Bölüm 21'deki isterler. Doküman `v1.0-vize` etiketini tüm kriterler sağlanınca öngörür; eksik kriterle etiketlenirse bu belge eksikleri gösterir.

## Kabul kriterleri

| # | Kriter | Durum | Dayanak |
|---|---|---|---|
| 1 | 8 vize isteri geçen testlerle doğrulanmış | Sağlandı | Aşağıdaki tablo |
| 2 | Birim ve entegrasyon testlerinin tamamı geçer | Sağlandı | `dotnet test` |
| 3 | En az 3 fiziksel bilgisayarla uçtan uca sınav | **Sağlanmadı, atlandı** | Ekip kararıyla tekrar yapılmadı. 05.11.2026 denemesinin kayıtlarında eğitmen ve tek öğrenci (`172.20.10.2`) var; ikinci öğrenci, geri dönüş, ikinci cihaz ve sunucu yeniden başlatma görünmüyor. Bu senaryolar yalnız tek bilgisayarda gerçek pencerelerle ve otomatik testlerle denendi |
| 4 | Başka bir ekip üyesi README ile sıfırdan kurar | **Yapılmadı** | [Deneme listesi](laboratuvar-deneme-listesi.md), bölüm 3 |
| 5 | Final modülü kodu yok, yalnız genişleme noktaları | Sağlandı | Kaynak taraması ve `ProjectDependencyTests` |

## İsterler

Laboratuvar sütunu: "Kayıtta var" veritabanı loglarından doğrulandı; "Bildirildi" yalnız kullanıcının sözlü bildirimidir, kayıtta iz bırakmaz; "Yapılmadı" kayıtlara göre gerçekleşmemiştir.

| No | İster | Otomatik test | Laboratuvar (2 bilgisayar, 05.11.2026) |
|---|---|---|---|
| V1 | Rol tabanlı giriş, oturum, veritabanı | VT-01 – VT-07 | — |
| V2 | Eğitmen sunucu, öğrenciler bağlanır | VT-09; yük: VT-37 | Kayıtta var: başka bilgisayardan bağlantı. VT-08 belirsiz |
| V3 | Soru bankası | VT-10 – VT-14 | — |
| V4 | Sınav hazırlama ve eş zamanlı gönderme | VT-15, VT-16, VT-18, VT-37 (bildirim süresi) | Kayıtta var: yayın ve katılım. VT-17 (3 lobi, 1 sn) yapılmadı |
| V5 | Öğrencinin süre içinde çözüp göndermesi | VT-19 – VT-23 | VT-19 ve VT-21 yapılmadı |
| V6 | Cevap toplama ve log dosyası | VT-24 – VT-26, VT-36 | VT-24 bildirildi. VT-36 kayıtta var |
| V7 | Anında puanlama | VT-31 – VT-34 | VT-34 kayıtta var |
| V8 | Öğrenci bazında sonuçlar | VT-35 | Bildirildi |
| H10 | Süre sonrası gönderim yok | VT-27 (kaynak denetimi), VT-28 – VT-30 | VT-27 bildirildi. VT-30 yapılmadı |
| H12 | Teslim saati logda ve panelde | VT-36 | Kayıtta var |

## Açık kararlar

| Konu | Durum |
|---|---|
| Bağlantı olayları `activity_logs` tablosuna yazılmıyor | Doküman 6.4 ile 11.4 çelişiyor; ekip kararı bekliyor |
| `SCORE_OVERRIDDEN` satırı ham dosyada bir karakter kayıyor | Doküman biçimi korundu; işlevi etkilemez |

## Yük testi

`scripts\load-test.ps1`, sunucu ve sanal öğrenciler aynı bilgisayarda. WiFi gecikmesini kapsamaz.

| Öğrenci | Cevap kaydı %95 | Yayın bildirimi | Sonuç listesi | Hata |
|---|---|---|---|---|
| 60 | 15 ms (hedef 300) | 59 ms (hedef 1000) | 32 ms (hedef 1000) | 0 |
| 100 | 15–22 ms | — | — | 0 (3 tekrar) |
