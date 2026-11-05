# Laboratuvar Deneme Listesi (Aşama 11)

En az **3 fiziksel bilgisayarla** yapılır: 1 eğitmen (sunucu), en az 2 öğrenci. Hepsi aynı ağda olmalı. Her satırın sonucunu **Geçti / Kaldı** diye işaretleyin, kalanın ekran görüntüsünü alın.

Tarih: ........ Ağ: ........ Eğitmen bilgisayarı: ........ Öğrenci bilgisayarları: ........

## 1. Hazırlık

| # | Adım | Sonuç |
|---|---|---|
| 1 | Kurulumu README'deki "Kurulum" bölümüyle yapın. Eğitmen bilgisayarında güvenlik duvarı betiği çalıştı | |
| 2 | Sunucu açıldı. Eğitmen ana sayfasında sunucu adresi görünüyor (örn. `192.168.1.10:5000`) | |
| 3 | Her öğrenci bilgisayarında PowerShell'de aşağıdaki komut `TcpTestSucceeded : True` veriyor | |

```bash
Test-NetConnection 192.168.1.10 -Port 5000
```

**3. adım `False` verirse:** Önce güvenlik duvarını kontrol edin. Doğruysa ağda istemci yalıtımı (client isolation) vardır; kendi router'ınıza veya telefon erişim noktasına geçin. Telefon erişim noktası bazı telefonlarda da yalıtım uygular, demodan önce deneyin.

## 2. Uçtan uca sınav (kabul kriteri 3)

Hazırlık: Eğitmen en az bir şıklı ve bir klasik soruyla sınav hazırlar, yayınlamaz. Öğrenciler A ve B ilk girişlerini yapar, lobide bekler.

| # | Senaryo | Adım | Beklenen | Sonuç |
|---|---|---|---|---|
| 1 | VT-08 | Öğrenci uygulamasını açın | Sunucu listede kendiliğinden görünür. Görünmezse adresi elle yazın ve bunu not edin | |
| 2 | Bağlı liste | Eğitmen ana sayfasına bakın | "Bağlı öğrenci" sayısı doğru | |
| 3 | VT-17 | Sınavı yayınlayın, kronometre tutun | **1 sn içinde** tüm lobilerde görünür | |
| 4 | Katılım | A ve B katılır. Eğitmen **Canlı izle**'yi açar | İkisi "Çözüyor", IP'ler doğru | |
| 5 | Otomatik kayıt | A soruları cevaplar | "Tüm cevaplar kaydedildi"; canlı tabloda sayı artar | |
| 6 | VT-27 | A'nın bilgisayar saatini **1 saat ileri** alın | Kalan süre değişmez. Bitince saati geri alın | |
| 7 | VT-24 | A'nın **ağını kesin** (kablo/WiFi), cevap yazın | "Bağlantı yok — cevaplarınız bağlantı gelince gönderilecek"; yazı kaybolmaz | |
| 8 | VT-24 | Ağı açın, birkaç saniye bekleyin | "Tüm cevaplar kaydedildi"; canlı tablo güncellenir | |
| 9 | VT-19 | B'nin uygulamasını **Görev Yöneticisi'nden** kapatıp açın, girip devam edin | Aynı gönderim, cevaplar yerinde, süre sıfırlanmamış | |
| 10 | VT-21 | A'nın hesabıyla **üçüncü bilgisayardan** (veya B'nin bilgisayarından) girip sınava devam edin | Katılım olur; eğitmende ikinci cihaz uyarısı, iki IP de görünür | |
| 11 | VT-30 | **Sunucuyu kapatıp açın** | Öğrencide bağlantı yok, sonra otomatik geri gelir. Sınav ve kalan süre sürer, sayaç sıfırlanmaz | |
| 12 | Teslim | A teslim eder | Teslim saati görünür; canlı tabloda "Teslim etti" | |
| 13 | Otomatik teslim | B teslim etmez, eğitmen sınavı **Bitir**'le bitirir (veya süre dolar) | B'nin ekranı kapanır; "Otomatik teslim" | |
| 14 | VT-35 | **Sonuçlar**'ı açın | Puanlar, teslim saatleri, "Katılmadı" satırları doğru | |
| 15 | VT-34 | **Cevapları aç**, klasik soruda puanı düzeltin | Toplam değişir | |
| 16 | VT-36 | **Loglar**'ı açın, öğrenci no ile filtreleyin, **Ham dosyayı göster** | Teslim satırı panelde ve dosyada | |

## 3. Kurulum denemesi (kabul kriteri 4)

Ekipten projeyi kurmamış bir üye, yalnız README'ye bakarak temiz bir bilgisayarda kurulumu tamamlar.

| # | Adım | Sonuç |
|---|---|---|
| 1 | PostgreSQL, sunucu, güvenlik duvarı, uygulamalar kuruldu | |
| 2 | Takıldığı yerler not edildi ve README düzeltildi | |

Takılınan yerler: ........

## 4. Yük denemesi (isteğe bağlı, ağ üzerinden)

`scripts\load-test.ps1` sunucuyla aynı bilgisayarda çalışır, yani WiFi gecikmesini ölçmez. Tasarım dokümanına göre asıl darboğaz WiFi'dir. Laboratuvarda öğrenci sayısı kadar gerçek bilgisayarla denerseniz gözleminizi yazın.

Gözlem: ........
