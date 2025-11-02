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

        public async Task<TransactionDto> CreateTransactionAsync(CreateTransactionDto createTransactionDto)
        {
            var transaction = new Transaction
            {
                AssetId = createTransactionDto.AssetId,
                Type = Enum.Parse<TransactionType>(createTransactionDto.Type),
                Quantity = createTransactionDto.Quantity,
                Price = createTransactionDto.Price,
                Date = createTransactionDto.Date,
                Fees = createTransactionDto.Fees,
                Notes = createTransactionDto.Notes,
                UserId = "default-user" // TODO: Get from authentication context
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            // Portfolio'yu otomatik olarak güncelle
            await UpdatePortfolioAsync(transaction);

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

            // Eski transaction'ı geri al (portfolio'dan)
            await RevertPortfolioAsync(transaction);

            // Transaction'ı güncelle
            if (!string.IsNullOrEmpty(transactionDto.AssetId))
                transaction.AssetId = transactionDto.AssetId;
            if (!string.IsNullOrEmpty(transactionDto.Type))
                transaction.Type = Enum.Parse<TransactionType>(transactionDto.Type);
            transaction.Quantity = transactionDto.Quantity;
            transaction.Price = transactionDto.Price;
            if (transactionDto.Date != null && transactionDto.Date != default(DateTime)
                && transactionDto.Date != transaction.Date && transactionDto.Date != DateTime.MinValue)
            {
                transaction.Date = transactionDto.Date;
            }
            if (transactionDto.Fees != null && transactionDto.Fees >= 0)
                transaction.Fees = transactionDto.Fees;

            if (!string.IsNullOrEmpty(transactionDto.Notes))
                transaction.Notes = transactionDto.Notes;

            await _context.SaveChangesAsync();

            // Yeni transaction'ı portfolio'ya uygula
            await UpdatePortfolioAsync(transaction);

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

            // Transaction'ı portfolio'dan geri al
            await RevertPortfolioAsync(transaction);

            _context.Transactions.Remove(transaction);
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task UpdatePortfolioAsync(Transaction transaction)
        {
            // Mevcut portfolio kaydını bul
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.AssetId == transaction.AssetId && p.UserId == transaction.UserId);

            if (portfolio == null)
            {
                // Yeni portfolio kaydı oluştur
                portfolio = new Portfolio
                {
                    AssetId = transaction.AssetId,
                    UserId = transaction.UserId,
                    TotalQuantity = 0,
                    AveragePrice = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _context.Portfolios.Add(portfolio);
            }

            // Transaction tipine göre portfolio'yu güncelle
            if (transaction.Type == TransactionType.BUY || transaction.Type == TransactionType.DEPOSIT_ADD)
            {
                // BUY veya DEPOSIT_ADD: Weighted average price hesapla
                var newTotalCost = (portfolio.TotalQuantity * portfolio.AveragePrice) + (transaction.Quantity * transaction.Price);
                var newTotalQuantity = portfolio.TotalQuantity + transaction.Quantity;

                portfolio.TotalQuantity = newTotalQuantity;
                portfolio.AveragePrice = newTotalQuantity > 0 ? newTotalCost / newTotalQuantity : 0;
            }
            else if (transaction.Type == TransactionType.SELL || transaction.Type == TransactionType.DEPOSIT_WITHDRAW)
            {
                // SELL veya DEPOSIT_WITHDRAW: Sadece quantity'yi azalt, average price aynı kalır
                portfolio.TotalQuantity -= transaction.Quantity;

                // Eğer tüm pozisyon satıldıysa portfolio kaydını sil
                if (portfolio.TotalQuantity <= 0)
                {
                    _context.Portfolios.Remove(portfolio);
                    await _context.SaveChangesAsync();
                    return;
                }
            }
            else if (transaction.Type == TransactionType.DEPOSIT_INCOME)
            {
                // DEPOSIT_INCOME: Gelir olarak ekleme, average price değişmez
                portfolio.TotalQuantity += transaction.Quantity;
            }

            portfolio.LastUpdated = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private async Task RevertPortfolioAsync(Transaction transaction)
        {
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.AssetId == transaction.AssetId && p.UserId == transaction.UserId);

            if (portfolio == null)
                return;

            // Transaction tipine göre geri alma işlemi yap
            if (transaction.Type == TransactionType.BUY || transaction.Type == TransactionType.DEPOSIT_ADD)
            {
                // BUY veya DEPOSIT_ADD işlemini geri al: quantity'yi azalt
                portfolio.TotalQuantity -= transaction.Quantity;

                // Eğer quantity sıfır veya negatif olursa portfolio'yu sil
                if (portfolio.TotalQuantity <= 0)
                {
                    _context.Portfolios.Remove(portfolio);
                    await _context.SaveChangesAsync();
                    return;
                }

                // Average price yeniden hesapla (tüm işlemleri yeniden hesapla)
                await RecalculatePortfolioAsync(portfolio);
            }
            else if (transaction.Type == TransactionType.SELL || transaction.Type == TransactionType.DEPOSIT_WITHDRAW)
            {
                // SELL veya DEPOSIT_WITHDRAW işlemini geri al: quantity'yi artır
                portfolio.TotalQuantity += transaction.Quantity;

                // Average price yeniden hesapla
                await RecalculatePortfolioAsync(portfolio);
            }
            else if (transaction.Type == TransactionType.DEPOSIT_INCOME)
            {
                // DEPOSIT_INCOME işlemini geri al: quantity'yi azalt
                portfolio.TotalQuantity -= transaction.Quantity;

                if (portfolio.TotalQuantity <= 0)
                {
                    _context.Portfolios.Remove(portfolio);
                    await _context.SaveChangesAsync();
                    return;
                }
            }

            portfolio.LastUpdated = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private async Task RecalculatePortfolioAsync(Portfolio portfolio)
        {
            // Bu asset için tüm BUY ve DEPOSIT_ADD transaction'larını al ve average price'ı yeniden hesapla
            var buyTransactions = await _context.Transactions
                .Where(t => t.AssetId == portfolio.AssetId &&
                           t.UserId == portfolio.UserId &&
                           (t.Type == TransactionType.BUY || t.Type == TransactionType.DEPOSIT_ADD))
                .ToListAsync();

            if (buyTransactions.Any())
            {
                var totalCost = buyTransactions.Sum(t => t.Quantity * t.Price);
                var totalQuantity = buyTransactions.Sum(t => t.Quantity);

                portfolio.AveragePrice = totalQuantity > 0 ? totalCost / totalQuantity : 0;
            }
        }
    }
}
