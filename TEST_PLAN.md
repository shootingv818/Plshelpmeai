# IVA Scanner System Test Plan

## نمای کلی

این سند شامل برنامه جامع تست و اعتبارسنجی سیستم IVA Scanner Master-Worker است.

## 1. آماده‌سازی محیط تست

### 1.1 نیازمندی‌های سیستم

**Master Server:**
- .NET 8.0 Runtime
- SQL Server (یا SQL Server LocalDB برای تست)
- Redis Server
- IIS یا Kestrel برای hosting

**Worker Clients:**
- .NET 8.0 Runtime
- دسترسی شبکه به Master
- (اختیاری) Proxy servers

### 1.2 تنظیم پایگاه داده

```bash
# ایجاد دیتابیس
cd Master
dotnet ef database update

# بررسی جداول ایجاد شده
sqlcmd -S "(localdb)\mssqllocaldb" -d IvaScanner -Q "SELECT name FROM sys.tables"
```

### 1.3 راه‌اندازی Redis

```bash
# Linux/Mac
redis-server

# Windows
redis-server.exe

# بررسی اتصال
redis-cli ping
```

## 2. تست‌های واحد (Unit Tests)

### 2.1 تست Service Layer

```csharp
// WorkerServiceTest.cs
[Test]
public async Task RegisterWorker_ShouldReturnSuccess()
{
    var worker = new WorkerRegistrationRequest
    {
        WorkerId = "test-worker-1",
        Name = "Test Worker"
    };
    
    var result = await _workerService.RegisterWorkerAsync(worker);
    Assert.IsTrue(result);
}
```

### 2.2 تست Task Distribution

```csharp
// TaskDistributionServiceTest.cs
[Test]
public async Task CreateTasksForJob_ShouldChunkCorrectly()
{
    var jobId = "test-job-1";
    var cvvList = GenerateCvvList(1000); // 1000 CVVs
    
    await _taskService.CreateTasksForJobAsync(jobId, cvvList);
    
    var tasks = await _taskService.GetTasksByJobIdAsync(jobId);
    Assert.AreEqual(10, tasks.Count); // 1000/100 = 10 tasks
}
```

## 3. تست‌های یکپارچگی (Integration Tests)

### 3.1 تست API Endpoints

```bash
# ثبت نام Worker
curl -X POST http://localhost:5000/api/workers/register \
  -H "Content-Type: application/json" \
  -d '{
    "workerId": "test-worker-1",
    "name": "Test Worker",
    "maxConcurrentTasks": 2
  }'

# ارسال Heartbeat
curl -X POST http://localhost:5000/api/workers/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "workerId": "test-worker-1",
    "status": 2,
    "activeTasks": 0,
    "completedTasks": 5,
    "failedTasks": 1
  }'

# دریافت Task
curl -X GET http://localhost:5000/api/workers/test-worker-1/next-task

# تکمیل Task
curl -X POST http://localhost:5000/api/tasks/complete \
  -H "Content-Type: application/json" \
  -d '{
    "taskId": "task-123",
    "workerId": "test-worker-1",
    "results": [],
    "completedAt": "2024-01-01T12:00:00Z",
    "processingTime": "00:02:30",
    "processedItems": 100
  }'
```

### 3.2 تست Redis Integration

```bash
# بررسی Stream ها
redis-cli XINFO GROUPS iva:tasks:stream

# مشاهده Consumer Groups
redis-cli XINFO CONSUMERS iva:tasks:stream iva:workers

# بررسی Pending Messages
redis-cli XPENDING iva:tasks:stream iva:workers
```

## 4. تست‌های عملکرد (Performance Tests)

### 4.1 تست Load Testing

```csharp
// LoadTest.cs
[Test]
public async Task ProcessMultipleTasks_ShouldHandleConcurrency()
{
    var tasks = new List<Task>();
    
    for (int i = 0; i < 50; i++)
    {
        tasks.Add(CreateAndProcessJob($"job-{i}"));
    }
    
    var results = await Task.WhenAll(tasks);
    Assert.IsTrue(results.All(r => r.Success));
}
```

### 4.2 تست Memory Usage

```bash
# نظارت بر مصرف Memory
dotnet-counters monitor --process-id [PID] \
  --counters System.Runtime[working-set,gc-heap-size]
```

### 4.3 تست Throughput

```csharp
// ThroughputTest.cs
[Test] 
public async Task TaskProcessing_ShouldMeetTargetThroughput()
{
    var startTime = DateTime.UtcNow;
    var jobsProcessed = 0;
    
    // Process jobs for 5 minutes
    while (DateTime.UtcNow - startTime < TimeSpan.FromMinutes(5))
    {
        await ProcessSingleJob();
        jobsProcessed++;
    }
    
    var throughput = jobsProcessed / 5.0; // jobs per minute
    Assert.IsTrue(throughput >= 10); // At least 10 jobs per minute
}
```

## 5. تست‌های امنیتی (Security Tests)

### 5.1 تست Authentication

```bash
# تست بدون API Key
curl -X POST http://localhost:5000/api/workers/register \
  -H "Content-Type: application/json" \
  -d '{"workerId":"test"}'
# انتظار: 401 Unauthorized

# تست با API Key نامعتبر
curl -X POST http://localhost:5000/api/workers/register \
  -H "Authorization: Bearer invalid-key" \
  -d '{"workerId":"test"}'
# انتظار: 401 Unauthorized
```

### 5.2 تست Input Validation

```bash
# تست SQL Injection
curl -X GET "http://localhost:5000/api/workers/'; DROP TABLE Workers; --/next-task"
# انتظار: 400 Bad Request یا 404 Not Found

# تست XSS
curl -X POST http://localhost:5000/api/workers/register \
  -H "Content-Type: application/json" \
  -d '{"workerId":"<script>alert(1)</script>"}'
# انتظار: Input sanitized
```

