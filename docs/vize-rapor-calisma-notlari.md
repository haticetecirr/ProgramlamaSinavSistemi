# Vize Raporu Teknik Çalışma Notları

Hazırlanma: 06.11.2026. Kaynaklar: ders proje PDF'i (`2026-2027_Guz_Yazılım_Geliştirme_proje.pdf`), Sistem Tasarımı v2.0, depo commit `e717e26` üzerine bağlantı bildirimi düzeltmesi, 06.11.2026 test çalıştırmaları, `proglab` veritabanı kayıtları.

**Bu dosya rapor metni değildir, ham nottur.** Ders PDF'i: içeriği öğrencilerce hazırlanmadığı anlaşılan ve doğrudan yapay zekâ ile oluşturulduğu tespit edilen raporlar değerlendirmeye alınmaz. Raporu kendi cümlelerinizle yazın, burada anlamadığınız şeyi rapora koymayın; demoda kodun herhangi bir kısmını açıklamanız da istenebilir.

Durum sözlüğü: **Geçti (otomatik)** = xUnit testi; **Kayıtta var** = laboratuvar denemesinde veritabanı logundan doğrulandı; **Bildirildi** = yalnız kullanıcının sözlü bildirimi; **Yapılmadı** = gerçek ağda denenmedi.

---

## 1. Ders isterleri (PDF'teki metin özeti)

**Vize isterleri:** (1) Log-in sayfaları ve backend: rol tabanlı giriş, kimlik doğrulama, oturum, veri tabanı altyapısı. (2) LAN/WiFi sunucu–istemci iletişimi. (3) Çoktan seçmeli ve klasik soru bankası: ekleme, düzenleme, silme, kategorilendirme. (4) Soru bankasından ödev/sınav hazırlama ve eş zamanlı gönderme. (5) Öğrenci arayüzü: ödevi görme, belirlenen zaman diliminde çözme ve gönderme. (6) Cevapların merkezi toplanması ve log dosyaları. (7) Cevap anahtarına göre anında otomatik puanlama. (8) Öğrenci bazında puanların eğitmen ekranında listelenmesi.

**Rapor isterleri:** IEEE formatı, en az 4 sayfa; özet, giriş, yöntem (algoritma açıklaması), akış diyagramı, UML diyagramı, class diyagramı, kaba kod, sonuç. 3 veya 4 kişilik grup. Sunumda tüm üyeler bulunmalı; gelmeyen olursa 0 puan.

---

## 2. Sekiz isterin kontrolü

Sunucu yolları `src/ProgLab.Server/`, istemci yolları `src/` altındadır. Test sayıları TRX çıktısından, Theory satırları dahil.

### V1. Giriş, kimlik doğrulama, oturum, veritabanı
- **Durum:** Geliştirildi.
- **Sınıflar ve dosyalar:** `AuthController` (Controllers), `AuthService`, `UserService`, `ClassService` (Services); `JwtTokenService`, `JwtKeyStore`, `PasswordHasher`, `MustChangePasswordFilter`, `SecurityOptions` (Security); `AppDbContext`, `DatabaseInitializer`, `DemoDataSeeder` (Data); istemci `ApiClient`, `TokenStore` (ProgLab.Client.Core); `LoginViewModel`, `ChangePasswordViewModel` (InstructorApp); `LoginViewModel`, `FirstLoginViewModel` (StudentApp).
- **API:** `POST /api/auth/login`, `/first-login`, `/change-password`; `GET /api/auth/me`; `/api/users`, `/api/classes`.
- **Tablolar:** `users`, `activity_logs`, `classes`, `class_members`; altyapı olarak 12 tablonun tümü.
- **Testler:** Geçti (otomatik): `AuthApiTests` 18, `AuthRateLimitTests` 1, `InstructorFirstLoginTests` 1, `UserAndClassApiTests` 10, `DatabaseSchemaTests` 55, `DatabaseRuleTests` 22, `DatabaseSeedTests` 5. Senaryolar VT-01 – VT-07. Laboratuvarda girişler yapıldı.
- **Eksik:** Yok. Not: 05.11 kayıtlarında 21:09'da kullanıcı adı alanına yanlış değer yazılmış bir `LOGIN_FAILED` var; kullanıcı adı alanı ne yazılırsa loga geçer.

