using IvaScanner.Core.Models;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public class TaskProcessor : ITaskProcessor
    {
        private readonly ILogger<TaskProcessor> _logger;
        private readonly IConfiguration _config;

        public TaskProcessor(ILogger<TaskProcessor> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task<TaskResult> ProcessTaskAsync(TaskAssignment task)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                _logger.LogInformation("Processing task {TaskId} for job {JobId}, range {RangeStart}-{RangeEnd}", 
                    task.TaskId, task.JobId, task.RangeStart, task.RangeEnd);

                // Check if this is expiry detection (RangeStart = 0, RangeEnd = 0)
                if (task.RangeStart == 0 && task.RangeEnd == 0)
                {
                    var expiryResult = await DetectExpiryAsync(task);
                    return new TaskResult
                    {
                        Success = expiryResult.Success,
                        Result = JsonSerializer.Serialize(expiryResult),
                        ErrorMessage = expiryResult.ErrorMessage,
                        ProcessingTime = DateTime.UtcNow - startTime
                    };
                }
                else
                {
                    var cvvResult = await ScanCvvRangeAsync(task);
                    return new TaskResult
                    {
                        Success = cvvResult.Success,
                        Result = JsonSerializer.Serialize(cvvResult),
                        ErrorMessage = cvvResult.ErrorMessage,
                        ProcessingTime = DateTime.UtcNow - startTime
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing task {TaskId}", task.TaskId);
                return new TaskResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTime = DateTime.UtcNow - startTime
                };
            }
        }

        public async Task<ExpiryDetectionResult> DetectExpiryAsync(TaskAssignment task)
        {
            _logger.LogInformation("Starting expiry detection for card {CardNumber}", task.CardNumber);

            try
            {
                // Simulate expiry detection logic
                // In real implementation, this would use the IVA scanner logic from original codebase
                
                var requestDelay = _config.GetValue<int>("ScanSettings:RequestDelayMs", 1000);
                
                // Persian calendar years 1406-1410, months 01-12
                var years = new[] { "06", "07", "08", "09", "10" }; // Last two digits of 1406-1410
                var months = new[] { "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12" };

                foreach (var year in years)
                {
                    foreach (var month in months)
                    {
                        var expiry = $"{year}{month}";
                        
                        // Simulate IVA API call to check expiry
                        // This would be replaced with actual IvaAuthClient call
                        _logger.LogDebug("Testing expiry {Expiry} for card {CardNumber}", expiry, task.CardNumber);
                        
                        // Add delay between requests
                        await Task.Delay(requestDelay);
                        
                        // Simulate random success (for testing - would be real API response)
                        if (new Random().Next(1, 100) <= 5) // 5% chance of finding valid expiry
                        {
                            _logger.LogInformation("Found valid expiry {Expiry} for card {CardNumber}", expiry, task.CardNumber);
                            
                            return new ExpiryDetectionResult
                            {
                                Success = true,
                                Expiry = expiry
                            };
                        }
                    }
                }

                // No valid expiry found
                _logger.LogInformation("No valid expiry found for card {CardNumber}", task.CardNumber);
                
                return new ExpiryDetectionResult
                {
                    Success = false,
                    ErrorMessage = "No valid expiry date found"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during expiry detection for card {CardNumber}", task.CardNumber);
                
                return new ExpiryDetectionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<CvvScanResult> ScanCvvRangeAsync(TaskAssignment task)
        {
            _logger.LogInformation("Scanning CVV range {RangeStart}-{RangeEnd} for card {CardNumber}", 
                task.RangeStart, task.RangeEnd, task.CardNumber);

            try
            {
                var requestDelay = _config.GetValue<int>("ScanSettings:RequestDelayMs", 1000);
                
                for (int cvv = task.RangeStart; cvv <= task.RangeEnd; cvv++)
                {
                    // Format CVV with leading zeros
                    var cvvStr = cvv.ToString("D3");
                    
                    // Simulate IVA API call to check CVV
                    // This would be replaced with actual IvaAuthClient call
                    _logger.LogDebug("Testing CVV {Cvv} for card {CardNumber}", cvvStr, task.CardNumber);
                    
                    // Add delay between requests
                    await Task.Delay(requestDelay);
                    
                    // Simulate random success (for testing - would be real API response)
                    if (new Random().Next(1, 10000) <= 1) // 0.01% chance of finding valid CVV
                    {
                        _logger.LogInformation("Found valid CVV {Cvv} for card {CardNumber}", cvvStr, task.CardNumber);
                        
                        // Simulate getting card info after successful CVV
                        var cardInfo = new
                        {
                            CardNumber = task.CardNumber,
                            Cvv = cvvStr,
                            Balance = new Random().Next(10000, 1000000),
                            Status = "Active",
                            CheckedAt = DateTime.UtcNow
                        };
                        
                        return new CvvScanResult
                        {
                            Success = true,
                            ValidCvv = cvvStr,
                            CardInfo = cardInfo
                        };
                    }
                }

                // No valid CVV found in this range
                _logger.LogInformation("No valid CVV found in range {RangeStart}-{RangeEnd} for card {CardNumber}", 
                    task.RangeStart, task.RangeEnd, task.CardNumber);
                
                return new CvvScanResult
                {
                    Success = false,
                    ErrorMessage = $"No valid CVV found in range {task.RangeStart}-{task.RangeEnd}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during CVV scanning for card {CardNumber}, range {RangeStart}-{RangeEnd}", 
                    task.CardNumber, task.RangeStart, task.RangeEnd);
                
                return new CvvScanResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}