using FinanceApi.Models.Dtos;

namespace FinanceApi.Services
{
    public interface ITransactionService
    {
        Task<IEnumerable<TransactionDto>> GetAllTransactionsAsync();
        Task<TransactionDto?> GetTransactionByIdAsync(string id);
        Task<IEnumerable<TransactionDto>> GetTransactionsByAssetIdAsync(string assetId);
        Task<TransactionDto> CreateTransactionAsync(CreateTransactionDto createTransactionDto);
        Task<TransactionDto?> UpdateTransactionAsync(string id, TransactionDto transactionDto);
        Task<bool> DeleteTransactionAsync(string id);
    }
}