### V2. LAN sunucu–istemci iletişimi
- **Durum:** Geliştirildi; gerçek ağda çalıştı, bir kopma senaryosu doğrulanamadı.
- **Sınıflar:** `Program.cs` (Kestrel 0.0.0.0:5000, HTTPS), `CertificateProvider`, `DiscoveryBroadcaster` (Background), `ExamHub`, `ConnectionTracker` (Hubs), `HealthController`, `ConnectionsController`; istemci `ServerDiscovery`, `CertificatePinner`, `ExamHubClient`, `ServerSession`, `LocalClientStore`, `ServerAddress`, `ConnectViewModel`, `LiveStatusViewModel`.
- **API:** `GET /api/health`, `GET /api/connections/students`, SignalR `/hubs/exam`, UDP 47777 duyurusu.
- **Tablolar:** Doğrudan yok (bağlı öğrenci listesi bellekte, `ConnectionTracker`).
- **Testler:** Geçti (otomatik): `DiscoveryTests` 16, `CertificateTests` 10, `HealthEndpointTests` 5, `RealtimeTests` 14, `ServerAddressTests` 12, `StudentConnectFlowTests` 2, `TimeSyncTests` 1, `HubConnectionStateTests` 1. Laboratuvar: 3 ayrı bilgisayar kayıtta var (06.11: eğitmen `172.2.2.139`, öğrenciler `172.2.3.75`, `172.2.0.209`). VT-08: Bildirildi.
- **Eksik / doğrulanmamış:**
  - 06.11'de A bilgisayarı WiFi kapatılıp açıldıktan sonra sunucuya dönmedi. Nedeni belirlenemedi; `Test-NetConnection` sonucu alınmadı.
  - Bağlantı olayları `activity_logs` tablosuna yazılmıyor (doküman 6.4 ile 11.4 çelişiyor, karar bekliyor).

### V3. Soru bankası
- **Durum:** Geliştirildi.
- **Sınıflar:** `QuestionsController`, `CategoriesController`, `QuestionService`, `QuestionValidator`, `CategoryService`, `AnswerNormalizer` (Grading); `QuestionBankViewModel`, `QuestionEditorViewModel`, `CategoryDialogViewModel`.
- **API:** `GET/POST /api/categories`, `PUT/DELETE /api/categories/{id}`; `GET/POST /api/questions`, `GET/PUT/DELETE /api/questions/{id}`.
- **Tablolar:** `categories`, `questions`, `question_options`, `classic_answer_keys`.
- **Testler:** Geçti (otomatik): `QuestionApiTests` 14, `CategoryApiTests` 8, `QuestionValidatorTests` 22, `QuestionBankViewModelTests` 9, `AnswerNormalizerTests` 11. VT-10 – VT-14. Geliştirme bilgisayarında pencere denemesi yapıldı.
- **Eksik:** Yok. Laboratuvarda bankaya yeni soru eklenmedi, seed sorular kullanıldı.

### V4. Sınav/ödev oluşturma ve eş zamanlı dağıtım
- **Durum:** Geliştirildi.
- **Sınıflar:** `ExamsController`, `ExamService`, `ExamRules`, `ExamNotifier`, `ExamHub`; `ExamListViewModel`, `ExamEditorViewModel`, `ClassesViewModel`; öğrenci `LobbyViewModel`.
- **API:** `GET/POST /api/exams`, `GET/PUT/DELETE /api/exams/{id}`, `PUT /{id}/questions`, `POST /{id}/publish`, `/{id}/end`, `/{id}/copy`, `GET /{id}/live`. SignalR olayı `ExamPublished`.
- **Tablolar:** `exams`, `exam_questions`, `classes`, `class_members`, `questions` (`is_locked`).
- **Testler:** Geçti (otomatik): `ExamApiTests` 20, `ExamRulesTests` 29, `ExamViewModelTests` 10, `RealtimeTests` 14. Yük testi: 60 öğrenciye bildirim 59 ms (05.11). Laboratuvar: yayın kayıtta var (06.11 10:57:34), iki lobide görünme Bildirildi, süre kronometreyle ölçülmedi.
- **Eksik / doğrulanmamış:** Ödevin takvimle tarih seçilerek pencereden oluşturulması gerçek kullanımda denenmedi; ödev akışı otomatik testlerle doğrulandı.

### V5. Öğrenci arayüzü
- **Durum:** Geliştirildi.
- **Sınıflar:** `StudentExamsController`, `StudentSubmissionsController`, `StudentExamService`, `SubmissionService`; `LobbyViewModel`, `ExamPaperViewModel`, `PaperQuestionViewModels`, `SubmittedViewModel`, `CountdownClock`.
- **API:** `GET /api/student/exams`, `POST /{id}/join`, `GET /{id}/paper`, `PUT /api/student/submissions/{sid}/answers/{examQuestionId}`, `POST /{sid}/submit`.
- **Tablolar:** `submissions`, `answers`, `exams`, `exam_questions`, `question_options`.
- **Testler:** Geçti (otomatik): `StudentPaperApiTests` 22, `StudentStateRulesTests` 13, `ExamPaperViewModelTests` 11, `CountdownClockTests` 13, `SubmitAndLiveViewModelTests` 10, `StudentViewModelFlowTests` 2. Laboratuvar: VT-19 ve VT-21 kayıtta var (06.11 11:17:02, 11:18:22). VT-27: Bildirildi.
- **Eksik:** Yok. Öğrenciye puan gösterilmez; ister eğitmen ekranını istiyor, öğrenciye açma isteğe bağlı olarak yapılmadı.

