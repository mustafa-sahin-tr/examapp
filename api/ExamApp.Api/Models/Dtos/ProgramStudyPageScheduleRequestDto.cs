using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos
{
    public class ProgramStudyPageScheduleRequestDto
    {
        public List<ProgramStudyPageScheduleItemDto> Items { get; set; } = new List<ProgramStudyPageScheduleItemDto>();
    }

    public class ProgramStudyPageScheduleItemDto
    {
        public int StudyPageId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
