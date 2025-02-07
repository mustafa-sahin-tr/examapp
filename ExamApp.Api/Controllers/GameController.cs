using ExamApp.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/*
    🟢 GET /api/game/leaderboard/{timePeriod} - Liderlik tablosunu getir
    🟢 GET /api/game/special-events - Özel etkinlikleri getir

    Bu tasarımla motivasyonu artıracak, öğrencilerin sistemde daha fazla vakit geçirmesini sağlayacak ve uzun vadede öğrenmeyi teşvik edecek bir oyunlaştırma modeli oluşturabiliriz. Günlük görevler, rozetler, seviyeler ve liderlik tablosu gibi sistemlerle öğrenciler hem eğlenecek hem de eğitim sürecini daha interaktif bir şekilde deneyimleyecek.

    💡 Bu sistem, öğrencileri ders çalışmaya teşvik ederken aynı zamanda sağlıklı bir rekabet ortamı yaratacaktır. 🚀
*/

namespace ExamApp.Api.Controllers
{
    /// <summary>
    /// Bu yapıyı kullanarak öğrencilerin aktif katılımını artırabiliriz.
    /// 🎖 Liderlik Tablosu sayesinde en başarılı öğrencileri ödüllendirebiliriz.
    /// 🏆 Özel Etkinlikler ile öğrencilere ekstra motivasyon kazandırabiliriz.
    /// 🎁 Ödül sistemi ile öğrencilerin puanlarını faydalı hediyelere dönüştürmesini sağlayabiliriz.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;
        public GameController(AppDbContext context)
        {
            _context = context;
        }


        [HttpGet("leaderboard/{timePeriod}")]
        public async Task<IActionResult> GetLeaderboard(string timePeriod)
        {
            var leaderboard = await _context.Leaderboards
                .Where(lb => lb.TimePeriod == timePeriod)
                .OrderBy(lb => lb.Rank)
                .ToListAsync();

            return Ok(leaderboard);
        }

        [HttpGet("special-events")]
        public async Task<IActionResult> GetSpecialEvents()
        {
            var events = await _context.SpecialEvents
                .Where(e => e.StartDate <= DateTime.UtcNow && e.EndDate >= DateTime.UtcNow)
                .ToListAsync();

            return Ok(events);
        }

    }

    

}