### V6. Cevap toplama ve log dosyaları
- **Durum:** Geliştirildi.
- **Sınıflar:** `SubmissionService` (`SaveAnswerAsync`, `SubmitAsync`), `ExamCompletionService`, `ExamTimerService`, `ActivityRecorder`, `ActivityEvents`, `ExamLogWriter`, `LogQueryService`, `LogsController`; istemci `AnswerSaveQueue`, `LogsViewModel`.
- **API:** Cevap ve teslim uçları; `GET /api/exams/{id}/logs`, `GET /api/exams/{id}/logs/file`.
- **Tablolar:** `submissions`, `answers`, `activity_logs`; dosya `logs/exams/exam-<id>.log`.
- **Testler:** Geçti (otomatik): `AnswerCollectionApiTests` 15, `AnswerSaveQueueTests` 9, `ExamTimerAndLogFileTests` 5, `RawLogFileTests` 2, `ResultsAndLogsTests` 18 (ortak). Laboratuvar: otomatik kayıt kayıtta var (06.11); teslim satırları kayıtta var (05.11).
- **Eksik / doğrulanmamış:** **VT-24 tamamlanmadı.** Otomatik testte geçiyor, ama gerçek WiFi kopmasında A'nın yazdığı `kablo testi 7` sunucuya hiç ulaşmadı, cevap `pointer` kaldı. Kopukken çıkan uyarı Bildirildi.

### V7. Otomatik puanlama
- **Durum:** Geliştirildi.
- **Sınıflar:** `IGrader`, `GradeResult`, `MultipleChoiceGrader`, `ClassicGrader` (Grading/Graders.cs), `GradingService`, `AnswerNormalizer`, `AnswersController`.
- **API:** Ayrı bir puanlama ucu yoktur, teslimde çalışır. Elle düzeltme `PUT /api/answers/{id}/score`.
- **Tablolar:** `answers` (`score`, `graded_by`, `graded_note`), `submissions` (`total_score`, `graded_at`), `question_options`, `classic_answer_keys`.
- **Testler:** Geçti (otomatik): `GraderTests` 15, `GradingApiTests` 10. VT-31 – VT-34; VT-29'un puan kısmı `ExamTimerAndLogFileTests` içinde. Laboratuvar: elle düzeltme kayıtta var (05.11, toplam 7.50 satırı); otomatik teslim puanı kayıtta var (06.11: A 20.00, B 10.00).
- **Eksik:** Yok.

### V8. Sonuç listesi
- **Durum:** Geliştirildi.
- **Sınıflar:** `ResultsController`, `ResultService`; `ResultsViewModel`, `SubmissionDetailViewModel`.
- **API:** `GET /api/exams/{id}/results`, `GET /api/submissions/{id}`.
- **Tablolar:** `submissions`, `answers`, `exam_questions`, `users`, `class_members`.
- **Testler:** Geçti (otomatik): `ResultsAndLogsTests` 18. VT-35. Yayınlanmış sürümle pencere senaryosu geçti. Laboratuvar: Bildirildi (05.11, 2 bilgisayar).
- **Eksik:** 06.11 3 bilgisayarlı denemede sonuç ekranı açılmadı.

---

## 3. Sistem mimarisi

- **Model:** İstemci–sunucu, internetsiz. Eğitmen bilgisayarı sunucu ve PostgreSQL'i çalıştırır, öğrenciler aynı ağdan bağlanır.
- **Çözüm (5 uygulama + 2 test projesi):**

| Proje | Görev | Dosya | Satır |
|---|---|---|---|
| `ProgLab.Shared` | DTO, enum, hata kodu, hub olay adları | 17 | 489 |
| `ProgLab.Server` | Web API, SignalR, zamanlayıcı | 66 | 3930 |
| `ProgLab.Client.Core` | API istemcisi, kayıt kuyruğu, sayaç | 12 | 970 |
| `ProgLab.InstructorApp` | Eğitmen uygulaması | 47 | 3175 |
| `ProgLab.StudentApp` | Öğrenci uygulaması | 29 | 1486 |
| `ProgLab.Server.Tests` | xUnit testleri | 35 | 6442 |
| `ProgLab.LoadTest` | Yük testi | 1 | 208 |

  Sayımlar `.cs` ve `.axaml` içindir; migration'lar hariç.
- **Sunucu katmanları:** Controller → Service → EF Core (`AppDbContext`) → PostgreSQL. Hatalar `ApiException` ile fırlatılır, `ApiExceptionMiddleware` bunu ProblemDetails + `code` biçimine çevirir.
- **İletişim:**
  - REST/HTTPS (JSON): tüm iş komutları.
  - SignalR `/hubs/exam`: yalnız sunucudan istemciye bildirim. Olaylar `ExamPublished`, `ExamEnded`, `TimeSync`, `StudentConnected`, `StudentDisconnected`, `AnswerProgress`, `SubmissionReceived`, `SecondDeviceWarning`, `Ready`.
  - UDP 47777: sunucu 2 saniyede bir kendini duyurur (`DiscoveryBroadcaster`).
