using IvaScanner.Core.Models;
using IvaScanner.Core;

namespace IvaScanner.Worker.Services;

public interface IIvaWorkerClient
{
    Task ConfigureAsync(IvaAccountDto account, ProxyServerDto? proxy = null, CancellationToken cancellationToken = default);
    Task<IvaResult?> TestCvvAsync(string cvv, CancellationToken cancellationToken = default);
    Task<IvaResult?> ScanCvvAsync(string cvv, CancellationToken cancellationToken = default);
    Task<bool> ValidateAccountAsync(CancellationToken cancellationToken = default);
}

public class IvaWorkerClient : IIvaWorkerClient
{
    private readonly ILogger<IvaWorkerClient> _logger;
    private readonly HttpClient _httpClient;
    private IvaAccountDto? _currentAccount;
    private ProxyServerDto? _currentProxy;

    public IvaWorkerClient(ILogger<IvaWorkerClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task ConfigureAsync(IvaAccountDto account, ProxyServerDto? proxy = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Configuring IVA client for account {Phone}", account.PhoneNumber);
        
        _currentAccount = account;
        _currentProxy = proxy;

        // Configure HTTP client
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

        // Configure proxy if provided
        if (proxy != null)
        {
            _logger.LogDebug("Configuring proxy {ProxyId} for IVA client", proxy.Id);
            // TODO: Implement proxy configuration
        }

        _logger.LogInformation("IVA client configured for account {Phone}", account.PhoneNumber);
    }

    public async Task<IvaResult?> TestCvvAsync(string cvv, CancellationToken cancellationToken = default)
    {
        if (_currentAccount == null)
            throw new InvalidOperationException("IVA client is not configured. Call ConfigureAsync first.");

        try
        {
            _logger.LogDebug("Testing CVV {Cvv} for expiry detection", cvv);

            // Use the original IVA scanner logic for testing
            var result = await PerformIvaRequestAsync(cvv, isTest: true, cancellationToken);
            
            _logger.LogDebug("CVV {Cvv} test completed - Success: {Success}", cvv, result?.IsSuccessful);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing CVV {Cvv}", cvv);
            return new IvaResult
            {
                CardNumber = cvv,
                IsSuccessful = false,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public async Task<IvaResult?> ScanCvvAsync(string cvv, CancellationToken cancellationToken = default)
    {
        if (_currentAccount == null)
            throw new InvalidOperationException("IVA client is not configured. Call ConfigureAsync first.");

        try
        {
            _logger.LogDebug("Scanning CVV {Cvv}", cvv);

            var result = await PerformIvaRequestAsync(cvv, isTest: false, cancellationToken);
            
            _logger.LogDebug("CVV {Cvv} scan completed - Success: {Success}", cvv, result?.IsSuccessful);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning CVV {Cvv}", cvv);
            return new IvaResult
            {
                CardNumber = cvv,
                IsSuccessful = false,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public async Task<bool> ValidateAccountAsync(CancellationToken cancellationToken = default)
    {
        if (_currentAccount == null)
            return false;

        try
        {
            _logger.LogDebug("Validating IVA account {Phone}", _currentAccount.PhoneNumber);

            // Test connection with a simple request
            var testResult = await PerformIvaRequestAsync("1234567890123456", isTest: true, cancellationToken);
            
            bool isValid = testResult != null && !testResult.ErrorMessage?.Contains("authentication") == true;
            
            _logger.LogDebug("Account {Phone} validation result: {IsValid}", 
                _currentAccount.PhoneNumber, isValid);
            
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating account {Phone}", _currentAccount?.PhoneNumber);
            return false;
        }
    }

    private async Task<IvaResult?> PerformIvaRequestAsync(string cvv, bool isTest, CancellationToken cancellationToken)
    {
        if (_currentAccount == null)
            throw new InvalidOperationException("No account configured");

        var startTime = DateTime.UtcNow;

        try
        {
            // Simulate IVA API call using the original scanning logic
            // This would integrate with the existing IvaScannerBot logic
            
            // For now, we'll simulate the request structure
            var requestData = new
            {
                cardNumber = cvv,
                amount = isTest ? "1000" : "5000", // Smaller amount for testing
                sessionId = _currentAccount.SessionData,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _logger.LogTrace("Making IVA request for CVV {Cvv}", cvv);

            // TODO: Implement actual IVA API integration
            // This should use the IvaAuthClient and related components from the Core project
            
            // Simulate processing delay
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            // For now, return a simulated result
            // In real implementation, this would parse the actual IVA response
            var result = new IvaResult
            {
                CardNumber = cvv,
                IsSuccessful = Random.Shared.NextDouble() > 0.8, // 20% success rate for simulation
                Amount = isTest ? 1000 : 5000,
                Timestamp = DateTime.UtcNow,
                ResponseTime = DateTime.UtcNow - startTime,
                ExpiryDate = GenerateRandomExpiryDate()
            };

            if (!result.IsSuccessful)
            {
                result.ErrorMessage = "Invalid card number or insufficient funds";
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IVA request failed for CVV {Cvv}", cvv);
            
            return new IvaResult
            {
                CardNumber = cvv,
                IsSuccessful = false,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.UtcNow,
                ResponseTime = DateTime.UtcNow - startTime
            };
        }
    }

    private static string GenerateRandomExpiryDate()
    {
        var random = Random.Shared;
        var year = random.Next(2024, 2030);
        var month = random.Next(1, 13);
        return $"{month:D2}/{year % 100:D2}";
    }
}