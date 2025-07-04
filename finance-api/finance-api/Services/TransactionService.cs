using Microsoft.EntityFrameworkCore;
using FinanceApi.Models;
using FinanceApi.Models.Dtos;
using FinanceApi.Data;

namespace FinanceApi.Services
{
    public class TransactionService : ITransactionService
    {
        private readonly FinanceDbContext _context;

        public TransactionService(FinanceDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<TransactionDto>> GetAllTransactionsAsync()
        {
            var transactions = await _context.Transactions
                .Include(t => t.Asset)
                .ToListAsync();

            return transactions.Select(t => new TransactionDto
            {
                Id = t.Id,
                AssetId = t.AssetId,
                Type = t.Type.ToString(),
                Quantity = t.Quantity,
                Price = t.Price,
                Date = t.Date,
                Fees = t.Fees,
                Notes = t.Notes
            });
        }

        public async Task<TransactionDto?> GetTransactionByIdAsync(string id)
        {
            var transaction = await _context.Transactions
                .Include(t => t.Asset)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (transaction == null)
                return null;

            return new TransactionDto
            {
                Id = transaction.Id,
                AssetId = transaction.AssetId,
                Type = transaction.Type.ToString(),
                Quantity = transaction.Quantity,
                Price = transaction.Price,
                Date = transaction.Date,
                Fees = transaction.Fees,
                Notes = transaction.Notes
            };
        }

        public async Task<IEnumerable<TransactionDto>> GetTransactionsByAssetIdAsync(string assetId)
        {
            var transactions = await _context.Transactions
                .Include(t => t.Asset)
                .Where(t => t.AssetId == assetId)
                .ToListAsync();

            return transactions.Select(t => new TransactionDto
            {
                Id = t.Id,
                AssetId = t.AssetId,
                Type = t.Type.ToString(),
                Quantity = t.Quantity,
                Price = t.Price,
                Date = t.Date,
                Fees = t.Fees,
                Notes = t.Notes
            });
        }

        public async Task<TransactionDto> CreateTransactionAsync(TransactionDto transactionDto)
        {
            var transaction = new Transaction
            {
                AssetId = transactionDto.AssetId,
                Type = Enum.Parse<TransactionType>(transactionDto.Type),
                Quantity = transactionDto.Quantity,
                Price = transactionDto.Price,
                Date = transactionDto.Date,
                Fees = transactionDto.Fees,
                Notes = transactionDto.Notes
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            return new TransactionDto
            {
                Id = transaction.Id,
                AssetId = transaction.AssetId,
                Type = transaction.Type.ToString(),
                Quantity = transaction.Quantity,
                Price = transaction.Price,
                Date = transaction.Date,
                Fees = transaction.Fees,
                Notes = transaction.Notes
            };
        }

        public async Task<TransactionDto?> UpdateTransactionAsync(string id, TransactionDto transactionDto)
        {
            var transaction = await _context.Transactions.FindAsync(id);
            if (transaction == null)
                return null;

            transaction.AssetId = transactionDto.AssetId;
            transaction.Type = Enum.Parse<TransactionType>(transactionDto.Type);
            transaction.Quantity = transactionDto.Quantity;
            transaction.Price = transactionDto.Price;
            transaction.Date = transactionDto.Date;
            transaction.Fees = transactionDto.Fees;
            transaction.Notes = transactionDto.Notes;

            await _context.SaveChangesAsync();

            return new TransactionDto
            {
                Id = transaction.Id,
                AssetId = transaction.AssetId,
                Type = transaction.Type.ToString(),
                Quantity = transaction.Quantity,
                Price = transaction.Price,
                Date = transaction.Date,
                Fees = transaction.Fees,
                Notes = transaction.Notes
            };
        }

        public async Task<bool> DeleteTransactionAsync(string id)
        {
            var transaction = await _context.Transactions.FindAsync(id);
            if (transaction == null)
                return false;

            _context.Transactions.Remove(transaction);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