- **Gruplar:** `instructors`, `class-{id}`, `exam-{id}`.
- **Arka plan servisleri:** `ExamTimerService` (saniyede bir süresi dolanı tamamlar, 30 saniyede bir `TimeSync`), `DiscoveryBroadcaster`.
- **Güvenlik:** Öz imzalı sertifika (RSA 2048, 2 yıl, `CertificateProvider`), SHA-256 parmak izi; öğrenci ilk bağlantıda kaydeder, sonra yalnız ona güvenir (`CertificatePinner`).
- **Masaüstü:** Avalonia + MVVM (CommunityToolkit.Mvvm). `MainWindowViewModel.CurrentPage` hangi ekranın görüneceğini belirler.
- **Genişleme noktası (final):** `IGrader` listesi, soru tipine göre ekran şablonu. Final kodu yoktur; `ProjectDependencyTests` proje bağımlılıklarını denetler.

## 4. Kullanılan teknolojiler (csproj'den)

| Alan | Teknoloji |
|---|---|
| Dil, çatı | C#, .NET 10 |
| Masaüstü | Avalonia 12.1.5, CommunityToolkit.Mvvm 8.4.2 |
| Sunucu | ASP.NET Core Web API, SignalR (Client 10.0.14) |
| Veritabanı | PostgreSQL (kurulu sürüm 18), EF Core Relational/Design 10.0.14, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, EFCore.NamingConventions 10.0.1 |
| Güvenlik | JWT Bearer 10.0.14 (HS256), BCrypt.Net-Next 4.2.0 |
| Log | Serilog.AspNetCore 10.0.0, Serilog.Sinks.File 7.0.0 |
| Test | xUnit 2.9.3, Moq 4.21.0, Mvc.Testing 10.0.14, coverlet 6.0.4 |

## 5. Veritabanı yapısı

12 tablo, 2 migration (`InitialCreate`, `RequireExamDuration`). Adlandırma `snake_case`. Enumlar `smallint`: `UserRole`, `QuestionType`, `ExamKind`, `ExamStatus` (Draft 1, Published 2, Completed 3), `SubmissionStatus` (InProgress 1, Submitted 2, AutoSubmitted 3), `GradedBy` (Auto 1, Manual 2).

| Tablo | Amaç | Önemli alanlar |
|---|---|---|
| `users` | Eğitmen ve öğrenci | `username` (benzersiz), `password_hash`, `role`, `must_change_password`, `is_active`, `verified_at` |
| `classes` | Şube (entity: `SchoolClass`) | `name`+`term` benzersiz, `instructor_id` |
| `class_members` | Şube üyeliği | `class_id`, `student_id` |
| `categories` | Soru kategorisi (ağaç) | `name`, `parent_id` |
| `questions` | Soru, sürümlü | `type`, `default_points`, `version_no`, `previous_version_id`, `is_current`, `is_locked`, `is_deleted` |
| `question_options` | Şıklar | `is_correct`, `order_no` |
| `classic_answer_keys` | Kabul edilen cevaplar | `accepted_text`, `normalized_text` |
| `exams` | Sınav/ödev | `kind`, `status`, `duration_minutes`, `starts_at`, `ends_at`, `class_id` |
| `exam_questions` | Sınavdaki soru ve puan | `question_id`, `order_no`, `points` |
| `submissions` | Öğrenci gönderimi | `status`, `submitted_at`, `submit_count`, `total_score`, `graded_at`, `client_ip` |
| `answers` | Soru başına cevap | `selected_option_id`, `text_answer`, `score`, `graded_by`, `graded_note` |
| `activity_logs` | Olaylar | `event_type`, `user_id`, `exam_id`, `submission_id`, `ip`, `details` (jsonb) |

**Bütünlük kuralları (veritabanı düzeyinde):**
- Kısmi benzersiz indeks: soruda tek doğru şık (`filter: is_correct`).
- Benzersiz: `submissions(exam_id, student_id)`, `answers(submission_id, exam_question_id)`, `exam_questions(exam_id, question_id)` ve `(exam_id, order_no)`, `classic_answer_keys(question_id, normalized_text)`.
- CHECK: `ck_exams_duration`, `ck_exams_time_window` (`starts_at < ends_at`), `ck_exams_published_has_times`, `ck_exam_questions_points`, `ck_questions_default_points`.
- Eşzamanlılık: `exams` için `xmin` satır sürümü (`IsRowVersion`).
- Bağlantı havuzu en çok 40 (`AppDbContext.LimitPoolSize`). Yük testinde 100 eşzamanlı istek PostgreSQL'in 100 bağlantı sınırını aşınca eklendi.

**Soru sürümleme:** Yayınlanan sınavın soruları `is_locked` olur. Kilitli soru düzenlenince yeni sürüm oluşur (`CreateNewVersionAsync`), sınav eski sürüme bağlı kalır. Silme yumuşaktır (`is_deleted`).

## 6. Giriş ve kimlik doğrulama algoritması

