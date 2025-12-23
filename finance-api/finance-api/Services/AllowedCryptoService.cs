using FinanceApi.Data;
using FinanceApi.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Services
{
    public interface IAllowedCryptoService
    {
        Task<IReadOnlyList<AllowedCrypto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<AllowedCrypto?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
        Task<AllowedCrypto?> FindByAnyKeyAsync(string input, CancellationToken cancellationToken = default);
        Task<AllowedCrypto> CreateAsync(AllowedCrypto crypto, CancellationToken cancellationToken = default);
        Task<AllowedCrypto?> UpdateAsync(string id, Action<AllowedCrypto> apply, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    }

    public class AllowedCryptoService : IAllowedCryptoService
    {
        private readonly FinanceDbContext _context;

        public AllowedCryptoService(FinanceDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<AllowedCrypto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.AllowedCryptos
                .OrderBy(c => c.Symbol)
                .ToListAsync(cancellationToken);
        }

        public async Task<AllowedCrypto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return await _context.AllowedCryptos
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task<AllowedCrypto?> FindByAnyKeyAsync(string input, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var normalized = input.Trim();
            var upper = normalized.ToUpperInvariant();
            var lower = normalized.ToLowerInvariant();

            return await _context.AllowedCryptos
                .Where(c => c.IsEnabled)
                .FirstOrDefaultAsync(c =>
                    c.Symbol.ToUpper() == upper ||
                    c.CoinGeckoId.ToLower() == lower ||
                    c.YahooSymbol.ToUpper() == upper,
                    cancellationToken);
        }

        public async Task<AllowedCrypto> CreateAsync(AllowedCrypto crypto, CancellationToken cancellationToken = default)
        {
            crypto.Symbol = crypto.Symbol.Trim().ToUpperInvariant();
            crypto.CoinGeckoId = crypto.CoinGeckoId.Trim().ToLowerInvariant();
            crypto.YahooSymbol = crypto.YahooSymbol.Trim().ToUpperInvariant();
            crypto.CreatedAt = DateTime.UtcNow;
            crypto.UpdatedAt = DateTime.UtcNow;

            _context.AllowedCryptos.Add(crypto);
            await _context.SaveChangesAsync(cancellationToken);
            return crypto;
        }

        public async Task<AllowedCrypto?> UpdateAsync(string id, Action<AllowedCrypto> apply, CancellationToken cancellationToken = default)
        {
            var entity = await _context.AllowedCryptos.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (entity == null)
            {
                return null;
            }

            apply(entity);

            if (!string.IsNullOrWhiteSpace(entity.Symbol))
            {
                entity.Symbol = entity.Symbol.Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(entity.CoinGeckoId))
            {
                entity.CoinGeckoId = entity.CoinGeckoId.Trim().ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(entity.YahooSymbol))
            {
                entity.YahooSymbol = entity.YahooSymbol.Trim().ToUpperInvariant();
            }

            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return entity;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var entity = await _context.AllowedCryptos.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (entity == null)
            {
                return false;
            }

            _context.AllowedCryptos.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
