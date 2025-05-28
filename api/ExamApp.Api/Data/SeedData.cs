using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace ExamApp.Api.Data
{
    public static class SeedData
    {
        public static void Initialize(AppDbContext context)
        {
            // Veritabanında zaten ProgramStep varsa seedlemeyi atla
            if (context.ProgramSteps.Any())
            {
                return;   // DB has been seeded
            }

            var programSteps = new List<ProgramStep>
            {
                new ProgramStep
                {
                    Id = 1,
                    Title = "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?",
                    Description = "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?",
                    Order = 1,
                    Multiple = false,
                    Options = new List<ProgramStepOption>
                    {
                        new ProgramStepOption { Label = "Süreli Çalışma", Value = "time", Selected = false, Icon = "icons/question-mark.svg", NextStep = 2 },
                        new ProgramStepOption { Label = "Soru Sayısı Takipli Çalışma", Value = "question", Selected = false, Icon = "icons/timer.svg", NextStep = 3 },
                    },
                    Actions = new List<ProgramStepAction>()
                },
                new ProgramStep
                {
                    Id = 2,
                    Title = "Sana uygun olan çalışma süresini seçebilirsin.",
                    Description = "Sana uygun olan çalışma süresini seçebilirsin.",
                    Order = 2,
                    Multiple = false,
                    Options = new List<ProgramStepOption>
                    {
                        new ProgramStepOption { Label = "25 dakika çalışma 5 dakika ara", Value = "25-5", Selected = false, Icon = "icons/question-mark.svg", NextStep = 5 },
                        new ProgramStepOption { Label = "30 dakika çalışma 10 dakika ara", Value = "30-10", Selected = false, Icon = "icons/question-mark.svg", NextStep = 5 },
                        new ProgramStepOption { Label = "40 dakika çalışma 10 dakika ara", Value = "40-10", Selected = false, Icon = "icons/question-mark.svg", NextStep = 5 },
                        new ProgramStepOption { Label = "50 dakika çalışma 10 dakika ara", Value = "50-10", Selected = false, Icon = "icons/question-mark.svg", NextStep = 5 },
                    },
                    Actions = new List<ProgramStepAction>()
                },
                new ProgramStep
                {
                    Id = 3,
                    Title = "Bir dersten bir günde kaç soru çözersin?",
                    Description = "Bir dersten bir günde kaç soru çözersin?",
                    Order = 3,
                    Multiple = false,
                    Options = new List<ProgramStepOption>
                    {
                        new ProgramStepOption { Label = "8", Value = "8", Selected = false, Icon = "icons/question-mark.svg", NextStep = 5 },
                        new ProgramStepOption { Label = "12", Value = "12", Selected = false, Icon = "icons/question-mark.svg", NextStep = 5 },
                        new ProgramStepOption { Label = "16", Value = "16", Selected = false, Icon = "icons/question-mark.svg", NextStep = 5 },
                    },
                    Actions = new List<ProgramStepAction>()
                },
                new ProgramStep
                {
                    Id = 4,
                    Title = "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin",
                    Description = "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin",
                    Order = 4,
                    Multiple = false,
                    Options = new List<ProgramStepOption>
                    {
                        new ProgramStepOption { Label = "Süreli Çalışma", Value = "time", Selected = false, Icon = "icons/question-mark.svg", NextStep = -1 },
                        new ProgramStepOption { Label = "Soru Sayısı Takipli Çalışma", Value = "question", Selected = false, Icon = "icons/question-mark.svg", NextStep = -1 },
                    },
                    Actions = new List<ProgramStepAction>()
                },
                new ProgramStep
                {
                    Id = 5,
                    Title = "Bir günde kaç farklı ders çalışmak istersin?",
                    Description = "Bir günde kaç farklı ders çalışmak istersin?",
                    Order = 5,
                    Multiple = false,
                    Options = new List<ProgramStepOption>
                    {
                        new ProgramStepOption { Label = "1", Value = "1", Selected = false, Icon = "icons/one-svgrepo-com.svg", NextStep = 6 },
                        new ProgramStepOption { Label = "2", Value = "2", Selected = false, Icon = "icons/two-svgrepo-com.svg", NextStep = 6 },
                        new ProgramStepOption { Label = "3", Value = "3", Selected = false, Icon = "icons/three-svgrepo-com.svg", NextStep = 6 },
                    },
                    Actions = new List<ProgramStepAction>()
                },
                new ProgramStep
                {
                    Id = 6,
                    Title = "Ders çalışamayacağın gün var mı?",
                    Description = "Ders çalışamayacağın gün var mı?",
                    Order = 6,
                    Multiple = true,
                    Options = new List<ProgramStepOption>
                    {
                        new ProgramStepOption { Label = "Pazartesi", Value = "1", Selected = false, Icon = "icons/monday-svgrepo-com.svg", NextStep = 7 },
                        new ProgramStepOption { Label = "Salı", Value = "2", Selected = false, Icon = "icons/tuesday-svgrepo-com.svg", NextStep = 7 },
                        new ProgramStepOption { Label = "Çarşamba", Value = "3", Selected = false, Icon = "icons/wednesday-svgrepo-com.svg", NextStep = 7 },
                        new ProgramStepOption { Label = "Perşembe", Value = "4", Selected = false, Icon = "icons/thursday-svgrepo-com.svg", NextStep = 7 },
                        new ProgramStepOption { Label = "Cuma", Value = "5", Selected = false, Icon = "icons/friday-svgrepo-com.svg", NextStep = 7 },
                        new ProgramStepOption { Label = "Cumartesi", Value = "6", Selected = false, Icon = "icons/saturday-svgrepo-com.svg", NextStep = 7 },
                        new ProgramStepOption { Label = "Pazar", Value = "7", Selected = false, Icon = "icons/sunday-svgrepo-com.svg", NextStep = 7 },
                        new ProgramStepOption { Label = "Yok", Value = "8", Selected = false, Icon = "icons/null-svgrepo-com.svg", NextStep = 7 },
                    },
                    Actions = new List<ProgramStepAction>()
                },
                new ProgramStep
                {
                    Id = 7,
                    Title = "Çalışırken zorlandığın ders / dersler hangileri?",
                    Description = "Çalışırken zorlandığın ders / dersler hangileri?",
                    Order = 7,
                    Multiple = true,
                    Options = new List<ProgramStepOption>
                    {
                        new ProgramStepOption { Label = "Hayat Bilgisi", Value = "1", Selected = false, Icon = "icons/home-svgrepo-com.svg", NextStep = 8 },
                        new ProgramStepOption { Label = "Türkçe", Value = "2", Selected = false, Icon = "icons/alphabet-svgrepo-com.svg", NextStep = 8 },
                        new ProgramStepOption { Label = "Matematik", Value = "3", Selected = false, Icon = "icons/math-svgrepo-com.svg", NextStep = 8 },
                        new ProgramStepOption { Label = "Fen Bilimleri", Value = "4", Selected = false, Icon = "icons/world-svgrepo-com.svg", NextStep = 8 },
                        new ProgramStepOption { Label = "Yok", Value = "5", Selected = false, Icon = "icons/null-svgrepo-com.svg", NextStep = 8 },
                    },
                    Actions = new List<ProgramStepAction>()
                },
                new ProgramStep
                {
                    Id = 8,
                    Title = "Artık programını oluşturmaya hazırsın",
                    Description = "Artık programını oluşturmaya hazırsın",
                    Order = 8,
                    Multiple = false,
                    Options = new List<ProgramStepOption>(),
                    Actions = new List<ProgramStepAction>
                    {
                        new ProgramStepAction { Label = "Programı Oluştur", Value = "CreateProgram" },
                        new ProgramStepAction { Label = "Vazgeç", Value = "Cancel" },
                    }
                }
            };

            foreach (var step in programSteps)
            {
                // Önce ProgramStep'i ekleyip ID'sinin oluşmasını sağlıyoruz
                context.ProgramSteps.Add(step);
            }
            context.SaveChanges(); // Değişiklikleri kaydet


            // Alternatif olarak, eğer ID'leri manuel set etmek istemiyorsanız ve veritabanının atamasını istiyorsanız:
            // ProgramStep'leri ekledikten sonra SaveChanges() çağırıp, ardından Option ve Action'ları ekleyebilirsiniz.
            // Ya da Fluent API ile HasData kullanarak ID'leri belirleyebilirsiniz.
            // Şimdilik yukarıdaki yöntem daha basit olacaktır.
        }
    }
}