Kaynak: `AuthService.LoginAsync`, `FirstLoginAsync`, `JwtTokenService`, `PasswordHasher`, `MustChangePasswordFilter`.

```text
GİRİŞ(kullanıcıAdı, parola, uygulama)
  kullanıcı = veritabanında bul
  kullanıcı yoksa:
      sahte özetle doğrulama yap (süre farkı olmasın); LOGIN_FAILED; 401 INVALID_CREDENTIALS
  parola özeti varsa ve parola uymuyorsa: LOGIN_FAILED; 401 INVALID_CREDENTIALS
  rol != uygulama ise: LOGIN_FAILED; 403 WRONG_APP_ROLE
  hesap pasifse: LOGIN_FAILED; 403 ACCOUNT_DISABLED
  parola özeti yoksa: LOGIN_FAILED; 409 FIRST_LOGIN_REQUIRED
  LOGIN kaydı yaz; JWT üret ve dön (token, bitiş, rol, ad, mustChangePassword)

İLK GİRİŞ(numara, e-posta, yeniParola)  [yalnız öğrenci]
  parolası zaten varsa: 409 ALREADY_VERIFIED
  e-posta uymuyorsa (büyük/küçük harf duyarsız): 401
  pasifse: 403
  parola < 8 karakter: 422 PASSWORD_TOO_SHORT
  BCrypt özetini ve verified_at'i yaz; FIRST_LOGIN kaydı; JWT dön
```

- **Parola:** BCrypt, work factor 11; en az 8 karakter. Düz metin saklanmaz.
- **Token:** HS256; `sub`, `role`, `name`; 4 saat geçerli (`Jwt:TokenLifetimeHours`); `ClockSkew = 0`; issuer/audience denetimi kapalı. SignalR'da `access_token` sorgu parametresiyle gelir.
- **Anahtar:** Yoksa ilk açılışta rastgele üretilip `appsettings.Local.json` dosyasına yazılır (`JwtKeyStore`).
- **Her istekte:** `MustChangePasswordFilter` hesabın güncel durumunu veritabanından kontrol eder (hesap yok/parolasız 401, pasif, parola değişikliği zorunlu). Hub bağlantısında da aynı denetim var.
- **Hız sınırı:** login ve first-login için IP başına dakikada 10 (sabit pencere), aşılırsa 429 `RATE_LIMIT`.
- **Eğitmen ilk girişi:** Seed geçici parolası, sonra zorunlu değişim.
- **Tasarım notu:** Şu an 403 `WRONG_APP_ROLE` doğru paroladan sonra döner; yanlış uygulamaya giren kullanıcıya parolanın doğru olduğu belli olur, bu kasıtlı kod sırasıdır.

## 7. Sınav oluşturma ve yayınlama akışı

Kaynak: `ExamService`, `ExamRules`, `ExamNotifier`.

```text
TASLAK(başlık, tür, şube, süre veya tarih aralığı)
  sınav: süre > 0 zorunlu; ödev: başlangıç < bitiş zorunlu; şube zorunlu
  status = Draft

SORU LİSTESİ(liste)   [yalnız taslak, transaction]
  aynı soru iki kez olamaz, sıra 1'den başlar ve benzersiz, puan > 0
  listeyi tümüyle değiştir; puan verilmezse sorunun varsayılan puanı

YAYINLA(id)   [tek transaction]
  taslak değilse 409 EXAM_NOT_DRAFT
  soru yoksa 422; silinmiş soru varsa 422; şubede öğrenci yoksa 422
  sınavsa: starts_at = şimdi, ends_at = şimdi + süre
  ödevse: girilen tarihler korunur, bitiş geçmişte olamaz
  tüm soruları is_locked = true yap; status = Published
  EXAM_PUBLISHED logu; commit
  commit sonrası: log dosyasına yaz; class-{id} grubuna ExamPublished gönder
```

- Sınav için yayınlama başlatmadır; süre öğrenciye değil sınava bağlıdır.
- Erken bitirme ve süre dolumu aynı yoldan geçer (`ExamCompletionService.CompleteAsync`).
- Bildirim başarısız olsa işlem bozulmaz (`SendSafelyAsync`); öğrenci lobiyi yenileyince görür.

## 8. Öğrencinin sınava katılma akışı

Kaynak: `SubmissionService.JoinAsync`, `GetPaperAsync`, `StudentExamService`, `ExamPaperViewModel`.

```text
BAĞLAN: UDP duyurusundan sunucuyu bul veya adresi elle yaz; sertifika parmak izini kaydet
GİRİŞ → LOBİ: yalnız üye olunan şubelerin yayınlanmış/tamamlanmış sınavları
KATIL(sınav)
  sınav görünür mü (taslak değil) → yoksa 404
  şube üyesi mi → değilse 403 NOT_IN_CLASS
  şu an pencere içinde mi → 409 EXAM_NOT_OPEN / EXAM_CLOSED
  sınav türü ve teslim edilmişse → 409 ALREADY_SUBMITTED
  gönderim yoksa oluştur (InProgress), varsa aynısını kullan
  client_ip güncelle; JOINED logu
  önceki IP farklıysa: SECOND_DEVICE logu, eğitmene uyarı (engellenmez)
  kalan süreyi sunucu saatinden hesapla ve dön
KÂĞIT: sorular + kayıtlı cevaplar (cevap anahtarı içermez, StudentQuestionDto)
SAYAÇ: Stopwatch ile geri sayar, TimeSync ile düzeltilir; sistem saati okunmaz
```

