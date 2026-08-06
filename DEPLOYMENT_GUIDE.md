# راهنمای استقرار سیستم IVA Scanner

## نمای کلی

این راهنمای کاملی برای استقرار سیستم توزیع‌شده IVA Scanner در محیط Production است.

## 1. معماری سیستم

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Load Balancer │    │   Master Server │    │  Worker Clients │
│     (nginx)     │◄──►│  (ASP.NET Core) │◄──►│   (.NET 8.0)    │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                              │                         │
                              ▼                         ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   SQL Server    │    │  Redis Server   │    │  Proxy Servers  │
│   (Database)    │    │ (Task Queue)    │    │   (Optional)    │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

## 2. نیازمندی‌های سیستم

### 2.1 Master Server

**حداقل مشخصات:**
- CPU: 4 Core (Intel/AMD x64)
- RAM: 8 GB
- Storage: 100 GB SSD
- Network: 1 Gbps
- OS: Windows Server 2019+ یا Ubuntu 20.04+

**نرم‌افزارهای مورد نیاز:**
- .NET 8.0 Runtime
- SQL Server 2019+ (یا PostgreSQL)
- Redis 6.0+
- IIS (Windows) یا nginx (Linux)

### 2.2 Worker Clients  

**حداقل مشخصات:**
- CPU: 2 Core
- RAM: 4 GB  
- Storage: 20 GB
- Network: 100 Mbps
- OS: Windows 10+ یا Linux

**نرم‌افزارهای مورد نیاز:**
- .NET 8.0 Runtime

### 2.3 Database Server

**حداقل مشخصات:**
- CPU: 4 Core
- RAM: 16 GB
- Storage: 500 GB SSD
- OS: Windows Server یا Linux

## 3. استقرار Master Server

### 3.1 Windows Server با IIS

```powershell
# نصب .NET 8.0 Runtime
Invoke-WebRequest -Uri "https://download.microsoft.com/..." -OutFile "dotnet-runtime.exe"
.\dotnet-runtime.exe /quiet

# نصب IIS و ASP.NET Core Module
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ASPNET47

# دانلود و نصب ASP.NET Core Hosting Bundle
Invoke-WebRequest -Uri "https://download.microsoft.com/..." -OutFile "hosting-bundle.exe"
.\hosting-bundle.exe /quiet

# ایجاد Application Pool
Import-Module WebAdministration
New-WebAppPool -Name "IvaScanner" -Force
Set-ItemProperty -Path "IIS:\AppPools\IvaScanner" -Name processModel.identityType -Value ApplicationPoolIdentity
```

**تنظیم IIS Site:**
```powershell
# ایجاد Website
New-Website -Name "IvaScanner" -ApplicationPool "IvaScanner" -PhysicalPath "C:\inetpub\wwwroot\iva-scanner" -Port 80

# تنظیم SSL Certificate (اختیاری)
New-SelfSignedCertificate -DnsName "iva-scanner.local" -CertStoreLocation "cert:\LocalMachine\My"
```

### 3.2 Linux با systemd

```bash
# نصب .NET 8.0 Runtime
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-8.0

# ایجاد کاربر service
sudo useradd --system --home /opt/iva-scanner --shell /usr/sbin/nologin iva-scanner

# ایجاد دایرکتوری
sudo mkdir -p /opt/iva-scanner/master
sudo chown iva-scanner:iva-scanner /opt/iva-scanner/master

# کپی فایل‌های application
sudo cp -r ./Master/bin/Release/net8.0/publish/* /opt/iva-scanner/master/
sudo chown -R iva-scanner:iva-scanner /opt/iva-scanner/master

# ایجاد systemd service
sudo tee /etc/systemd/system/iva-scanner.service > /dev/null <<EOF
[Unit]
Description=IVA Scanner Master Server
After=network.target

[Service]
Type=notify
User=iva-scanner
Group=iva-scanner
WorkingDirectory=/opt/iva-scanner/master
ExecStart=/usr/bin/dotnet /opt/iva-scanner/master/IvaScanner.Master.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOF

# فعال‌سازی و شروع service
sudo systemctl daemon-reload
sudo systemctl enable iva-scanner
sudo systemctl start iva-scanner
```

