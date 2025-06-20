using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data
{
    public class Book : BaseEntity
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;  // Kitap adı

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
