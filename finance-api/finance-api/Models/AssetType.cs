namespace FinanceApi.Models
{
    public enum AssetType
    {
        Stock = 0,      // Hisse Senedi (BIST100)
        USStock = 1,    // ABD Hisse Senedi
        Gold = 2,       // Altın
        Silver = 3,     // Gümüş
        Fund = 4        // Fon
    }

    public enum TransactionType
    {
        BUY,
        SELL
    }
}
