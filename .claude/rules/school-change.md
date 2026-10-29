# Okul değişimi — davranış matrisi (#194, #540)

Kullanıcı okulunu kendisi değiştiremez (öğrenci kayıttan sonra kilitli, öğretmen 409); tek yol admin:
`PUT api/admin/students/{id}/school`, `PUT api/admin/teachers/{id}/school`
(`AdminStudentSchoolService`, `AdminTeacherSchoolService`). Yetki kararları claim'e değil DB'den çözülen okula bakar
(`UserSchoolResolver`, `SchoolAccessPolicy`); değişiklikten sonra profil önbelleği düşer, Keycloak `school_id` best-effort
güncellenir. Davranış değişikliği yapacaksan bu tabloyu ve testlerini birlikte güncelle.

## Matris

| Konu | Öğrenci okul değiştirince | Öğretmen okul değiştirince | Test |
|---|---|---|---|
| Liderlik (K1) | Canlı hesap, snapshot yok: tüm XP'siyle yeni okul sıralamasına girer, eski okulunkinden geriye dönük düşer; global değişmez | — | `LeaderboardServiceSchoolScopeTests.School_TransferredStudentMovesWithAllXpToNewSchoolRanking_issue540` |
| `SchoolOnly` sınavlar (K2, #539) | — | Silinmişler dahil hepsi okul güncellemesiyle aynı transaction'da `Private`; sayı yanıtta `privatizedWorksheetCount` ve audit `AffectedCount`'ta; eski okula yapılmış atamalar çözülmeye devam eder | `AdminTeacherSchoolServiceTests` (#539 bölümü) |
| Yarım oturum (K3) | İptal edilmez. `SaveAnswer` / `EndTest` yalnız sahipliğe bakar, sonuç öğrencinin geçmişine yazılır. Normal görünürlükte `start-test` aynı oturumu döner. **Boşluk:** `Restricted` sayfada `start-test` erişimi önce denetler, transferden sonra 403 → UI'dan devam edilemez (#643) | — | `AdminStudentSchoolServiceTests.Session_started_before_the_transfer_can_be_finished_after_it_issue540` |
| Sınıf atamaları | Eski okulun sınıf atamaları hemen kaybolur, yenininkiler görünür; doğrudan (StudentId) ve platform geneli atamalar kalır | Atama kendi `SchoolId`'sini taşır (K4): eski okul öğrencileri için `EndAt`'e kadar aktif, otomatik sonlanmaz; yeni atamalar güncel okulla damgalanır | `AdminStudentSchoolServiceTests.Grade_assignments_follow_...`, `AdminTeacherSchoolServiceTests.Old_school_assignment_stays_active_until_EndAt_...` |
| Öğretmenin görünümü (K4) | Eski okulun öğretmenleri öğrenciyi listede / ilerlemede görmez | Eski atamayı kendi listesinde görür (düzenler, sonlandırır) ama eski okulun öğrencisini ve **sonucunu** görmez: takip, etkinlik analizi, panel uçları, açık uçlu kuyruk hepsi `SchoolAccessPolicy` kapsamından geçer (değerlendirilmemiş açık uçlu cevaplar da kuyruktan düşer) | `AdminTeacherSchoolServiceTests.After_school_change_...`, `...Old_school_assignment_stays_active_until_EndAt_...` |
| Geçmiş sonuçlar | Silinmez / taşınmaz, öğrenciyle gider; `StudentDailyXps` okul sütunu taşımaz | — | |
| Yorumlar | Yorum yazıldığı okulu damgalar (`AuthorSchoolId`); öğretmenin sabitleme / cevap yetkisi güncel okulu ile yorumun okulu eşitse vardır → eski okul yorumlarında düşer | aynı | `WorksheetCommentAssignmentPinSchoolTests` |
| DM (K8) | Konuşma silinmez, geçmiş iki tarafça okunur; ilişki (atama zinciri, açıksa aynı okul) bitince iki taraf da yazamaz (`CanSend=false`, öğrenci 403 `CannotMessageTeacher`, öğretmen 403 `RelationshipEnded`), UI giriş alanını kapatır; yeni okulda yeni ilişki doğar | aynı | `DirectMessageServiceTests.Student_changing_school_...`, `Teacher_changing_school_..._issue540`, `dm-conversation.component.spec.ts` |
| Veli bağı, bağımsız öğretmen randevuları | Okuldan bağımsız, etkilenmez | — | |
| Yeni okulda onay (K7) | — | Admin kararı yeterli; askıdaki / hesabı onaysız / onaylı bağımsız öğretmen 409 | `AdminTeacherSchoolServiceTests` |
| Okulsuz yapma (K6, #541) | `{ "schoolId": null }` (alan yoksa 400): `SchoolId`, `SchoolVerifiedAt`, `SchoolVerifiedByUserId` temizlenir, Keycloak `school_id` kaldırılır, audit `ToSchoolId=null`; zaten okulsuzsa `changed=false`. Eski okulun bütün kapsamı kalkar, doğrudan / platform geneli atamalar kalır, okul liderliği uygulanamaz. Admin dialog'unda "Okulsuz yap" kipi + onay kutusu | Yapılmaz: `schoolId: null` → 400 (kullanıcı kendi register akışını kullanır) | `AdminStudentSchoolServiceTests` (#541), `AdminController{Student,Teacher}SchoolTests` |
| Bildirim (K5, #542) | Okul gerçekten değişince (`changed=true` ve okul farklı; yalnız üyelik doğrulaması / talep temizliği değil) okul yazımıyla aynı transaction'da `SchoolChangedEvent`: öğrenciye ve o an Active her veliye birer tane (tek alıcı / event). BadgeService `SchoolChangedConsumer` uygulama içi bildirim yazar: değişti / belirlendi / kaldırıldı (#541) metinleri, alıcının dili; veli bildirimi çocuğun paneline gider | Öğretmene tek event; mesaj #539 ile özel yapılan sınav sayısını içerir (0 ise cümle yok) | `Admin{Student,Teacher}SchoolServiceTests` (#542), `SchoolChangedConsumerTests`, `SchoolChangedRabbitPermissionsTests` |
| Okul geçmişi (K9) | Yalnız `AdminUserActionLogs` (From/To okul, sonuç, #539 `AffectedCount`); kullanıcıya görünür geçmiş ekranı yok | aynı | `Admin{Student,Teacher}SchoolServiceTests` |

Aynı okul isteği `changed=false` döner, audit / önbellek / Keycloak'a dokunmaz (öğretmende bekleyen okul talebi temizleniyorsa
`changed=true` ama `SchoolOnly` sınavlara dokunulmaz).

## Kapsam dışı (#194 kararları)
Kullanıcının kendi okul değişimi / transfer talebi, çoklu okul üyeliği, liderlik snapshot'ı, okul geçmişi ekranı, öğretmeni admin
eliyle bağımsız yapmak, sınıf (grade) değişikliği, atamaların yeni okula taşınması, toplu transfer.
