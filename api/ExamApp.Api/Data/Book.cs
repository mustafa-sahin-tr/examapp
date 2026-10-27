using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data
{
    public class Book : BaseEntity
    {
        public const int MaxNotesLength = 500;

        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;  // Kitap adı

        /// <summary>
        /// Issue #577: aynı içeriğin asıl kaydı (başka baskı/ad, örn. Güneş Koleji Türkçe → Süper Combo Türkçe). Tek seviye:
        /// asıl kaydın kendisi başka bir kitaba bağlanamaz.
        /// </summary>
        public int? CanonicalBookId { get; set; }

        [ForeignKey(nameof(CanonicalBookId))]
        public Book? CanonicalBook { get; set; }

        [MaxLength(MaxNotesLength)]
        public string? Notes { get; set; }

        public ICollection<BookTest> BookTests { get; set; } = new List<BookTest>();
    }

    public class BookTest : BaseEntity
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; // Test adı (Örn: 1. Deneme, Bölüm 2 Testi)
        
        public int BookId { get; set; }
        [ForeignKey("BookId")]
        public Book Book { get; set; }        
    }
}
