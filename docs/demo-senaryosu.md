# Vize Demo Senaryosu

Süre: yaklaşık 10 dakika. Eğitmen bilgisayarı ve en az 2 öğrenci bilgisayarı gerekir. Kurulum için [README](../README.md).

## Demodan önce

| Adım | Kontrol |
|---|---|
| Ağ | Tüm bilgisayarlar aynı ağda; öğrencilerde `Test-NetConnection <sunucu adresi> -Port 5000` başarılı |
| Sunucu | `ProgLab.Server.exe` açık, eğitmen girişi yapılmış |
| Öğrenciler | Uygulama açık, giriş yapılmış, lobide bekliyor |
| Yedek | İstemci yalıtımı çıkarsa telefon erişim noktası hazır |

## Akış

| # | Ne gösterilir | Kim | Ne söylenir |
|---|---|---|---|
| 1 | Eğitmen ana sayfası: sunucu adresi, bağlı öğrenciler | Eğitmen | Eğitmen bilgisayarı sunucudur, internet gerekmez |
| 2 | Soru bankası: şıklı ve klasik soru, kabul edilen cevaplar | Eğitmen | Klasik sorular metin karşılaştırmasıyla puanlanır, elle düzeltilebilir |
| 3 | Yeni sınav: başlık, süre, iki soru; **Yayınla** | Eğitmen | Sınav yalnız seçilen şubeye gider |
| 4 | Sınav iki lobide aynı anda belirir, **Katıl** | Öğrenciler | Bildirim SignalR ile gelir; süre sunucudan alınır |
| 5 | **Canlı izle** açılır; A cevaplarken sayı artar | Eğitmen, A | Her cevap otomatik kaydedilir |
| 6 | A'nın WiFi'si kapatılır, cevap yazılır, "Bağlantı yok"; WiFi açılır, "kaydedildi" | A | Kopmada cevap kaybolmaz |
| 7 | B'nin uygulaması kapatılıp açılır, **Devam et** | B | Cevaplar sunucudadır, süre sıfırlanmaz |
| 8 | A **Teslim et**: sunucu saatiyle teslim alındı | A | Teslim zamanı sunucudan yazılır |
| 9 | Eğitmen **Bitir**; B otomatik teslim edilir | Eğitmen, B | Süre dolunca da aynı akış çalışır; sonraki istek reddedilir |
| 10 | **Sonuçlar**: puan, teslim saati, "Katılmadı"; **Cevapları aç**, puan düzeltme | Eğitmen | Otomatik puan anında hesaplanır; yalnız klasikte elle düzeltme var |
| 11 | **Loglar**: öğrenci no filtresi, **Ham dosyayı göster** | Eğitmen | Teslim saatleri log dosyasında tutulur ve panelde görünür |

## Sorulabilecek sorular

| Soru | Kısa cevap |
|---|---|
| Öğrenci saati değiştirirse? | Etkilemez: kalan süre sunucudan gelir, sayaç bilgisayar saatini okumaz |
| Cevap anahtarı öğrenciye gidiyor mu? | Hayır: öğrenciye dönen soru tipinde doğru cevap alanı yoktur, test edilir |
| Sunucu kapanırsa? | Veri PostgreSQL'de; açılınca sınav sürer, süresi geçmişse tamamlanır ve otomatik teslim edilir |
| Kaç öğrenci taşır? | Aynı bilgisayarda 100 sanal öğrenciyle denendi; gerçek sınır WiFi'dir, WiFi ayrıca ölçülmedi |
| Kod sorusu nerede? | Finalde: vizede yalnız genişleme noktaları var (`IGrader`, soru tipi şablonu) |
