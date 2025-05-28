using System.Collections.Generic;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Linq;

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

        // Implement other methods from IProgramService as needed
        // For example:
        // public async Task<ProgramStep> GetProgramStepByIdAsync(int id)
        // {
        //     return await _context.ProgramSteps.FindAsync(id);
        // }

        // public async Task CreateProgramStepAsync(ProgramStep programStep)
        // {
        //     _context.ProgramSteps.Add(programStep);
        //     await _context.SaveChangesAsync();
        // }

        // public async Task UpdateProgramStepAsync(ProgramStep programStep)
        // {
        //     _context.Entry(programStep).State = EntityState.Modified;
        //     await _context.SaveChangesAsync();
        // }

        // public async Task DeleteProgramStepAsync(int id)
        // {
        //     var programStep = await _context.ProgramSteps.FindAsync(id);
        //     if (programStep != null)
        //     {
        //         _context.ProgramSteps.Remove(programStep);
        //         await _context.SaveChangesAsync();
        //     }
        // }
    }
}
