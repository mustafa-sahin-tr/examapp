# Diğer özellik notları

Eskiden `.claude/rules/local-dev.md` içindeydi; 2026-11-01'de taşındı. Okul değişimi matrisi: `.claude/rules/school-change.md`.

**Issue #501 — profil sayıları ve veli rozet backfill'i.** Öğrenci profilindeki (`GET api/student/profile`) çözülen / doğru / yanlış
ve tamamlanan test sayıları artık istatistik ekranıyla aynı kuraldan (`GetGroupedStudentStatistics`: tamamlanmış, etkinlik olmayan
oturumlar, açık uçlu hariç), sıra genel liderlik tablosundan (#193) gelir; diğer uçların döndüğü `StudentProfileDto`'da bu alanlar 0.
Kimsenin yazmadığı `StudentPointHistories` / `StudentBadges` / `Leaderboards` / `StudentRewards` okunmuyor (tabloların kaldırılması ayrı
karar); profildeki "Kazanılan ödül" satırı ve DTO'daki `Badges` / `TotalRewards` kalktı. #422'den önce kazanılmış rozetleri veli paneline
taşımak için BadgeService dizininde bir kez: `dotnet run -- backfill-student-badges [--dry-run]` (her `BadgeEarned` için outbox'a
`StudentBadgeEarnedEvent`; exam API consumer'ı (öğrenci, rozet) UNIQUE ile idempotent — ikinci çalıştırma çift kayıt üretmez; Production'da
`backfill-student-points` gibi `--allow-production --confirm`). Migration yok.

**Issue #539 — öğretmen transferinde "Okulum" sınavları özel olur.** `PUT api/admin/teachers/{id}/school` okul gerçekten
değişince (aynı okul / yalnız bekleyen talebin kapanması değil) öğretmenin `TeacherSharing=SchoolOnly` sınavları (silinmişler dahil)
okul güncellemesiyle **aynı transaction'da** `Private` olur; sayısı yanıtta `privatizedWorksheetCount` ve `AdminUserActionLogs.AffectedCount`'ta.
Herhangi bir adım başarısızsa ne okul ne görünürlük değişir. Öğrenci erişimi `TeacherSharing`'e bakmaz: eski okula yapılmış atamalar
çözülmeye devam eder. Admin dialog'u onaydan önce sayıyı `GET api/admin/teachers/{id}/school-change-impact` (`schoolOnlyWorksheetCount`,
silinmemişler) ile gösterir. Yeniden paylaşım öğretmenin elinde (elle tekrar "Okulum"). Migration `AddAdminUserActionAffectedCount` exam API
açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).