- Aynı anda çift katılım: benzersiz indeks tek gönderim bırakır, ikinci istek var olanı döner.
- Uygulama kapanıp açılırsa süre sıfırlanmaz, cevaplar sunucudan gelir.
- Bağlantı dönünce kalan süre yeniden alınır (`ExamPaperViewModel.ResyncAsync`).

## 9. Cevap kaydetme mekanizması

**İstemci (`AnswerSaveQueue`):**
```text
cevap değişti(soru, değer):
   bekleyenler[soru] = değer (eskisinin üzerine yazılır); gönderim zamanı = şimdi + 1 sn
işçi döngüsü:
   vakti gelen cevabı gönder
   başarılı → kuyruktan sil (gönderim sırasında yenisi geldiyse o kalır)
   ağ hatası → kuyrukta kalır, durum "Bağlantı yok", 3 sn sonra yeniden dene
   sunucu reddetti → yeniden denenmez, ekrana bildirilir
FlushAsync: bekleyenleri hemen gönder; bağlantı yoksa false döner
```
Flush kullanılan yerler: teslimden önce, süre bitmeden son 5 saniyede, lobiye dönüşte, Çıkış'ta ve pencere kapanırken (3 sn sınır; başarısızsa bir kez uyarır).

**Sunucu (`SaveAnswerAsync`), tek transaction:**
```text
gönderim satırını kilitle (SELECT ... FOR UPDATE); sahibi değilse 403 NOT_OWNER
pencere kapalıysa: SAVE_REJECTED logu, 409 EXAM_CLOSED (tolerans yok)
sınav türü ve teslim edilmişse: SAVE_REJECTED, 409 ALREADY_SUBMITTED
soru bu sınavda mı → 404; şık bu soruya ait mi → 422; metin ≤ 2000 → 422
INSERT ... ON CONFLICT (submission_id, exam_question_id) DO UPDATE
   cevap değiştiyse score, graded_by, graded_note silinir
last_saved_at yaz; teslim edilmiş ödev düzenlendiyse: InProgress, toplam puan silinir
commit; eğitmene AnswerProgress
```

**Teslim (`SubmitAsync`):** Aynı kilit ve pencere denetimi; `Submitted`, `submitted_at` (sunucu saati), `submit_count++`, puanlama, `SUBMITTED` logu, commit, sonra dosya ve `SubmissionReceived`.

**Süre sonu (`ExamCompletionService`):** `InProgress` gönderimler kilitlenir, `AutoSubmitted` yapılıp puanlanır, `AUTO_SUBMITTED` ve `EXAM_ENDED` loglanır, sınav `Completed`. Eşzamanlı tamamlamada `xmin` çakışması biri kazanır. Sunucu kapalıyken süre dolduysa açılıştaki ilk kontrol tamamlar.

**Ödev kararı (doküman boşluğu, kullanıcı onayladı):** Teslimden sonra düzenlenen ödev `InProgress` olur; yeniden teslim edilmezse bitişte son kayıt otomatik teslim edilir.

## 10. Otomatik puanlama algoritması

Kaynak: `Grading/Graders.cs`, `GradingService.cs`, `AnswerNormalizer.cs`. Strategy deseni: her soru tipinin `IGrader` sınıfı var, servis `QuestionType`'a göre seçer.

```text
PUANLA(gönderim):   [teslim veya otomatik teslimin transaction'ı içinde]
  toplam = 0
  sınavın her sorusu için (kilitli sürümün anahtarıyla):
     cevap elle puanlandıysa: toplam += cevap.puan; atla
     sonuç = puanlayıcı[soru.tip].puanla(soru, cevap)
     toplam += sonuç
     cevap satırı varsa: cevap.puan = sonuç; graded_by = Auto
  gönderim.total_score = toplam; graded_at = şimdi

ŞIKLI: seçilen == doğru şık ise sorunun sınav puanı, değilse 0 (negatif yok)

KLASİK: n = NORMALİZE(cevap)
        n boş değil ve kabul edilenlerin normalize hâlinden biriyle aynıysa tam puan, değilse 0

NORMALİZE(metin):
  boşsa ""
  tüm boşlukları teke indir, baştan ve sondan kırp
  sondaki . , ; : ! ? işaretlerini at
  tr-TR kültürüyle küçült (I→ı, İ→i)
  Unicode NFC
```