### 3.3 تنظیم nginx (Linux)

```nginx
# /etc/nginx/sites-available/iva-scanner
server {
    listen 80;
    server_name iva-scanner.local;
    
    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    
    # SignalR WebSocket support
    location /dashboardHub {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

# فعال‌سازی site
sudo ln -s /etc/nginx/sites-available/iva-scanner /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

## 4. تنظیم پایگاه داده

### 4.1 SQL Server

```sql
-- ایجاد Database
CREATE DATABASE IvaScanner;
GO

-- ایجاد User برای Application
CREATE LOGIN IvaScanner_User WITH PASSWORD = 'Strong_Password_Here!';
GO

USE IvaScanner;
GO

CREATE USER IvaScanner_User FOR LOGIN IvaScanner_User;
GO

-- اعطای دسترسی‌ها
ALTER ROLE db_datareader ADD MEMBER IvaScanner_User;
ALTER ROLE db_datawriter ADD MEMBER IvaScanner_User;
ALTER ROLE db_ddladmin ADD MEMBER IvaScanner_User;
GO
```

```bash
# اجرای Migration
cd Master
dotnet ef database update --connection "Server=localhost;Database=IvaScanner;User Id=IvaScanner_User;Password=Strong_Password_Here!;TrustServerCertificate=true"
```

### 4.2 PostgreSQL (جایگزین)

```bash
# نصب PostgreSQL
sudo apt-get install postgresql postgresql-contrib

# ایجاد Database و User
sudo -u postgres psql
postgres=# CREATE DATABASE ivascanner;
postgres=# CREATE USER iva_user WITH PASSWORD 'strong_password';
postgres=# GRANT ALL PRIVILEGES ON DATABASE ivascanner TO iva_user;
postgres=# \q

# اجرای Migration
cd Master  
dotnet ef database update --connection "Host=localhost;Database=ivascanner;Username=iva_user;Password=strong_password"
```

## 5. راه‌اندازی Redis

### 5.1 Windows

```powershell
# دانلود Redis for Windows
Invoke-WebRequest -Uri "https://github.com/microsoftarchive/redis/releases/download/win-3.2.100/Redis-x64-3.2.100.msi" -OutFile "redis.msi"
Start-Process msiexec.exe -ArgumentList '/i redis.msi /quiet' -Wait

# شروع Redis Service
Start-Service Redis
```

### 5.2 Linux

```bash
# نصب Redis
sudo apt-get install redis-server

# تنظیمات امنیتی
sudo nano /etc/redis/redis.conf

# تغییرات مورد نیاز:
bind 127.0.0.1
requirepass your_strong_password_here
maxmemory 2gb
maxmemory-policy allkeys-lru

# راه‌اندازی مجدد
sudo systemctl restart redis-server
sudo systemctl enable redis-server

# بررسی وضعیت
redis-cli ping
```

## 6. تنظیمات Configuration

### 6.1 Master Server - appsettings.Production.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Server=db-server;Database=IvaScanner;User Id=iva_user;Password=strong_password;TrustServerCertificate=true",
    "Redis": "localhost:6379,password=redis_password"
  },
  "Worker": {
    "HeartbeatTimeout": "00:02:00",
    "TaskLeaseTimeout": "00:02:00",
    "MaxRetryAttempts": 3,
    "ConcurrentWorkers": 10
  },
  "TaskDistribution": {
    "ChunkSize": 100,
    "MaxQueuedTasks": 1000,
    "RetryDelay": "00:00:30"
  },
  "Logging": {
    "CleanupInterval": "24:00:00",
    "RetentionDays": 30
  },
  "Security": {
    "ApiKey": "your-secure-api-key-here",
    "JwtSecret": "your-jwt-secret-key-here",
    "AllowedOrigins": ["https://your-domain.com"]
  }
}
```

