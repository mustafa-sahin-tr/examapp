using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;

namespace ExamApp.Api.Services
{
    public class ProgramService : IProgramService
    {
        private readonly AppDbContext _context;

        public ProgramService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<ProgramStepDto>> GetProgramStepsAsync()
        {
            return await _context.ProgramSteps
                .OrderBy(ps => ps.Order)
                .Select(ps => new ProgramStepDto
                {
                    Id = ps.Id,
                    Title = ps.Title,
                    Description = ps.Description,
                    Order = ps.Order,
                    Multiple = ps.Multiple,
                    Options = ps.Options.Select(o => new ProgramStepOptionDto
                    {
                        Id = o.Id,
                        Label = o.Label,
                        Value = o.Value,
                        Selected = o.Selected ?? false,
                        Icon = o.Icon,
                        NextStep = o.NextStep
                    }).ToList(),
                    Actions = ps.Actions.Select(a => new ProgramStepActionDto
                    {
                        Id = a.Id,
                        Label = a.Label,
                        Value = a.Value
                    }).ToList()
                })
                .ToListAsync();
        }

        public async Task<UserProgramDto> CreateUserProgramAsync(string userId, CreateProgramRequestDto request)
        {
            // Parse user selections to extract program configuration
            var programConfig = ParseUserSelections(request.UserSelections);

            // Parse provided dates or use defaults, always set Kind=Utc
            DateTime startDate = DateTime.UtcNow.Date;
            DateTime endDate = DateTime.UtcNow.Date.AddDays(30);

            if (!string.IsNullOrEmpty(request.StartDate) && DateTime.TryParse(request.StartDate, out var parsedStartDate))
            {
                startDate = DateTime.SpecifyKind(parsedStartDate.Date, DateTimeKind.Utc);
            }
            else
            {
                startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            }

            if (!string.IsNullOrEmpty(request.EndDate) && DateTime.TryParse(request.EndDate, out var parsedEndDate))
            {
                endDate = DateTime.SpecifyKind(parsedEndDate.Date, DateTimeKind.Utc);
            }
            else
            {
                endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
            }

            var userProgram = new UserProgram
            {
                UserId = userId,
                ProgramName = request.ProgramName,
                Description = request.Description,
                StudyType = programConfig.StudyType,
                StudyDuration = programConfig.StudyDuration,
                QuestionsPerDay = programConfig.QuestionsPerDay,
                SubjectsPerDay = programConfig.SubjectsPerDay,
                RestDays = programConfig.RestDays,
                DifficultSubjects = programConfig.DifficultSubjects,
                StartDate = startDate,
                EndDate = endDate
            };

            _context.UserPrograms.Add(userProgram);
            await _context.SaveChangesAsync();

            // Generate schedule based on program configuration
            await GenerateProgramScheduleAsync(userProgram);

            // Return DTO
            return await GetUserProgramDtoAsync(userProgram.Id);
        }

        public async Task<List<UserProgramDto>> GetUserProgramsAsync(string userId)
        {
            var userPrograms = await _context.UserPrograms
                .Where(up => up.UserId == userId && up.IsActive)
                .Include(up => up.Schedules)
                .Include(up => up.StudyPageSchedules)
                .ThenInclude(s => s.StudyPage)
                .ThenInclude(p => p.Images)
                .ToListAsync();

            return userPrograms.Select(MapToUserProgramDto).ToList();
        }

        public async Task<UserProgramDto?> GetUserProgramByIdAsync(string userId, int programId)
        {
            var userProgram = await _context.UserPrograms
                .Where(up => up.UserId == userId && up.Id == programId && up.IsActive)
                .Include(up => up.Schedules)
                .Include(up => up.StudyPageSchedules)
                .ThenInclude(s => s.StudyPage)
                .ThenInclude(p => p.Images)
                .FirstOrDefaultAsync();

            return userProgram != null ? MapToUserProgramDto(userProgram) : null;
        }

        public async Task<UserProgramDto?> AddStudyPageSchedulesAsync(string userId, int programId, ProgramStudyPageScheduleRequestDto request)
        {
            var userProgram = await _context.UserPrograms
                .Include(up => up.StudyPageSchedules)
                .FirstOrDefaultAsync(up => up.UserId == userId && up.Id == programId && up.IsActive);

            if (userProgram == null)
            {
                return null;
            }

            var studyPageIds = request.Items.Select(i => i.StudyPageId).Distinct().ToList();
            var studyPages = await _context.StudyPages
                .Where(p => studyPageIds.Contains(p.Id))
                .ToListAsync();

            var schedules = new List<UserProgramStudyPageSchedule>();
            foreach (var item in request.Items)
            {
                var pageExists = studyPages.Any(p => p.Id == item.StudyPageId);
                if (!pageExists)
                {
                    continue;
                }

                var startDate = DateTime.SpecifyKind(item.StartDate.Date, DateTimeKind.Utc);
                var endDate = DateTime.SpecifyKind(item.EndDate.Date, DateTimeKind.Utc);

                schedules.Add(new UserProgramStudyPageSchedule
                {
                    UserProgramId = userProgram.Id,
                    StudyPageId = item.StudyPageId,
                    StartDate = startDate,
                    EndDate = endDate
                });
            }

            if (schedules.Count > 0)
            {
                _context.UserProgramStudyPageSchedules.AddRange(schedules);
                await _context.SaveChangesAsync();
            }

            return await GetUserProgramByIdAsync(userId, programId);
        }

