using Microsoft.AspNetCore.Mvc;
using FinanceApi.Models.Dtos;
using FinanceApi.Services;

namespace FinanceApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TransactionsController : ControllerBase
    {
        private readonly ITransactionService _transactionService;

        public TransactionsController(ITransactionService transactionService)
        {
            _transactionService = transactionService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TransactionDto>>> GetAllTransactions()
        {
            try
            {
                var transactions = await _transactionService.GetAllTransactionsAsync();
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<TransactionDto>> GetTransaction(string id)
        {
            try
            {
                var transaction = await _transactionService.GetTransactionByIdAsync(id);
                if (transaction == null)
                {
                    return NotFound($"Transaction with ID {id} not found");
                }
                return Ok(transaction);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("by-asset/{assetId}")]
        public async Task<ActionResult<IEnumerable<TransactionDto>>> GetTransactionsByAsset(string assetId)
        {
            try
            {
                var transactions = await _transactionService.GetTransactionsByAssetIdAsync(assetId);
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<ActionResult<TransactionDto>> CreateTransaction(CreateTransactionDto createTransactionDto)
        {
            try
            {
                var createdTransaction = await _transactionService.CreateTransactionAsync(createTransactionDto);
                return CreatedAtAction(nameof(GetTransaction), new { id = createdTransaction.Id }, createdTransaction);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<TransactionDto>> UpdateTransaction(string id, TransactionDto transactionDto)
        {
            try
            {
                var updatedTransaction = await _transactionService.UpdateTransactionAsync(id, transactionDto);
                if (updatedTransaction == null)
                {
                    return NotFound($"Transaction with ID {id} not found");
                }
                return Ok(updatedTransaction);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteTransaction(string id)
        {
            try
            {
                var deleted = await _transactionService.DeleteTransactionAsync(id);
                if (!deleted)
                {
                    return NotFound($"Transaction with ID {id} not found");
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