### 6.2 Worker Client - appsettings.Production.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "Master": {
    "BaseUrl": "https://master-server.com",
    "ApiKey": "your-secure-api-key-here",
    "ConnectionTimeout": "00:05:00",
    "RetryAttempts": 5,
    "RetryDelay": "00:01:00"
  },
  "Worker": {
    "Id": "",
    "Name": "Worker-{MachineName}",
    "MaxConcurrentTasks": 4,
    "HeartbeatInterval": "00:00:30",
    "TaskTimeout": "00:15:00",
    "WorkingDirectory": "/var/lib/iva-worker/temp"
  },
  "Proxy": {
    "Enabled": true,
    "RotationStrategy": "LeastUsed",
    "HealthCheckInterval": "00:05:00"
  }
}
```

## 7. استقرار Worker Clients

### 7.1 Linux systemd

```bash
# استفاده از اسکریپت نصب خودکار
cd Worker
chmod +x install-service.sh
sudo ./install-service.sh

# یا نصب دستی:
sudo mkdir -p /opt/iva-scanner/worker
sudo useradd --system iva-worker
dotnet publish -c Release -o /opt/iva-scanner/worker
sudo chown -R iva-worker:iva-worker /opt/iva-scanner/worker

# کپی systemd service file
sudo cp iva-worker.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable iva-worker
sudo systemctl start iva-worker
```

### 7.2 Windows Service

```powershell
# نصب به‌عنوان Windows Service
sc create "IvaWorker" binPath="C:\IvaScanner\Worker\IvaScanner.Worker.exe" start=auto
sc description "IvaWorker" "IVA Scanner Worker Service"

# شروع Service
sc start "IvaWorker"

# بررسی وضعیت
sc query "IvaWorker"
```

## 8. Load Balancer (اختیاری)

### 8.1 nginx Load Balancer

```nginx
# /etc/nginx/nginx.conf
upstream iva_masters {
    server master1.local:5000 weight=3;
    server master2.local:5000 weight=2;
    server master3.local:5000 backup;
}

server {
    listen 80;
    server_name iva-scanner.com;
    
    location / {
        proxy_pass http://iva_masters;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        
        # Health check
        health_check uri=/health interval=30s;
    }
}
```

## 9. SSL/TLS Setup

### 9.1 Let's Encrypt (Linux)

```bash
# نصب certbot
sudo apt-get install certbot python3-certbot-nginx

# دریافت SSL Certificate
sudo certbot --nginx -d iva-scanner.com

# تنظیم تمدید خودکار
sudo crontab -e
# افزودن خط زیر:
0 12 * * * /usr/bin/certbot renew --quiet
```

### 9.2 Self-Signed Certificate (Development)

```bash
# ایجاد Private Key
openssl genrsa -out iva-scanner.key 2048

# ایجاد Certificate Request
openssl req -new -key iva-scanner.key -out iva-scanner.csr

# ایجاد Self-Signed Certificate
openssl x509 -req -days 365 -in iva-scanner.csr -signkey iva-scanner.key -out iva-scanner.crt
```

## 10. نظارت و Monitoring

### 10.1 Health Checks

```bash
# بررسی Master Health
curl -f http://localhost:5000/health || exit 1

# بررسی Redis
redis-cli ping | grep PONG || exit 1

# بررسی Database
sqlcmd -S localhost -d IvaScanner -Q "SELECT 1" || exit 1
```

### 10.2 Application Insights (اختیاری)

```json
// appsettings.json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=your-key-here"
  }
}
```

### 10.3 Prometheus Metrics (اختیاری)

```csharp
// Program.cs
builder.Services.AddSingleton<IMetricsLogger, PrometheusMetricsLogger>();

app.UseMetricServer(); // /metrics endpoint
```

## 11. Backup و Recovery

### 11.1 Database Backup

```sql
-- SQL Server
BACKUP DATABASE IvaScanner 
TO DISK = 'C:\Backups\IvaScanner_Full.bak'
WITH INIT, COMPRESSION;

-- تنظیم Backup خودکار
-- SQL Server Agent Job ایجاد کنید
```

```bash
# PostgreSQL
pg_dump -h localhost -U iva_user ivascanner > backup_$(date +%Y%m%d).sql

# تنظیم cron job
0 2 * * * /usr/bin/pg_dump -h localhost -U iva_user ivascanner > /backups/ivascanner_$(date +\%Y\%m\%d).sql
```

### 11.2 Redis Backup

```bash
# Manual backup
redis-cli BGSAVE

