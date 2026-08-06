using IvaScanner.Master.Services;
using System.Net;
using System.Text.Json;

namespace IvaScanner.Master.Middleware
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
        private readonly IServiceProvider _serviceProvider;

        public GlobalExceptionHandlerMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlerMiddleware> logger,
            IServiceProvider serviceProvider)
        {
            _next = next;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var response = context.Response;
            var request = context.Request;

            // Log the exception
            _logger.LogError(exception, 
                "Unhandled exception occurred. Path: {Path}, Method: {Method}, User: {User}", 
                request.Path, request.Method, context.User?.Identity?.Name ?? "Anonymous");

            // Record error in system logs if available
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var systemLogService = scope.ServiceProvider.GetService<ISystemLogService>();
                if (systemLogService != null)
                {
                    await systemLogService.LogErrorAsync("GlobalExceptionHandler", exception.Message, exception.ToString());
                }
            }
            catch (Exception logException)
            {
                _logger.LogError(logException, "Failed to log exception to system logs");
            }

            // Prepare error response
            var errorResponse = new ErrorResponse();
            
            switch (exception)
            {
                case UnauthorizedAccessException:
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    errorResponse.Message = "دسترسی غیرمجاز";
                    errorResponse.Code = "UNAUTHORIZED";
                    break;
                    
                case ArgumentNullException:
                case ArgumentException:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "درخواست نامعتبر";
                    errorResponse.Code = "INVALID_REQUEST";
                    break;
                    
                case KeyNotFoundException:
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    errorResponse.Message = "منبع مورد نظر یافت نشد";
                    errorResponse.Code = "NOT_FOUND";
                    break;
                    
                case InvalidOperationException:
                    response.StatusCode = (int)HttpStatusCode.Conflict;
                    errorResponse.Message = "عملیات در وضعیت فعلی امکان‌پذیر نیست";
                    errorResponse.Code = "INVALID_OPERATION";
                    break;
                    
                case TimeoutException:
                case TaskCanceledException:
                    response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                    errorResponse.Message = "درخواست منقضی شد";
                    errorResponse.Code = "TIMEOUT";
                    break;
                    
                case NotImplementedException:
                    response.StatusCode = (int)HttpStatusCode.NotImplemented;
                    errorResponse.Message = "این قابلیت هنوز پیاده‌سازی نشده است";
                    errorResponse.Code = "NOT_IMPLEMENTED";
                    break;
                    
                default:
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    errorResponse.Message = "خطای داخلی سرور";
                    errorResponse.Code = "INTERNAL_ERROR";
                    break;
            }

            // Add additional details in development
            if (IsDevelopmentEnvironment())
            {
                errorResponse.Details = exception.Message;
                errorResponse.StackTrace = exception.StackTrace;
            }

            errorResponse.Timestamp = DateTime.UtcNow;
            errorResponse.TraceId = context.TraceIdentifier;

            // Set response headers
            response.ContentType = "application/json";

            // Handle different response types
            if (IsApiRequest(request))
            {
                // API request - return JSON
                var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
                
                await response.WriteAsync(jsonResponse);
            }
            else
            {
                // Web request - redirect to error page or return HTML
                if (response.StatusCode >= 400 && response.StatusCode < 500)
                {
                    response.Redirect($"/Home/Error?statusCode={response.StatusCode}");
                }
                else
                {
                    response.Redirect("/Home/Error");
                }
            }
        }

        private bool IsApiRequest(HttpRequest request)
        {
            return request.Path.StartsWithSegments("/api") ||
                   request.Headers["Accept"].ToString().Contains("application/json") ||
                   request.Headers["Content-Type"].ToString().Contains("application/json");
        }

        private bool IsDevelopmentEnvironment()
        {
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        }
    }

    public class ErrorResponse
    {
        public string Message { get; set; } = "";
        public string Code { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string TraceId { get; set; } = "";
        public string? Details { get; set; }
        public string? StackTrace { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    // Extension method to register the middleware
    public static class GlobalExceptionHandlerMiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        }
    }
}