        public Task<bool> CompleteStudyPageAsync(string userId, int programId, int scheduleId, CancellationToken ct = default)
            => SetStudyPageCompletionAsync(userId, programId, scheduleId, completed: true, ct);

        public Task<bool> UncompleteStudyPageAsync(string userId, int programId, int scheduleId, CancellationToken ct = default)
            => SetStudyPageCompletionAsync(userId, programId, scheduleId, completed: false, ct);

        public async Task<bool> DeleteUserProgramAsync(string userId, int programId, CancellationToken ct = default)
        {
            // Sahiplik kontrolü: başka kullanıcının programı bulunamamış gibi (false → 404) davranır.
            var userProgram = await _context.UserPrograms
                .FirstOrDefaultAsync(up => up.UserId == userId && up.Id == programId, ct);

            if (userProgram == null)
            {
                return false;
            }

            if (userProgram.IsActive)
            {
                userProgram.IsActive = false;
                await _context.SaveChangesAsync(ct);
            }

            return true;
        }

        private async Task<bool> SetStudyPageCompletionAsync(string userId, int programId, int scheduleId, bool completed, CancellationToken ct)
        {
            // Sahiplik kontrolü schedule → program → UserId zinciri üzerinden tek sorguda yapılır;
            // eşleşme yoksa false (controller 404 döner, 403 değil — GetProgramById deseniyle tutarlı).
            // Soft-delete edilmiş (IsActive=false) programın schedule'ları da "yok" sayılır.
            var schedule = await _context.UserProgramStudyPageSchedules
                .FirstOrDefaultAsync(s => s.Id == scheduleId
                                       && s.UserProgramId == programId
                                       && s.UserProgram.UserId == userId
                                       && s.UserProgram.IsActive, ct);

            if (schedule == null)
            {
                return false;
            }

            if (completed)
            {
                // İdempotent: ilk tamamlanma zamanı korunur.
                if (!schedule.IsCompleted)
                {
                    schedule.IsCompleted = true;
                    schedule.CompletedDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);
                }
            }
            else
            {
                if (schedule.IsCompleted || schedule.CompletedDate != null)
                {
                    schedule.IsCompleted = false;
                    schedule.CompletedDate = null;
                    await _context.SaveChangesAsync(ct);
                }
            }

            return true;
        }

        private async Task<UserProgramDto> GetUserProgramDtoAsync(int userProgramId)
        {
            var userProgram = await _context.UserPrograms
                .Include(up => up.Schedules)
                .Include(up => up.StudyPageSchedules)
                .ThenInclude(s => s.StudyPage)
                .ThenInclude(p => p.Images)
                .FirstOrDefaultAsync(up => up.Id == userProgramId);

            return userProgram != null ? MapToUserProgramDto(userProgram) : null;
        }

        private UserProgramDto MapToUserProgramDto(UserProgram up)
        {
            var totalPageCount = up.StudyPageSchedules.Count;
            var completedPageCount = up.StudyPageSchedules.Count(s => s.IsCompleted);

            return new UserProgramDto
            {
                TotalPageCount = totalPageCount,
                CompletedPageCount = completedPageCount,
                ProgressPercentage = totalPageCount == 0 ? 0 : completedPageCount * 100 / totalPageCount,
                Id = up.Id,
                UserId = up.UserId,
                ProgramName = up.ProgramName,
                Description = up.Description,
                CreatedDate = up.CreatedDate,
                StartDate = up.StartDate,
                EndDate = up.EndDate,
                IsActive = up.IsActive,
                StudyType = up.StudyType,
                StudyDuration = up.StudyDuration,
                QuestionsPerDay = up.QuestionsPerDay,
                SubjectsPerDay = up.SubjectsPerDay,
                RestDays = up.RestDays,
                DifficultSubjects = up.DifficultSubjects,
                Schedules = up.Schedules.Select(s => new UserProgramScheduleDto
                {
                    Id = s.Id,
                    UserProgramId = s.UserProgramId,
                    ScheduleDate = s.ScheduleDate,
                    SubjectId = s.SubjectId,
                    SubjectName = s.SubjectName,
                    StudyDurationMinutes = s.StudyDurationMinutes,
                    QuestionCount = s.QuestionCount,
                    IsCompleted = s.IsCompleted,
                    CompletedDate = s.CompletedDate,
                    Notes = s.Notes
                }).ToList(),
                StudyPageSchedules = up.StudyPageSchedules.Select(s => new UserProgramStudyPageScheduleDto
                {
                    Id = s.Id,
                    UserProgramId = s.UserProgramId,
                    StudyPageId = s.StudyPageId,
                    StudyPageTitle = s.StudyPage != null ? s.StudyPage.Title : string.Empty,
                    StudyPageCoverImageUrl = s.StudyPage != null
                        ? s.StudyPage.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault()
                        : null,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    IsCompleted = s.IsCompleted,
                    CompletedDate = s.CompletedDate
                }).ToList()
            };
        }

