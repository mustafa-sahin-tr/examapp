using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data
{
    public abstract class BaseEntity
    {
        public DateTime CreateTime { get; set; }        
        public int? CreateUserId { get; set; }
        public DateTime? UpdateTime { get; set; }
        public int? UpdateUserId { get; set; }
        public DateTime? DeleteTime { get; set; }
        public int? DeleteUserId { get; set; }
        public bool IsDeleted { get; set; } = false;
    }
}
