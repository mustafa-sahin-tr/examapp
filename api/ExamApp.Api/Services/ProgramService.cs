using System.Collections.Generic;
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
                StartDate = DateTime.UtcNow.Date,
                EndDate = DateTime.UtcNow.Date.AddDays(30) // Default 30-day program
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
            return await _context.UserPrograms
                .Where(up => up.UserId == userId)
                .Select(up => new UserProgramDto
                {
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
                    }).ToList()
                })
                .ToListAsync();
        }

        private async Task<UserProgramDto> GetUserProgramDtoAsync(int userProgramId)
        {
            return await _context.UserPrograms
                .Where(up => up.Id == userProgramId)
                .Select(up => new UserProgramDto
                {
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
                    }).ToList()
                })
                .FirstOrDefaultAsync();
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