        private (string StudyType, string StudyDuration, int? QuestionsPerDay, int SubjectsPerDay, string RestDays, string DifficultSubjects) ParseUserSelections(List<UserSelectionDto> selections)
        {
            string studyType = "time"; // Default to time-based study
            string studyDuration = "25-5"; // Default pomodoro duration
            int? questionsPerDay = null;
            int subjectsPerDay = 1;
            string restDays = "";
            string difficultSubjects = "";

            foreach (var selection in selections)
            {
                switch (selection.StepId)
                {
                    case 1: // Study type selection
                        studyType = selection.SelectedValues.FirstOrDefault() ?? "time";
                        break;
                    case 2: // Study duration selection (only for time-based study)
                        studyDuration = selection.SelectedValues.FirstOrDefault() ?? "25-5";
                        break;
                    case 3: // Questions per day selection
                        if (int.TryParse(selection.SelectedValues.FirstOrDefault(), out int questions))
                            questionsPerDay = questions;
                        break;
                    case 5: // Subjects per day selection
                        if (int.TryParse(selection.SelectedValues.FirstOrDefault(), out int subjects))
                            subjectsPerDay = subjects;
                        break;
                    case 6: // Rest days selection
                        var restDayValues = selection.SelectedValues.Where(v => v != "8"); // "8" means "Yok"
                        restDays = string.Join(",", restDayValues);
                        break;
                    case 7: // Difficult subjects selection  
                        var difficultSubjectValues = selection.SelectedValues.Where(v => v != "5"); // "5" means "Yok"
                        difficultSubjects = string.Join(",", difficultSubjectValues);
                        break;
                }
            }

            // If study type is "question", studyDuration is not needed but database requires it
            // Set a default value for question-based study
            if (studyType == "question" && studyDuration == "25-5")
            {
                studyDuration = "question-based"; // Placeholder value for question mode
            }

            return (studyType, studyDuration, questionsPerDay, subjectsPerDay, restDays, difficultSubjects);
        }

        private async Task GenerateProgramScheduleAsync(UserProgram userProgram)
        {
            if (userProgram.StartDate == null || userProgram.EndDate == null)
                return;

            var subjects = await _context.Subjects.Take(4).ToListAsync(); // Get available subjects
            var restDaysList = !string.IsNullOrEmpty(userProgram.RestDays)
                ? userProgram.RestDays.Split(',').Select(int.Parse).ToList()
                : new List<int>();

            var schedules = new List<UserProgramSchedule>();
            var currentDate = userProgram.StartDate.Value;

            while (currentDate <= userProgram.EndDate.Value)
            {
                // Skip rest days
                int dayOfWeek = ((int)currentDate.DayOfWeek == 0) ? 7 : (int)currentDate.DayOfWeek; // Sunday = 7
                if (restDaysList.Contains(dayOfWeek))
                {
                    currentDate = currentDate.AddDays(1);
                    continue;
                }

                // Create schedule for each subject per day
                for (int i = 0; i < userProgram.SubjectsPerDay && i < subjects.Count; i++)
                {
                    var subject = subjects[i];
                    var schedule = new UserProgramSchedule
                    {
                        UserProgramId = userProgram.Id,
                        ScheduleDate = currentDate,
                        SubjectId = subject.Id,
                        SubjectName = subject.Name,
                        Notes = string.Empty // Provide default empty string for Notes field
                    };

                    // Set study duration or question count based on study type
                    if (userProgram.StudyType == "time" && !string.IsNullOrEmpty(userProgram.StudyDuration))
                    {
                        var durationParts = userProgram.StudyDuration.Split('-');
                        if (durationParts.Length > 0 && int.TryParse(durationParts[0], out int minutes))
                        {
                            schedule.StudyDurationMinutes = minutes;
                        }
                    }
                    else if (userProgram.StudyType == "question" && userProgram.QuestionsPerDay.HasValue)
                    {
                        schedule.QuestionCount = userProgram.QuestionsPerDay.Value / userProgram.SubjectsPerDay;
                    }

                    schedules.Add(schedule);
                }

                currentDate = currentDate.AddDays(1);
            }

            _context.UserProgramSchedules.AddRange(schedules);
            await _context.SaveChangesAsync();
        }

        // ...existing commented methods...
    }
}
