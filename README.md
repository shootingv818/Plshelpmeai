# IvaScanner

یک کتابخانه C# برای اسکن و تست کارت‌های بانکی در سیستم پرداخت آیوا (IVA).

## ویژگی‌ها

- اتصال امن به API آیوا با رمزنگاری RSA و AES
- تست کامل اطلاعات کارت (CVV2، تاریخ انقضا، PIN)
- پشتیبانی از چندین شماره تلفن
- گزارش‌گیری پیشرفته از روند اسکن
- مدیریت خطاها و retry logic

## نصب

```bash
git clone https://github.com/shootingv818/Plshelpmeai.git
cd Plshelpmeai
dotnet restore
dotnet build
```

## استفاده

### استفاده ساده

```csharp
var scanner = new IvaScannerBot();
var result = await scanner.ScanCardAsync("1234567890123456", "09123456789");

if (result.Success)
{
    Console.WriteLine($"کارت معتبر - CVV2: {result.Cvv2}, PIN: {result.Pin}");
}
```

### اسکن چندگانه

```csharp
var phones = new[] { "09123456789", "09987654321" };
await scanner.BatchScanAsync("1234567890123456", phones);
```

## ساختار پروژه

- `IvaAuthClient.cs` - کلاینت اصلی API
- `IvaCrypto.cs` - عملیات رمزنگاری
- `IvaScannerBot.cs` - منطق اسکن کارت
- `Models.cs` - مدل‌های داده
- `IvaConstants.cs` - ثوابت و تنظیمات

## تنظیمات

```csharp
var options = new IvaOptions
{
    AppVersion = "3.10.24",
    MaxChargeRetries = 5,
    ChargeRetryDelay = TimeSpan.FromMilliseconds(500),
    Timeout = TimeSpan.FromSeconds(65)
};

var scanner = new IvaScannerBot(options);
```

## هشدارها

⚠️ **این کتابخانه صرفاً برای اهداف آموزشی و تست است. استفاده نادرست آن ممکن است غیرقانونی باشد.**

⚠️ **همواره از آن با احتیاط و در چارچوب قوانین استفاده کنید.**

## مجوز

این پروژه تحت مجوز MIT منتشر شده است.