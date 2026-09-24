namespace ExamApp.Api.Services.StudyLinks;

/// <summary>
/// Çalışma linki yönetim işlemini yapan kullanıcı (issue #61). Controller doğrulanmış profil + Keycloak "Admin" rolünden
/// kurar. <paramref name="IsAdmin"/> yalnızca token'daki Admin rolünden gelir; client'tan alınmaz.
/// Admin: tüm linkler üzerinde tam yetki. Teacher: yalnızca ONAYLI ise ve güncelleme/silmede yalnızca kendi linki.
/// </summary>
/// <param name="UserId">Profil kullanıcı kimliği (CreatedByUserId / UpdateUserId / audit ActorUserId).</param>
/// <param name="Name">Görünen ad (CreatedByName / UpdatedByName).</param>
/// <param name="Role">"Admin" | "Teacher" — CreatedByRole ve audit ActorRole'a yazılır.</param>
/// <param name="IsAdmin">Admin muafiyeti (onay + sahiplik kontrolü atlanır).</param>
public sealed record StudyLinkActor(int UserId, string Name, string Role, bool IsAdmin);
