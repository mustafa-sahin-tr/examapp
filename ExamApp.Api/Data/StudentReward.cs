using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;
/// <summary>
/// Öğrencinin aldığı ödülleri saklar.
/// </summary>
public class StudentReward
{
    public int Id { get; set; }
    public int StudentId { get; set; } // Öğrenci FK
    public int RewardId { get; set; } // Alınan ödül FK
    public int PointsSpent { get; set; } // Harcanan puan
    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("StudentId")]
    public Student Student { get; set; }

    [ForeignKey("RewardId")]
    public Reward Reward { get; set; }
}
