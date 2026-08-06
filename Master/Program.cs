using IvaScanner.Master.Data;
using IvaScanner.Master.Hubs;
using IvaScanner.Master.Services;
using IvaScanner.Master.Middleware;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Serilog Configuration
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/master-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddDbContext<MasterDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis Configuration
builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

// HTTP Client for proxy testing
builder.Services.AddHttpClient();

// Core Services
builder.Services.AddScoped<IWorkerService, WorkerService>();
builder.Services.AddScoped<ITaskDistributionService, TaskDistributionService>();
builder.Services.AddScoped<IScanJobService, ScanJobService>();
builder.Services.AddScoped<IIvaAccountService, IvaAccountService>();
builder.Services.AddScoped<IRedisService, RedisService>();
builder.Services.AddScoped<IScanOrchestrator, ScanOrchestrator>();
builder.Services.AddScoped<ITaskProcessor, TaskProcessor>();
builder.Services.AddScoped<ISystemLogService, SystemLogService>();
builder.Services.AddScoped<IProxyService, ProxyService>();
builder.Services.AddScoped<ISignalRNotificationService, SignalRNotificationService>();
builder.Services.AddScoped<IRemoteServerService, RemoteServerService>();

// Error Handling and Resilience Services
builder.Services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();
builder.Services.AddSingleton<IResilientHttpService, ResilientHttpService>();
builder.Services.AddScoped<IResilientDatabaseService, ResilientDatabaseService>();

// SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// Background Services
builder.Services.AddHostedService<WorkerHealthMonitorService>();
builder.Services.AddHostedService<TaskProcessorService>();
builder.Services.AddHostedService<LogCleanupService>();
builder.Services.AddHostedService<ProxyHealthMonitorService>();

// MVC
builder.Services.AddControllersWithViews();

// CORS for SignalR
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Global Exception Handler Middleware
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();

// Database initialization (SQLite - schema created directly from the model)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
    context.Database.EnsureCreated();
}

// SignalR Hub
app.MapHub<DashboardHub>("/dashboardHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

Log.Information("IVA Scanner Master Server starting...");
app.Run();