using System;
using System.Collections.Generic;

namespace IvaScanner
{
    public class IvaOptions
    {
        public string ApiBaseUrl { get; set; } = IvaConstants.ApiBaseUrl;
        public string ApiPrefix { get; set; } = IvaConstants.ApiPrefix;
        public string PublicKeyUrl { get; set; } = IvaConstants.PublicKeyUrl;
        public string? KeyId { get; set; }
        public string? TransactionId { get; set; }
        public string AppVersion { get; set; } = IvaConstants.AppVersion;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(65000);
        public int MaxChargeRetries { get; set; } = 10;
        public TimeSpan ChargeRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public string? ChargeProxy { get; set; }

        public List<string> RetryableStatusMessages { get; set; } = new()
        {
            "محدودیت روزانه تراکنش",
            "عملیات ناموفق بود",
            "سرویس در حال حاضر قادر به پاسخگویی نیست",
        };

        public List<string> DailyLimitMessages { get; set; } = new()
        {
            "محدودیت روزانه تراکنش",
        };

        public string BaseAddress => ApiBaseUrl.TrimEnd('/') + ApiPrefix;
    }

    public class SessionData
    {
        public string? Phone { get; set; }
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public string? TokenType { get; set; }
        public long? ExpiresIn { get; set; }
        public long? AccessTokenObtainedAt { get; set; }
        public string? SharedKey { get; set; }
        public string? WorkingKey { get; set; }
        public string? RsaPublic { get; set; }
    }

    public class CardInfo
    {
        public string Pan { get; set; } = string.Empty;
        public string ExpireMonth { get; set; } = string.Empty;
        public string ExpireYear { get; set; } = string.Empty;
        public string Cvv2 { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string FactorNumber { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string PhoneUsed { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int TestsPerformed { get; set; }
    }

    public class TokenResult
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? TokenType { get; set; }
        public long? ExpiresIn { get; set; }
        public string? Key { get; set; }
    }

    public class OtpRequestResult
    {
        public string? Token { get; set; }
        public string? ReagentNumber { get; set; }
    }

    public class ApiResponse<T>
    {
        public T? Data { get; set; }
        public ApiError? Error { get; set; }
    }

    public class ApiError
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }

    public class ChargePurchaseRequest
    {
        public long Amount { get; set; }
        public string? TargetMobileNo { get; set; }
        public string? ProviderId { get; set; }
        public CardPayment Card { get; set; } = new();
        public long? OrderId { get; set; }
        public Dictionary<string, object?>? Extra { get; set; }
    }

    public class CardPayment
    {
        public string? Pan { get; set; }
        public string? Cvv2 { get; set; }
        public string? ExpireMonth { get; set; }
        public string? ExpireYear { get; set; }
        public string? Pin { get; set; }
        public string? Token { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Token))
            {
                if (string.IsNullOrEmpty(Pan) || Pan!.Length < 16)
                    throw new ArgumentException("Invalid PAN");
                if (string.IsNullOrEmpty(Cvv2) || Cvv2!.Length < 3)
                    throw new ArgumentException("Invalid CVV2");
                if (string.IsNullOrEmpty(Pin) || Pin!.Length < 4)
                    throw new ArgumentException("Invalid PIN");
            }
        }
    }

    public class ChargePurchaseResult
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? Message { get; set; }
        public string? UsedPhone { get; set; }
        public int RetryCount { get; set; }
        public string? FactorNumber { get; set; }
        public string? TransactionId { get; set; }
        public long? Amount { get; set; }
        public string? OperatorName { get; set; }
    }

    public class ChargeOperator
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<ChargeAmount>? Amounts { get; set; }
    }

    public class ChargeAmount
    {
        public long? Amount { get; set; }
        public string? Label { get; set; }
    }
}