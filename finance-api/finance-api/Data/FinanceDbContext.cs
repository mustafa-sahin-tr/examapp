using Microsoft.EntityFrameworkCore;
using FinanceApi.Models;

namespace FinanceApi.Data
{
    public class FinanceDbContext : DbContext
    {
        public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
        {
        }

        public DbSet<Asset> Assets { get; set; }
        public DbSet<AllowedCrypto> AllowedCryptos { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Portfolio> Portfolios { get; set; }
        public DbSet<ProfitLossHistory> ProfitLossHistories { get; set; }
        public DbSet<AssetTypeProfitLoss> AssetTypeProfitLosses { get; set; }
        public DbSet<AssetProfitLoss> AssetProfitLosses { get; set; }
        public DbSet<ExchangeRate> ExchangeRates { get; set; }
        public DbSet<UserCurrencyPreference> UserCurrencyPreferences { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Asset configuration
            modelBuilder.Entity<Asset>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Symbol).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Currency).IsRequired().HasMaxLength(10);
                entity.Property(e => e.CurrentPrice).HasColumnType("decimal(18,4)");
                entity.Property(e => e.ChangePercentage).HasColumnType("decimal(18,4)");
                entity.Property(e => e.ChangeValue).HasColumnType("decimal(18,4)");
                entity.HasIndex(e => new { e.Symbol, e.Type }).IsUnique();
            });

            modelBuilder.Entity<AllowedCrypto>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Symbol).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.CoinGeckoId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.YahooSymbol).IsRequired().HasMaxLength(30);
                entity.Property(e => e.IsEnabled).HasDefaultValue(true);
                entity.HasIndex(e => e.Symbol).IsUnique();
            });

            // Transaction configuration
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Quantity).HasColumnType("decimal(18,8)");
                entity.Property(e => e.Price).HasColumnType("decimal(18,4)");
                entity.Property(e => e.Fees).HasColumnType("decimal(18,4)");
                entity.Property(e => e.Notes).HasMaxLength(500);

                entity.HasOne(e => e.Asset)
                      .WithMany(a => a.Transactions)
                      .HasForeignKey(e => e.AssetId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.UserId, e.AssetId, e.Date });
            });

            // Portfolio configuration
            modelBuilder.Entity<Portfolio>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TotalQuantity).HasColumnType("decimal(18,8)");
                entity.Property(e => e.AveragePrice).HasColumnType("decimal(18,4)");

                entity.HasOne(e => e.Asset)
                      .WithMany(a => a.Portfolios)
                      .HasForeignKey(e => e.AssetId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.UserId, e.AssetId }).IsUnique();
            });

            // ProfitLossHistory configuration
            modelBuilder.Entity<ProfitLossHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TotalProfitLoss).HasColumnType("decimal(18,4)");
                entity.Property(e => e.TotalInvestment).HasColumnType("decimal(18,4)");
                entity.Property(e => e.TotalCurrentValue).HasColumnType("decimal(18,4)");
                entity.Property(e => e.ProfitLossPercentage).HasColumnType("decimal(18,4)");
                entity.Property(e => e.AssetTypeBreakdown).HasColumnType("text");
                entity.HasIndex(e => new { e.UserId, e.Date, e.Hour });
            });

            // AssetTypeProfitLoss configuration
            modelBuilder.Entity<AssetTypeProfitLoss>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProfitLoss).HasColumnType("decimal(18,4)");
                entity.Property(e => e.Investment).HasColumnType("decimal(18,4)");
                entity.Property(e => e.CurrentValue).HasColumnType("decimal(18,4)");
                entity.Property(e => e.ProfitLossPercentage).HasColumnType("decimal(18,4)");

                entity.HasOne(e => e.ProfitLossHistory)
                      .WithMany()
                      .HasForeignKey(e => e.ProfitLossHistoryId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ProfitLossHistoryId, e.AssetType });
            });

            // AssetProfitLoss configuration
            modelBuilder.Entity<AssetProfitLoss>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProfitLoss).HasColumnType("decimal(18,4)");
                entity.Property(e => e.Investment).HasColumnType("decimal(18,4)");
                entity.Property(e => e.CurrentValue).HasColumnType("decimal(18,4)");
                entity.Property(e => e.ProfitLossPercentage).HasColumnType("decimal(18,4)");
                entity.Property(e => e.Quantity).HasColumnType("decimal(18,8)");
                entity.Property(e => e.AveragePrice).HasColumnType("decimal(18,4)");
                entity.Property(e => e.CurrentPrice).HasColumnType("decimal(18,4)");

                entity.HasOne(e => e.ProfitLossHistory)
                      .WithMany()
                      .HasForeignKey(e => e.ProfitLossHistoryId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Asset)
                      .WithMany()
                      .HasForeignKey(e => e.AssetId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ProfitLossHistoryId, e.AssetId });
            });

            // ExchangeRate configuration
            modelBuilder.Entity<ExchangeRate>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FromCurrency).IsRequired().HasMaxLength(3);
                entity.Property(e => e.ToCurrency).IsRequired().HasMaxLength(3);
                entity.Property(e => e.Rate).HasColumnType("decimal(18,8)");
                entity.Property(e => e.ChangePercentage).HasColumnType("decimal(18,4)");
                entity.Property(e => e.ChangeValue).HasColumnType("decimal(18,8)");
                entity.HasIndex(e => new { e.FromCurrency, e.ToCurrency }).IsUnique();
            });

            // UserCurrencyPreference configuration
            modelBuilder.Entity<UserCurrencyPreference>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.PreferredCurrency).IsRequired().HasMaxLength(3);
                entity.HasIndex(e => e.UserId).IsUnique();
            });

            // Seed data
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Seed allowed cryptos (minimal defaults)
            var allowedCryptoSeedTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var allowedCryptos = new List<AllowedCrypto>
            {
                new AllowedCrypto
                {
                    Id = "crypto-btc",
                    Symbol = "BTC",
                    Name = "Bitcoin",
                    CoinGeckoId = "bitcoin",
                    YahooSymbol = "BTC-USD",
                    IsEnabled = true,
                    CreatedAt = allowedCryptoSeedTime,
                    UpdatedAt = allowedCryptoSeedTime
                },
                new AllowedCrypto
                {
                    Id = "crypto-eth",
                    Symbol = "ETH",
                    Name = "Ethereum",
                    CoinGeckoId = "ethereum",
                    YahooSymbol = "ETH-USD",
                    IsEnabled = true,
                    CreatedAt = allowedCryptoSeedTime,
                    UpdatedAt = allowedCryptoSeedTime
                }
            };

            modelBuilder.Entity<AllowedCrypto>().HasData(allowedCryptos);

            // Seed Assets based on frontend mock data
            var assets = new List<Asset>
            {
                // BIST100 Stocks
                new Asset { Id = "1", Symbol = "TUPRS", Name = "Tüpraş", Type = AssetType.Stock, CurrentPrice = 85.50m, Currency = "TRY", ChangePercentage = 2.5m, ChangeValue = 2.08m },
                new Asset { Id = "2", Symbol = "AKBNK", Name = "Akbank", Type = AssetType.Stock, CurrentPrice = 42.30m, Currency = "TRY", ChangePercentage = -1.2m, ChangeValue = -0.51m },
                new Asset { Id = "3", Symbol = "THYAO", Name = "Türk Hava Yolları", Type = AssetType.Stock, CurrentPrice = 156.80m, Currency = "TRY", ChangePercentage = 3.1m, ChangeValue = 4.71m },
                new Asset { Id = "9", Symbol = "ASELS", Name = "Aselsan", Type = AssetType.Stock, CurrentPrice = 125.40m, Currency = "TRY", ChangePercentage = 1.8m, ChangeValue = 2.22m },
                new Asset { Id = "10", Symbol = "EREGL", Name = "Ereğli Demir Çelik", Type = AssetType.Stock, CurrentPrice = 31.85m, Currency = "TRY", ChangePercentage = -0.8m, ChangeValue = -0.26m },
                
                // US Stocks
                new Asset { Id = "4", Symbol = "AAPL", Name = "Apple Inc.", Type = AssetType.USStock, CurrentPrice = 192.45m, Currency = "USD", ChangePercentage = 1.8m, ChangeValue = 3.42m },
                new Asset { Id = "5", Symbol = "MSFT", Name = "Microsoft Corporation", Type = AssetType.USStock, CurrentPrice = 378.20m, Currency = "USD", ChangePercentage = -0.5m, ChangeValue = -1.89m },
                new Asset { Id = "15", Symbol = "GOOGL", Name = "Alphabet Inc.", Type = AssetType.USStock, CurrentPrice = 142.65m, Currency = "USD", ChangePercentage = 2.3m, ChangeValue = 3.21m },
                new Asset { Id = "16", Symbol = "AMZN", Name = "Amazon.com Inc.", Type = AssetType.USStock, CurrentPrice = 151.94m, Currency = "USD", ChangePercentage = 1.1m, ChangeValue = 1.65m },
                new Asset { Id = "17", Symbol = "TSLA", Name = "Tesla Inc.", Type = AssetType.USStock, CurrentPrice = 248.50m, Currency = "USD", ChangePercentage = -2.1m, ChangeValue = -5.32m },
                
                // Precious Metals
                new Asset { Id = "6", Symbol = "GOLD", Name = "Gold", Type = AssetType.Gold, CurrentPrice = 2012.45m, Currency = "USD", ChangePercentage = 0.8m, ChangeValue = 15.92m },
                new Asset { Id = "7", Symbol = "SILVER", Name = "Silver", Type = AssetType.Silver, CurrentPrice = 24.85m, Currency = "USD", ChangePercentage = -1.5m, ChangeValue = -0.38m },
                
                // Funds
                new Asset { Id = "8", Symbol = "QNB001", Name = "QNB Finans Portföy A.Ş. Hisse Senedi Fonu", Type = AssetType.Fund, CurrentPrice = 0.125864m, Currency = "TRY", ChangePercentage = 1.2m, ChangeValue = 0.001493m },
                new Asset { Id = "18", Symbol = "GAR001", Name = "Garanti Portföy Özel Sektör Borçlanma Araçları Fonu", Type = AssetType.Fund, CurrentPrice = 0.086543m, Currency = "TRY", ChangePercentage = 0.3m, ChangeValue = 0.000259m }
            };

            modelBuilder.Entity<Asset>().HasData(assets);
        }
    }
}