- Puanlama tekrar çalıştırılırsa aynı sonucu verir, `Manual` cevaba dokunmaz (test edildi).
- Cevap satırı yoksa soru 0 sayılır, satır açılmaz.
- Klasik puanlama metin karşılaştırmasıdır, anlam değerlendirmez.
- **Elle düzeltme** (`OverrideScoreAsync`): yalnız klasik soru (aksi 409 `NOT_CLASSIC_QUESTION`), teslim edilmiş olmalı (409 `INVALID_STATE`), puan 0 ile soru puanı arası ve en çok 2 ondalık (422 `SCORE_OUT_OF_RANGE`), not ≤ 300. `Manual` yazılır, toplam yeniden hesaplanır, `SCORE_OVERRIDDEN` loglanır.

## 11. Loglama sistemi

**İki yer:** `activity_logs` (esas kaynak, tüm olaylar) ve sınav başına `logs/exams/exam-<id>.log`. Ayrıca Serilog uygulama logu `logs/server-.log`.

**Olaylar (`ActivityEvents`):** `LOGIN`, `LOGIN_FAILED`, `FIRST_LOGIN`, `EXAM_PUBLISHED`, `EXAM_ENDED`, `JOINED`, `SECOND_DEVICE`, `SUBMITTED`, `AUTO_SUBMITTED`, `SAVE_REJECTED`, `SUBMIT_REJECTED`, `SCORE_OVERRIDDEN`. Giriş olayları dosyaya gitmez.

**Yazma sırası (`ActivityRecorder`):** Satır aynı transaction ile tabloya eklenir; dosya satırı commit'ten sonra yazılır, böylece geri alınan işlem dosyada görünmez. `details` içine satır metni `summary` olarak da yazılır.

**Dosya (`ExamLogWriter`):** Dosya başına tek `SemaphoreSlim`; paralel yazımda satırlar karışmaz (300 satırlık testle doğrulandı). Yazma hatası işlemi durdurmaz, uygulama loguna düşer. Gerçek çıktı (05.11):
```text
2026-11-05 20:05:50.880 +03:00 | SUBMITTED       | 2026900001 | Demo Öğrenci 1  | 127.0.0.1       | 2/2 cevap, 10.00 puan, 1. teslim
```

**Görüntüleme:** `LogQueryService` + `LogsViewModel`: olay, öğrenci no, zaman filtresi; sayfa başına 100; en yeniden eskiye; "Ham dosyayı göster". Dosya yoksa tablodan üretilir ve kaydedilir (dokümandaki 404 yerine onaylı karar).

**Bilinen eksikler:** Bağlantı olayları tabloya yazılmıyor. `SCORE_OVERRIDDEN` 16 karakter olduğundan ham dosyada o satır bir karakter kayıyor.

## 12. Gerçek test sonuçları

### Otomatik testler (06.11.2026, düzeltme sonrası)
**Toplam 473: 473 geçti.** `dotnet test -c Release` 4 kez, `-c Debug` 1 kez. Düzeltmeden önceki çalıştırmada 472/473 alınmıştı (bkz. Bölüm 13).

| Sınıf | Adet | Sınıf | Adet |
|---|---|---|---|
| `DatabaseSchemaTests` | 55 | `UserAndClassApiTests` | 10 |
| `ExamRulesTests` | 29 | `SubmitAndLiveViewModelTests` | 10 |
| `DatabaseRuleTests` | 22 | `CertificateTests` | 10 |
| `QuestionValidatorTests` | 22 | `GradingApiTests` | 10 |
| `SharedContractTests` | 22 | `ExamViewModelTests` | 10 |
| `StudentPaperApiTests` | 22 | `AnswerSaveQueueTests` | 9 |
| `ExamApiTests` | 20 | `QuestionBankViewModelTests` | 9 |
| `AuthApiTests` | 18 | `CategoryApiTests` | 8 |
| `ResultsAndLogsTests` | 18 | `ProjectDependencyTests` | 6 |
| `DiscoveryTests` | 16 | `DatabaseSeedTests` | 5 |
| `AnswerCollectionApiTests` | 15 | `ExamTimerAndLogFileTests` | 5 |
| `GraderTests` | 15 | `HealthEndpointTests` | 5 |
| `QuestionApiTests` | 14 | `ConnectionPoolTests` | 3 |
| `RealtimeTests` | 14 | `RawLogFileTests` | 2 |
| `CountdownClockTests` | 13 | `StudentConnectFlowTests` | 2 |
| `StudentStateRulesTests` | 13 | `StudentViewModelFlowTests` | 2 |
| `ServerAddressTests` | 12 | `AuthRateLimitTests`, `HubConnectionStateTests`, `InstructorFirstLoginTests`, `InstructorViewModelFlowTests`, `TimeSyncTests` | 1'er |
| `AnswerNormalizerTests`, `ExamPaperViewModelTests` | 11'er | | |

Testler `proglab_test` içinde geçici şemada çalışır, gerçek veriye dokunmaz. Test kodunda VT-08 ve VT-37 geçmez: VT-08 elle, VT-37 `scripts/load-test.ps1` ile yapılır. Diğer VT numaraları test kodunda yorum/ad olarak geçer.

