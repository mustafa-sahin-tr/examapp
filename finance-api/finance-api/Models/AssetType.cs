namespace FinanceApi.Models
{
    public enum AssetType
    {
        Stock = 0,      // Hisse Senedi (BIST)
        USStock = 1,    // ABD Hisse Senedi
        Gold = 2,       // Altın
        Silver = 3,     // Gümüş
        Fund = 4,       // Fon
        FixedDeposit = 5, // Vadeli Mevduat
        Crypto = 6      // Kripto (BTC, ETH, ...)
    }

    public enum TransactionType
    {
        BUY,
        SELL,
        DEPOSIT_ADD,    // Vadeli Mevduata Para Ekleme
        DEPOSIT_WITHDRAW, // Vadeli Mevduattan Para Çıkarma
        DEPOSIT_INCOME  // Vadeli Mevduat Faiz Geliri
    }
}
