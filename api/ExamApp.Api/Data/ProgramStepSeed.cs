using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace ExamApp.Api.Data
{
    public static class ProgramStepSeed
    {
        public static void SeedData(ModelBuilder modelBuilder)
        {
            var programStepsToSeed = new List<ProgramStep>();
            var programStepOptionsToSeed = new List<ProgramStepOption>();
            var programStepActionsToSeed = new List<ProgramStepAction>();

            int currentOptionId = 1;
            int currentActionId = 1;

            // Data provided by the user
            var rawProgramSteps = new[]
            {
                new { Id = 1, Order = 1, Title = "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?", Description = "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?", Multiple = false,
                    Options = new[] {
                        new { Label = "Süreli Çalışma", Value = "time", Icon = "timer", NextStep = (int?)2 },
                        new { Label = "Soru Sayısı Takipli Çalışma", Value = "question", Icon = "quiz", NextStep = (int?)3 }
                    },
                    Actions = new[] { new { Label = default(string), Value = default(string) } }.Take(0).ToArray()
                },
                new { Id = 2, Order = 2, Title = "Sana uygun olan çalışma süresini seçebilirsin.", Description = "Sana uygun olan çalışma süresini seçebilirsin.", Multiple = false,
                    Options = new[] {
                        new { Label = "25 dakika çalışma 5 dakika ara", Value = "25-5", Icon = "hourglass_empty", NextStep = (int?)5 },
                        new { Label = "30 dakika çalışma 10 dakika ara", Value = "30-10", Icon = "hourglass_top", NextStep = (int?)5 },
                        new { Label = "40 dakika çalışma 10 dakika ara", Value = "40-10", Icon = "hourglass_bottom", NextStep = (int?)5 },
                        new { Label = "50 dakika çalışma 10 dakika ara", Value = "50-10", Icon = "hourglass_full", NextStep = (int?)5 }
                    },
                    Actions = new[] { new { Label = default(string), Value = default(string) } }.Take(0).ToArray()
                },
                new { Id = 3, Order = 3, Title = "Bir dersten bir günde kaç soru çözersin?", Description = "Bir dersten bir günde kaç soru çözersin?", Multiple = false,
                    Options = new[] {
                        new { Label = "8", Value = "8", Icon = "note", NextStep = (int?)5 },
                        new { Label = "12", Value = "12", Icon = "library_books", NextStep = (int?)5 },
                        new { Label = "16", Value = "16", Icon = "auto_stories", NextStep = (int?)5 }
                    },
                    Actions = new[] { new { Label = default(string), Value = default(string) } }.Take(0).ToArray()
                },
                // Id = 4 was a dead duplicate of step 1 (no option pointed to it); removed in FixProgramStepWizardDeadEnd.
                new { Id = 5, Order = 5, Title = "Bir günde kaç farklı ders çalışmak istersin?", Description = "Bir günde kaç farklı ders çalışmak istersin?", Multiple = false,
                    Options = new[] {
                        new { Label = "1", Value = "1", Icon = "filter_1", NextStep = (int?)6 },
                        new { Label = "2", Value = "2", Icon = "filter_2", NextStep = (int?)6 },
                        new { Label = "3", Value = "3", Icon = "filter_3", NextStep = (int?)6 }
                    },
                    Actions = new[] { new { Label = default(string), Value = default(string) } }.Take(0).ToArray()
                },
                new { Id = 6, Order = 6, Title = "Ders çalışamayacağın gün var mı?", Description = "Ders çalışamayacağın gün var mı?", Multiple = true,
                    Options = new[] {
                        new { Label = "Pazartesi", Value = "1", Icon = "looks_one", NextStep = (int?)7 },
                        new { Label = "Salı", Value = "2", Icon = "looks_two", NextStep = (int?)7 },
                        new { Label = "Çarşamba", Value = "3", Icon = "looks_3", NextStep = (int?)7 },
                        new { Label = "Perşembe", Value = "4", Icon = "looks_4", NextStep = (int?)7 },
                        new { Label = "Cuma", Value = "5", Icon = "looks_5", NextStep = (int?)7 },
                        new { Label = "Cumartesi", Value = "6", Icon = "weekend", NextStep = (int?)7 },
                        new { Label = "Pazar", Value = "7", Icon = "wb_sunny", NextStep = (int?)7 },
                        new { Label = "Yok", Value = "8", Icon = "event_available", NextStep = (int?)7 }
                    },
                    Actions = new[] { new { Label = default(string), Value = default(string) } }.Take(0).ToArray()
                },
                new { Id = 7, Order = 7, Title = "Çalışırken zorlandığın ders / dersler hangileri?", Description = "Çalışırken zorlandığın ders / dersler hangileri?", Multiple = true,
                    Options = new[] {
                        // Last step: NextStep = null means the wizard proceeds to the "create program" form.
                        new { Label = "Hayat Bilgisi", Value = "1", Icon = "home", NextStep = (int?)null },
                        new { Label = "Türkçe", Value = "2", Icon = "spellcheck", NextStep = (int?)null },
                        new { Label = "Matematik", Value = "3", Icon = "calculate", NextStep = (int?)null },
                        new { Label = "Fen Bilimleri", Value = "4", Icon = "science", NextStep = (int?)null },
                        new { Label = "Yok", Value = "5", Icon = "sentiment_satisfied", NextStep = (int?)null }
                    },
                    Actions = new[] { new { Label = default(string), Value = default(string) } }.Take(0).ToArray()
                }
            };

            foreach (var rawStep in rawProgramSteps)
            {
                // Option Ids 10-11 belonged to the removed step 4; skip them so the
                // remaining option Ids stay identical to the rows already in the database.
                if (rawStep.Id == 5 && currentOptionId == 10)
                {
                    currentOptionId = 12;
                }

                programStepsToSeed.Add(new ProgramStep
                {
                    Id = rawStep.Id,
                    Title = rawStep.Title,
                    Description = rawStep.Description,
                    Order = rawStep.Order,
                    Multiple = rawStep.Multiple
                });

                foreach (var rawOption in rawStep.Options)
                {
                    programStepOptionsToSeed.Add(new ProgramStepOption
                    {
                        Id = currentOptionId++,
                        ProgramStepId = rawStep.Id,
                        Label = rawOption.Label,
                        Value = rawOption.Value,
                        Selected = false, // Defaulting to false as per original data structure
                        Icon = rawOption.Icon,
                        NextStep = rawOption.NextStep
                    });
                }

                foreach (var rawAction in rawStep.Actions) // Removed .Cast<dynamic>()
                {
                    programStepActionsToSeed.Add(new ProgramStepAction
                    {
                        Id = currentActionId++,
                        ProgramStepId = rawStep.Id,
                        Label = rawAction.Label,
                        Value = rawAction.Value
                    });
                }
            }

            modelBuilder.Entity<ProgramStep>().HasData(programStepsToSeed);
            modelBuilder.Entity<ProgramStepOption>().HasData(programStepOptionsToSeed);
            modelBuilder.Entity<ProgramStepAction>().HasData(programStepActionsToSeed);
        }
    }
}
