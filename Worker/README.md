# IVA Scanner Worker

این Worker client برای سیستم توزیع شده IVA Scanner است که وظیفه پردازش taskهای اسکن کارت را بر عهده دارد.

## ویژگی‌ها

- **اتصال خودکار به Master**: ثبت نام و heartbeat خودکار
- **پردازش موازی**: پشتیبانی از چندین task همزمان
- **مدیریت Proxy**: استفاده خودکار از proxyها برای درخواست‌ها
- **مقاومت در برابر خطا**: retry logic و error handling
- **نظارت سلامت**: monitoring و گزارش وضعیت

## نصب و راه‌اندازی

### پیش‌نیازها

- .NET 8.0 Runtime
- اتصال شبکه به Master server
- (اختیاری) Proxy servers

### تنظیمات

فایل `appsettings.json` را ویرایش کنید:

```json
{
  "Master": {
    "BaseUrl": "http://your-master-server:5000",
    "ApiKey": "your-api-key"
  },
  "Worker": {
    "Name": "Worker-{MachineName}",
    "MaxConcurrentTasks": 2,
    "HeartbeatInterval": "00:00:30"
  }
}
```

### اجرا

#### Windows

```bash
# Development
dotnet run

# Production (as Windows Service)
sc create IvaWorker binPath="C:\path\to\IvaScanner.Worker.exe"
sc start IvaWorker
```

#### Linux

```bash
# Development
dotnet run

# Production (as systemd service)
sudo systemctl enable iva-worker
sudo systemctl start iva-worker
```

### متغیرهای محیط

- `IVASCANNER_MASTER_URL`: آدرس Master server
- `IVASCANNER_WORKER_ID`: شناسه یکتا Worker
- `IVASCANNER_MAX_TASKS`: حداکثر تعداد task همزمان

## ساختار

```
Worker/
├── Program.cs              # Entry point
├── Configuration/          # Configuration classes
├── Services/              # Core services
│   ├── WorkerService.cs   # Main worker service
│   ├── HeartbeatService.cs
│   ├── TaskProcessingService.cs
│   ├── IvaWorkerClient.cs # IVA API integration
│   └── ...
└── appsettings.json       # Configuration
```

## عملکرد

1. **راه‌اندازی**: ثبت نام با Master server
2. **دریافت Task**: دریافت task از صف Redis
3. **پردازش**: اجرای scan با IVA API
4. **گزارش نتیجه**: ارسال نتیجه به Master
5. **Heartbeat**: گزارش مداوم وضعیت

## نظارت

Worker اطلاعات زیر را گزارش می‌دهد:

- وضعیت فعلی (Online/Busy/Error)
- تعداد taskهای فعال
- آمار تکمیل شده/ناموفق
- اطلاعات سیستم (CPU, Memory)

## عیب‌یابی

### لاگ‌ها

```bash
# مشاهده لاگ‌های real-time
tail -f logs/worker.log

# بررسی خطاهای اتصال
grep "connection" logs/worker.log
```

### مشکلات رایج

1. **اتصال به Master**: بررسی URL و network
2. **Authentication**: بررسی API key
3. **Proxy**: بررسی تنظیمات proxy
4. **IVA Account**: بررسی اعتبار session data

## API Endpoints (Master)

Worker با این endpoint های Master ارتباط برقرار می‌کند:

- `POST /api/workers/register` - ثبت نام worker
- `POST /api/workers/heartbeat` - ارسال heartbeat
- `GET /api/workers/{id}/next-task` - دریافت task بعدی
- `POST /api/tasks/complete` - گزارش تکمیل
- `POST /api/tasks/failure` - گزارش شکست