using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// Issue #420 review: öğretmen ilerleme ekranı ile veli özetinin ortak atama durumu kuralı — tablo halinde: instance yokken
/// pencere durumu (Scheduled / Expired / NotStarted) ve her instance durumu (EndTime dolu/boş).
/// </summary>
public class AssignmentStudentStatusRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    public static TheoryData<int, int?, WorksheetInstanceStatus?, bool, string> Cases() => new()
    {
        // startOffsetH, endOffsetH, instanceStatus, instanceHasEndTime, expected
        // Instance yok.
        { 2, 48, null, false, AssignmentStudentStatuses.Scheduled },   // pencere başlamadı
        { 2, null, null, false, AssignmentStudentStatuses.Scheduled },
        { -48, -1, null, false, AssignmentStudentStatuses.Expired },   // teslim tarihi geçti
        { -48, 24, null, false, AssignmentStudentStatuses.NotStarted },
        { -48, null, null, false, AssignmentStudentStatuses.NotStarted }, // açık uçlu
        { -48, 0, null, false, AssignmentStudentStatuses.NotStarted },  // EndAt == now: henüz geçmedi (< now)
        // Instance var — pencere durumundan bağımsız, instance belirler.
        { -48, 24, WorksheetInstanceStatus.Completed, true, AssignmentStudentStatuses.Completed },
        { -48, -1, WorksheetInstanceStatus.Completed, true, AssignmentStudentStatuses.Completed },
        { -48, 24, WorksheetInstanceStatus.Expired, true, AssignmentStudentStatuses.Expired },
        { -48, 24, WorksheetInstanceStatus.Expired, false, AssignmentStudentStatuses.Expired },
        { -48, 24, WorksheetInstanceStatus.Started, false, AssignmentStudentStatuses.InProgress },
        { -48, -1, WorksheetInstanceStatus.Started, false, AssignmentStudentStatuses.InProgress }, // geç kaldı ama başladı
        { -48, 24, WorksheetInstanceStatus.Started, true, AssignmentStudentStatuses.Completed },   // legacy: EndTime dolu
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_matches_table(int startOffsetH, int? endOffsetH, WorksheetInstanceStatus? status, bool hasEndTime, string expected)
    {
        DateTime? endAt = endOffsetH.HasValue ? Now.AddHours(endOffsetH.Value) : null;
        DateTime? instanceEnd = hasEndTime ? Now.AddHours(-1) : null;

        AssignmentStudentStatusRules.Resolve(Now.AddHours(startOffsetH), endAt, status, instanceEnd, Now).ShouldBe(expected);
    }

    [Fact]
    public void Every_instance_status_has_an_explicit_mapping()
    {
        // Yeni bir instance durumu eklenirse varsayılan dala (NotStarted) sessizce düşmesin — tabloya satır eklenmeli.
        foreach (var status in Enum.GetValues<WorksheetInstanceStatus>())
        {
            AssignmentStudentStatusRules.Resolve(Now.AddDays(-2), Now.AddDays(1), status, null, Now)
                .ShouldNotBe(AssignmentStudentStatuses.NotStarted, $"{status} eşlenmemiş");
        }
    }
}