## 6. تست‌های UI (User Interface Tests)

### 6.1 تست Dashboard

```javascript
// cypress/integration/dashboard.spec.js
describe('Dashboard Tests', () => {
  it('should display worker statistics', () => {
    cy.visit('/');
    cy.get('[data-testid="active-workers"]').should('be.visible');
    cy.get('[data-testid="total-jobs"]').should('contain.text', '0');
  });
  
  it('should update stats in real-time', () => {
    // Simulate worker registration via API
    cy.request('POST', '/api/workers/register', {
      workerId: 'test-worker',
      name: 'Test Worker'
    });
    
    // Check if UI updates
    cy.get('[data-testid="active-workers"]').should('contain.text', '1');
  });
});
```

### 6.2 تست SignalR Updates

```javascript
// تست Real-time Updates
describe('Real-time Updates', () => {
  it('should receive worker status updates', (done) => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/dashboardHub')
      .build();
      
    connection.on('WorkerStatusChanged', (data) => {
      expect(data.workerId).to.equal('test-worker');
      expect(data.status).to.equal('Online');
      done();
    });
    
    connection.start();
  });
});
```

## 7. تست‌های شبکه (Network Tests)

### 7.1 تست اتصال Worker به Master

```bash
# تست اتصال TCP
nc -zv master-server 5000

# تست HTTP Response
curl -I http://master-server:5000/health

# تست SignalR Connection  
wscat -c ws://master-server:5000/dashboardHub
```

### 7.2 تست Proxy Integration

```bash
# تست با HTTP Proxy
curl -x http://proxy-server:8080 http://master-server:5000/api/health

# تست Proxy Rotation
for i in {1..5}; do
  curl http://master-server:5000/api/workers/test-worker/proxy
done
```

## 8. تست‌های مقاومت (Resilience Tests)

### 8.1 تست Redis Failover

```bash
# متوقف کردن Redis
systemctl stop redis

# بررسی رفتار Master
curl http://localhost:5000/health
# انتظار: Degraded state

# راه‌اندازی مجدد Redis
systemctl start redis

# بررسی بازیابی
curl http://localhost:5000/health
# انتظار: Healthy state
```

### 8.2 تست Database Connection Loss

```sql
-- قطع اتصالات
KILL [session_id];

-- بررسی reconnection
-- Master باید خودکار reconnect شود
```

### 8.3 تست Worker Disconnection

```bash
# قطع Worker ناگهانی
kill -9 [worker-pid]

# بررسی تشخیص Master
# Master باید در عرض 30-60 ثانیه Worker را offline کند
```

## 9. سناریوهای End-to-End

### 9.1 سناریو کامل اسکن کارت

```bash
# 1. راه‌اندازی Master
cd Master && dotnet run

# 2. ثبت Worker
cd Worker && dotnet run

# 3. ایجاد IVA Account
curl -X POST http://localhost:5000/api/accounts \
  -d '{"phoneNumber": "09123456789", "sessionData": "..."}'

# 4. ایجاد Scan Job
curl -X POST http://localhost:5000/api/scan/jobs \
  -d '{"cardNumber": "1234567890123456", "phoneNumbers": ["09123456789"]}'

# 5. نظارت Progress
curl http://localhost:5000/api/scan/jobs/[job-id]/progress

# 6. مشاهده نتایج
curl http://localhost:5000/api/scan/jobs/[job-id]/results
```

### 9.2 سناریو مدیریت Proxy

```bash
# 1. اضافه کردن Proxy
curl -X POST http://localhost:5000/api/proxy \
  -d '{"host": "proxy1.example.com", "port": 8080, "type": "Http"}'

# 2. تست Proxy
curl -X POST http://localhost:5000/api/proxy/[proxy-id]/test

# 3. فعال‌سازی Proxy Pool
curl -X POST http://localhost:5000/api/proxy/pools \
  -d '{"name": "Main Pool", "proxyIds": ["proxy-id"]}'

# 4. بررسی آمار
curl http://localhost:5000/api/proxy/stats
```

## 10. معیارهای موفقیت

### 10.1 عملکرد
- [ ] Master server پاسخ‌دهی زیر 200ms
- [ ] Worker registration زیر 1 ثانیه  
- [ ] Task processing حداقل 100 CVV در دقیقه
- [ ] UI responsive زیر 2 ثانیه

### 10.2 قابلیت اطمینان
- [ ] Uptime بالای 99.9%
- [ ] Zero data loss در Redis
- [ ] Automatic recovery در عرض 30 ثانیه
- [ ] Error rate زیر 0.1%

### 10.3 مقیاس‌پذیری
- [ ] پشتیبانی از حداقل 10 Worker همزمان
- [ ] پردازش همزمان 1000 CVV
- [ ] Database تا 1 میلیون رکورد
- [ ] Memory usage زیر 1GB

## 11. گزارش نهایی

پس از اجرای تمام تست‌ها:

### ✅ موارد موفق
- [ ] تمام Unit Tests پاس شدند
- [ ] Integration Tests بدون خطا
- [ ] Performance ملاقات معیارها
- [ ] Security vulnerabilities رفع شدند
- [ ] UI/UX تست شد

### ❌ موارد نیاز به بهبود
- [ ] [فهرست مسائل باقی‌مانده]
- [ ] [پیشنهادات بهبود]
- [ ] [اولویت‌بندی رفع مسائل]

### 📋 مراحل بعدی
1. رفع مسائل شناسایی شده
2. بهینه‌سازی عملکرد  
3. استقرار در محیط Production
4. نظارت مداوم و maintenance