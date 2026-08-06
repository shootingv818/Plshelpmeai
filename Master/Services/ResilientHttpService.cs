using IvaScanner.Master.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public interface IResilientHttpService
    {
        Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default);

        Task<T?> GetJsonAsync<T>(
            string url,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default);

        Task<HttpResponseMessage> PostJsonAsync<T>(
            string url,
            T content,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default);

        Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
            string url,
            TRequest content,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default);

        // Proxy-aware HTTP methods
        Task<HttpResponseMessage> SendWithProxyAsync(
            HttpRequestMessage request,
            ProxyConfiguration? proxy = null,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default);
    }

    public class ResilientHttpService : IResilientHttpService, IDisposable
    {
        private readonly IErrorHandlingService _errorHandling;
        private readonly ISystemLogService _systemLog;
        private readonly IProxyService _proxyService;
        private readonly ILogger<ResilientHttpService> _logger;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public ResilientHttpService(
            IErrorHandlingService errorHandling,
            ISystemLogService systemLog,
            IProxyService proxyService,
            ILogger<ResilientHttpService> logger,
            HttpClient httpClient)
        {
            _errorHandling = errorHandling;
            _systemLog = systemLog;
            _proxyService = proxyService;
            _logger = logger;
            _httpClient = httpClient;
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            // Configure default timeouts
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default)
        {
            operationName ??= $"{request.Method} {request.RequestUri?.Host}";
            policy ??= CreateDefaultHttpPolicy();

            return await _errorHandling.ExecuteWithResilienceAsync(
                $"http_{operationName}",
                async () =>
                {
                    var response = await _httpClient.SendAsync(request, cancellationToken);

                    // Log HTTP errors for monitoring
                    if (!response.IsSuccessStatusCode)
                    {
                        await _systemLog.LogWarningAsync(
                            $"HTTP {request.Method} {request.RequestUri} returned {response.StatusCode}: {response.ReasonPhrase}",
                            "http_error",
                            "ResilientHttpService"
                        );

                        // Add status code to exception for retry logic
                        var httpException = new HttpRequestException($"HTTP {response.StatusCode}: {response.ReasonPhrase}");
                        httpException.Data["StatusCode"] = response.StatusCode;
                        
                        // Don't throw for client errors (4xx) unless it's retryable
                        if (IsRetryableStatusCode(response.StatusCode))
                        {
                            throw httpException;
                        }
                    }

                    return response;
                },
                policy,
                cancellationToken);
        }

        public async Task<T?> GetJsonAsync<T>(
            string url,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default)
        {
            operationName ??= $"GET {new Uri(url).Host}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await SendAsync(request, operationName, policy, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return default(T);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(content))
            {
                return default(T);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(content, _jsonOptions);
            }
            catch (JsonException ex)
            {
                await _systemLog.LogWarningAsync(
                    $"Failed to deserialize JSON response from {url}: {ex.Message}",
                    "json_deserialization_error",
                    "ResilientHttpService"
                );
                return default(T);
            }
        }

        public async Task<HttpResponseMessage> PostJsonAsync<T>(
            string url,
            T content,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default)
        {
            operationName ??= $"POST {new Uri(url).Host}";

            var json = JsonSerializer.Serialize(content, _jsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            return await SendAsync(request, operationName, policy, cancellationToken);
        }

        public async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
            string url,
            TRequest content,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default)
        {
            var response = await PostJsonAsync(url, content, operationName, policy, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return default(TResponse);
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(responseContent))
            {
                return default(TResponse);
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions);
            }
            catch (JsonException ex)
            {
                await _systemLog.LogWarningAsync(
                    $"Failed to deserialize JSON response from {url}: {ex.Message}",
                    "json_deserialization_error",
                    "ResilientHttpService"
                );
                return default(TResponse);
            }
        }

        public async Task<HttpResponseMessage> SendWithProxyAsync(
            HttpRequestMessage request,
            ProxyConfiguration? proxy = null,
            string? operationName = null,
            ResiliencePolicy? policy = null,
            CancellationToken cancellationToken = default)
        {
            operationName ??= $"{request.Method} {request.RequestUri?.Host} (Proxy)";

            // Get proxy if not provided
            if (proxy == null)
            {
                var proxyServer = await _proxyService.GetNextProxyForWorkerAsync("http_client");
                if (proxyServer != null)
                {
                    proxy = new ProxyConfiguration
                    {
                        Host = proxyServer.Host,
                        Port = proxyServer.Port,
                        Type = proxyServer.Type.ToString(),
                        Username = proxyServer.Username,
                        Password = proxyServer.Password
                    };
                }
            }

            if (proxy != null)
            {
                using var handler = CreateProxyHandler(proxy);
                using var proxyClient = new HttpClient(handler)
                {
                    Timeout = _httpClient.Timeout
                };

                // Copy default headers
                foreach (var header in _httpClient.DefaultRequestHeaders)
                {
                    proxyClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }

                return await _errorHandling.ExecuteWithResilienceAsync(
                    $"proxy_{operationName}",
                    async () =>
                    {
                        var response = await proxyClient.SendAsync(request, cancellationToken);

                        if (!response.IsSuccessStatusCode && IsRetryableStatusCode(response.StatusCode))
                        {
                            var httpException = new HttpRequestException($"Proxy HTTP {response.StatusCode}: {response.ReasonPhrase}");
                            httpException.Data["StatusCode"] = response.StatusCode;
                            throw httpException;
                        }

                        return response;
                    },
                    policy ?? CreateDefaultProxyPolicy(),
                    cancellationToken);
            }
            else
            {
                // Fall back to direct connection
                await _systemLog.LogWarningAsync(
                    "No proxy available, falling back to direct connection",
                    "proxy_fallback",
                    "ResilientHttpService"
                );
                
                return await SendAsync(request, operationName, policy, cancellationToken);
            }
        }

        private HttpClientHandler CreateProxyHandler(ProxyConfiguration proxy)
        {
            var proxyUri = new Uri($"http://{proxy.Host}:{proxy.Port}");
            var webProxy = new WebProxy(proxyUri);

            if (!string.IsNullOrEmpty(proxy.Username) && !string.IsNullOrEmpty(proxy.Password))
            {
                webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
            }

            return new HttpClientHandler
            {
                Proxy = webProxy,
                UseProxy = true,
                PreAuthenticate = true
            };
        }

        private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.RequestTimeout => true,
                HttpStatusCode.TooManyRequests => true,
                HttpStatusCode.InternalServerError => true,
                HttpStatusCode.BadGateway => true,
                HttpStatusCode.ServiceUnavailable => true,
                HttpStatusCode.GatewayTimeout => true,
                _ when ((int)statusCode >= 500) => true,
                _ => false
            };
        }

        private ResiliencePolicy CreateDefaultHttpPolicy()
        {
            return new ResiliencePolicy
            {
                RetryPolicy = RetryPolicies.Network,
                CircuitBreakerPolicy = CircuitBreakerPolicies.ExternalApi,
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private ResiliencePolicy CreateDefaultProxyPolicy()
        {
            return new ResiliencePolicy
            {
                RetryPolicy = RetryPolicies.ProxyTest,
                CircuitBreakerPolicy = CircuitBreakerPolicies.ProxyService,
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class ProxyConfiguration
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Type { get; set; } = "Http";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}