# تنظیم automatic backup در redis.conf
save 900 1
save 300 10
save 60 10000
```

## 12. Security Best Practices

### 12.1 Network Security

```bash
# Firewall تنظیمات (ufw)
sudo ufw allow 80/tcp    # HTTP
sudo ufw allow 443/tcp   # HTTPS
sudo ufw allow 22/tcp    # SSH (فقط از IP های مجاز)

# بستن پورت‌های غیرضروری
sudo ufw deny 5000/tcp   # Block direct access to Kestrel
```

### 12.2 Application Security

```json
// تنظیمات امنیتی در appsettings
{
  "Security": {
    "RequireHttps": true,
    "ApiKeyRequired": true,
    "RateLimiting": {
      "RequestsPerMinute": 100,
      "BurstLimit": 20
    }
  }
}
```

## 13. Performance Tuning

### 13.1 Database Optimization

```sql
-- Index optimization
CREATE INDEX IX_ScanTasks_Status_WorkerId ON ScanTasks(Status, WorkerId);
CREATE INDEX IX_Workers_Status_LastHeartbeat ON Workers(Status, LastHeartbeat);

-- Statistics update
UPDATE STATISTICS ScanTasks;
UPDATE STATISTICS Workers;
```

### 13.2 Redis Optimization

```bash
# redis.conf optimizations
maxmemory-policy allkeys-lru
tcp-keepalive 60
timeout 300
```

### 13.3 Application Pool Settings (IIS)

```xml
<!-- applicationHost.config -->
<applicationPool name="IvaScanner">
    <processModel 
        idleTimeout="00:00:00"
        maxProcesses="1"
        requestQueueLimit="5000" />
    <recycling>
        <periodicRestart time="1.00:00:00" />
    </recycling>
</applicationPool>
```

## 14. Troubleshooting

### 14.1 مشکلات رایج

**Master Server راه‌اندازی نمی‌شود:**
```bash
# بررسی لاگ‌ها
journalctl -u iva-scanner -f

# بررسی Port binding
netstat -tlnp | grep :5000

# بررسی اتصال Database
sqlcmd -S localhost -d IvaScanner -Q "SELECT 1"
```

**Worker ثبت نام نمی‌شود:**
```bash
# بررسی اتصال شبکه
telnet master-server 5000

# بررسی API Key
curl -H "Authorization: Bearer your-api-key" http://master-server/api/health
```

**Redis اتصال برقرار نمی‌کند:**
```bash
# بررسی Redis status
redis-cli ping

# بررسی configuration
redis-cli CONFIG GET "*"
```

### 14.2 لاگ‌های مفید

```bash
# Master logs
tail -f /var/log/iva-scanner/master.log

# Worker logs  
tail -f /var/log/iva-scanner/worker.log

# nginx logs
tail -f /var/log/nginx/access.log
tail -f /var/log/nginx/error.log

# System logs
journalctl -u iva-scanner -f
journalctl -u iva-worker -f
```

## 15. Maintenance

### 15.1 به‌روزرسانی سیستم

```bash
# 1. Backup گیری
./backup-system.sh

# 2. متوقف کردن services
sudo systemctl stop iva-scanner
sudo systemctl stop iva-worker

# 3. به‌روزرسانی files
cp -r new-version/* /opt/iva-scanner/

# 4. اجرای migrations (در صورت نیاز)
cd /opt/iva-scanner/master
dotnet ef database update

# 5. راه‌اندازی مجدد
sudo systemctl start iva-scanner
sudo systemctl start iva-worker
```

### 15.2 Performance Monitoring

```bash
# CPU & Memory usage
top -p $(pgrep -f IvaScanner)

# Disk I/O
iotop -p $(pgrep -f IvaScanner)

# Network connections
ss -tulpn | grep :5000
```

این راهنما چارچوب کاملی برای استقرار موفق سیستم IVA Scanner فراهم می‌کند. برای هر محیط خاص، ممکن است تنظیمات اضافی مورد نیاز باشد.