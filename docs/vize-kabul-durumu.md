# Vize Kabul Durumu

Tasarım dokümanı Bölüm 12.3'teki kabul kriterleri ve Bölüm 21'deki isterler. Doküman `v1.0-vize` etiketini tüm kriterler sağlanınca öngörür; eksik kriterle etiketlenirse bu belge eksikleri gösterir.

## Kabul kriterleri

| # | Kriter | Durum | Dayanak |
|---|---|---|---|
| 1 | 8 vize isteri geçen testlerle doğrulanmış | Sağlandı | Aşağıdaki tablo |
| 2 | Birim ve entegrasyon testlerinin tamamı geçer | Sağlandı | `dotnet test` |
| 3 | En az 3 fiziksel bilgisayarla uçtan uca sınav | **Kısmen, tamamlanmadı** | 06.11.2026 denemesi 3 bilgisayarla başladı, ekip kararıyla yarıda bırakıldı. Kopup geri gelme, sunucu yeniden başlatma, teslim, otomatik teslim ve sonuç adımları bu 3 bilgisayarlı denemede yapılmadı |
| 4 | Başka bir ekip üyesi README ile sıfırdan kurar | **Yapılmadı** | [Deneme listesi](laboratuvar-deneme-listesi.md), bölüm 3 |
| 5 | Final modülü kodu yok, yalnız genişleme noktaları | Sağlandı | Kaynak taraması ve `ProjectDependencyTests` |

## İsterler

Laboratuvar sütunu: "Kayıtta var" veritabanı loglarından doğrulandı; "Bildirildi" yalnız kullanıcının sözlü bildirimidir; "Yapılmadı" gerçek ağda denenmedi.

| No | İster | Otomatik test | Laboratuvar |
|---|---|---|---|
| V1 | Rol tabanlı giriş, oturum, veritabanı | VT-01 – VT-07 | — |
| V2 | Eğitmen sunucu, öğrenciler bağlanır | VT-09; yük: VT-37 | Kayıtta var: iki ayrı öğrenci bilgisayarı. VT-08 bildirildi |
| V3 | Soru bankası | VT-10 – VT-14 | — |
| V4 | Sınav hazırlama ve eş zamanlı gönderme | VT-15, VT-16, VT-18, VT-37 (bildirim süresi) | Yayın ve katılım kayıtta var. VT-17 (1 sn) bildirildi, 2 lobi ile |
| V5 | Öğrencinin süre içinde çözüp göndermesi | VT-19 – VT-23 | VT-19, VT-21 kayıtta var (06.11) |
| V6 | Cevap toplama ve log dosyası | VT-24 – VT-26, VT-36 | Otomatik kayıt kayıtta var. VT-24: 06.11 12:45 denemesinde kuyruktaki cevap yeniden giriş olmadan sunucuya geldi (kayıtta var; kopukken yazıldığı bildirildi). Aynı gün sabah okul ağındaki denemede cevap gelmedi, nedeni belirlenemedi. VT-36 kayıtta var (05.11, 2 bilgisayar) |
| V7 | Anında puanlama | VT-31 – VT-34 | VT-34 kayıtta var (05.11, 2 bilgisayar) |
| V8 | Öğrenci bazında sonuçlar | VT-35 | Bildirildi (05.11, 2 bilgisayar) |
| H10 | Süre sonrası gönderim yok | VT-27 (kaynak denetimi), VT-28 – VT-30 | VT-27 bildirildi. **VT-30 yapılmadı** (ağda; tek bilgisayarda pencereyle denendi) |
| H12 | Teslim saati logda ve panelde | VT-36 | Kayıtta var (05.11, 2 bilgisayar) |

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