### Yük testi (VT-37, 05.11.2026)
Sunucu ve sanal öğrenciler aynı bilgisayarda, **WiFi gecikmesini kapsamaz**. Bugün yeniden çalıştırılamadı (sunucu açıktı, betik port doluyken durur); o tarihten sonra bağlantı durumu koduna düzeltme yapıldı.

| İşlem | Adet | %95 (ms) | En çok (ms) | Hedef |
|---|---|---|---|---|
| Yayın bildirimi | 60 | 59 | 59 | 1000 |
| Katılım + kâğıt | 60 | 395 | 396 | — |
| Cevap kaydı | 481 | 15 | 52 | %95 < 300 |
| Teslim | 60 | 283 | 284 | — |
| Sonuç listesi | 1 | 32 | 32 | 1000 |

60 öğrenci: 0 hata, tüm puanlar ve loglar doğru. 100 öğrenci (20 sn, 3 tekrar): düzeltmeden sonra 0 hata; düzeltmeden önce 3–5 öğrenci 500 alıyordu.

### Tek bilgisayarda pencere senaryoları (UI Automation)
Gerçek sunucu ve uygulamalar, geçici şema. Aşama 8–9: 38/38, sunucu kapatıp açma dahil. Aşama 10–12: 25/25 ve 27/27. Yayınlanmış sürümle: 25/25.

### Laboratuvar

| Senaryo | 05.11 (2 bilgisayar) | 06.11 (3 bilgisayar, yarıda bırakıldı) |
|---|---|---|
| Birden çok öğrenci bilgisayarı | Yok, tek öğrenci | **Kayıtta var** |
| VT-08 sunucuyu listede bulma | — | Bildirildi |
| VT-17 yayın | Bildirildi | Bildirildi, 2 lobi, süre ölçülmedi |
| Otomatik kayıt | — | **Kayıtta var** |
| VT-27 saat değişimi | Bildirildi | Bildirildi |
| VT-24 ağ kopması | Bildirildi | **Tamamlanmadı**, A dönmedi |
| VT-19 geri dönüş | Yapılmadı | **Kayıtta var** |
| VT-21 ikinci cihaz | Yapılmadı | **Kayıtta var** |
| VT-30 sunucu yeniden başlatma | Yapılmadı | **Yapılmadı** |
| Teslim | Kayıtta var | Yapılmadı |
| Otomatik teslim ve puan | Kayıtta var | Kayıtta var (A 20.00, B 10.00) |
| VT-34 elle düzeltme | Kayıtta var | Yapılmadı |
| VT-35 sonuç listesi | Bildirildi | Yapılmadı |
| VT-36 log | Kayıtta var | Yapılmadı |

## 13. Rapora yazmadan önce bilmeniz gerekenler

1. **Kararsız test düzeltildi.** `HubConnectionStateTests` aralıklı kalıyordu (06.11 sabah 472/473). `ExamHubClient.OnReady`, ekrana haber vermeden önce `StartAsync`'i serbest bırakıyordu; sıra değiştirildi. Sonrasında 30 tekrarda (3000 bağlantı) ve 5 tam çalıştırmada kalmadı. Seyrek bir hata olduğu için bu güçlü bir işarettir, kesin kanıt değildir. Düzeltme sonrası pencere senaryosu çalıştırılmadı.
2. **Kabul kriteri 3 (3 bilgisayarla uçtan uca) tamamlanmadı**, ama bu tasarım dokümanının ölçütü; ders PDF'i sayı vermiyor. PDF'te yalnız finalde "sınıf ortamında çoklu öğrenciyle" denemesi geçer.
3. **Kriter 4 (başka üyenin kurulumu) yapılmadı.**
4. **Doküman ile kod farkları:**
   - Eklenenler: `GET /api/connections/students`, hub olayı `Ready`, hata kodları `SUBMISSION_NOT_FOUND`, `ANSWER_NOT_FOUND`.
   - Değişenler: `LOG_FILE_NOT_FOUND` kullanılmıyor; `SubmitConfirmDialog` ayrı pencere değil, sınav ekranında onay kutusu; dokümandaki `CertificatePinning` kodda `CertificatePinner`; VT-33 örneği "ISIK" yerine "IŞIK" (`ISIK` normalize edilince `ısık` olur, `ışık` ile eşleşmez).
5. **Diyagramlar:** Tasarım dokümanındaki şekiller (`diyagramlar/*.png`) koddan önce çizildi. Rapora koymadan class diyagramını koddaki adlarla karşılaştırın. Bu dosyaları ben incelemedim.
6. **PDF raporda şunları ister:** akış diyagramı, UML, class diyagramı, kaba kod. Bu notlardaki kaba kodlar koddan çıkarıldı; kendi anlatımınızla yazın.
7. **`.NET`, Avalonia sürümleri ve satır sayıları** bu commit içindir; kod değişirse güncelleyin.
