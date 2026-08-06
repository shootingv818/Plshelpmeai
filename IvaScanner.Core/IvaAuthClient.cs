using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace IvaScanner.Core
{
    public sealed class IvaAuthClient : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly IvaOptions _opts;
        private string? _currentTransactionId;

        public string? CurrentPhone { get; private set; }
        public IKeyStore Store { get; }
        public IvaCrypto Crypto { get; }

        public IvaAuthClient(IvaOptions? options = null, IKeyStore? store = null, HttpClient? http = null)
        {
            _opts = options ?? new IvaOptions();
            Store = store ?? new InMemoryKeyStore();
            Crypto = new IvaCrypto(Store);
            _currentTransactionId = _opts.TransactionId ?? Guid.NewGuid().ToString();

            _ownsHttp = http is null;
            _http = http ?? new HttpClient();
            _http.Timeout = _opts.Timeout;
        }

        public async Task<string> FetchPublicKeyAsync(
            string? keyId = null, string? transactionId = null, CancellationToken ct = default)
        {
            var finalKeyId = keyId ?? _opts.KeyId ?? "1";
            var finalTransactionId = transactionId ?? _opts.TransactionId ?? _currentTransactionId ?? Guid.NewGuid().ToString();
            _currentTransactionId = finalTransactionId;

            var body = new { keyId = finalKeyId, transactionId = finalTransactionId };
            var json = JsonSerializer.Serialize(body, Json);

            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.PublicKeyUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
                throw new Exception($"getKey failed (HTTP {(int)res.StatusCode}): {text}");

            string? keyData = null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
                {
                    var errorMsg = string.Join(", ", errs.EnumerateArray().Select(e => 
                        e.TryGetProperty("errorDescription", out var desc) ? desc.GetString() : e.GetRawText()));
                    throw new Exception($"getKey returned errors: {errorMsg}");
                }

                keyData = TryGetString(root, "keyData")
                       ?? (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? TryGetString(d, "keyData") : null)
                       ?? TryGetString(root, "data");
            }
            catch (JsonException)
            {
            }

            if (string.IsNullOrEmpty(keyData))
                throw new Exception("getKey response did not contain keyData: " + text);

            Store.Set(IvaConstants.StorageKeys.RsaPublic, keyData);
            return keyData;
        }

        public async Task KeyExchangeAsync(CancellationToken ct = default)
        {
            var sharedKey = Crypto.GenerateKey();
            Store.Set(IvaConstants.StorageKeys.SharedKey, Convert.ToBase64String(sharedKey));

            var workingKey = Crypto.GenerateKey();
            Store.Set(IvaConstants.StorageKeys.WorkingKey, Convert.ToBase64String(workingKey));

            var sharedHex = Convert.ToHexString(sharedKey).ToLowerInvariant();
            var workingHex = Convert.ToHexString(workingKey).ToLowerInvariant();

            var dataKey = Crypto.RsaEncrypt(sharedHex);
            var macKey = Crypto.RsaEncrypt(workingHex);

            var sent = new { DataKey = dataKey, MacKey = macKey };
            await PostTolerantAsync(IvaConstants.Endpoints.KeyExchange, sent, ct).ConfigureAwait(false);
        }

        public Task<OtpRequestResult> RequestOtpAsync(string phoneNumber, CancellationToken ct = default)
        {
            CurrentPhone = phoneNumber;
            return PostDataAsync<OtpRequestResult>(IvaConstants.Endpoints.RegisterRequest,
                new { PhoneNumber = phoneNumber }, ct);
        }

        public async Task<TokenResult> VerifyCodeAsync(
            string verificationCode, string? token, string? reagentNumber, CancellationToken ct = default)
        {
            var data = await PostDataAsync<TokenResult>(IvaConstants.Endpoints.Activation, new
            {
                VerificationCode = verificationCode,
                Token = token,
                ReagentNumber = reagentNumber,
            }, ct).ConfigureAwait(false);

            PersistTokens(data);
            return data;
        }

        private void PersistTokens(TokenResult d)
        {
            if (!string.IsNullOrEmpty(d.RefreshToken)) Store.Set(IvaConstants.StorageKeys.RefreshToken, d.RefreshToken!);
            if (!string.IsNullOrEmpty(d.AccessToken)) Store.Set(IvaConstants.StorageKeys.Token, d.AccessToken!);
            if (d.ExpiresIn.HasValue) Store.Set(IvaConstants.StorageKeys.AccessTokenExpTime, d.ExpiresIn.Value.ToString());
            if (!string.IsNullOrEmpty(d.TokenType)) Store.Set(IvaConstants.StorageKeys.TokenType, d.TokenType!);
            Store.Set(IvaConstants.StorageKeys.AccessTokenObtainedAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

            if (!string.IsNullOrEmpty(d.Key))
            {
                try { Store.Set(IvaConstants.StorageKeys.RsaPublic, IvaCrypto.Base64ModulusToPem(d.Key!)); }
                catch { }
            }
        }

        public async Task<IReadOnlyList<ChargeOperator>> GetChargeCatalogAsync(CancellationToken ct = default)
        {
            var data = await GetDataElementAsync(IvaConstants.Endpoints.ChargeCatalog, query: null, ct).ConfigureAwait(false);

            var array = FindFirstArray(data);
            if (array is null) return Array.Empty<ChargeOperator>();

            return array.Value.Deserialize<List<ChargeOperator>>(Json) ?? new List<ChargeOperator>();
        }

        public async Task<ChargePurchaseResult> BuyChargeAsync(
            ChargePurchaseRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Card.Validate();

            var contentType = "application/vnd.sadad.payment.charge." + 
                             (!string.IsNullOrEmpty(request.Card.Token) ? "Token" : "pan") + "+json";

            await EnsureSecureChannelAsync(ct).ConfigureAwait(false);

            long? orderId = request.OrderId;
            Dictionary<string, object?> BuildBody()
            {
                var extra = new Dictionary<string, object?>
                {
                    ["TTL"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["TargetMobileNo"] = request.TargetMobileNo,
                    ["ProviderId"] = request.ProviderId,
                };
                if (request.Extra is not null)
                    foreach (var kv in request.Extra) extra[kv.Key] = kv.Value;

                var body = CreatePaymentBody(request.Amount, request.Card, extra, pocketId: null, orderId);
                orderId ??= (long)body["OrderId"]!;
                return body;
            }

            var built = BuildBody();
            var (status, text) = await PostSignedOnceAsync(IvaConstants.Endpoints.PayCharge, built, contentType, ct)
                .ConfigureAwait(false);

            if (status == HttpStatusCode.Unauthorized)
            {
                await RefreshAuthAsync(ct).ConfigureAwait(false);
                (status, text) = await PostSignedOnceAsync(IvaConstants.Endpoints.PayCharge, BuildBody(), contentType, ct)
                    .ConfigureAwait(false);
            }

            return ParseChargeOutcome(text, status);
        }

        public Dictionary<string, object?> CreatePaymentBody(
            long amount, CardPayment card,
            IReadOnlyDictionary<string, object?>? extra = null,
            string? pocketId = null, long? orderId = null)
        {
            if (!Store.Has(IvaConstants.StorageKeys.SharedKey))
                throw new InvalidOperationException("Secure channel not established. Call EnsureSecureChannelAsync first.");

            var media = new Dictionary<string, object?>();

            if (!string.IsNullOrEmpty(card.Cvv2))
                media["Cvv2"] = Crypto.AesEncrypt(card.Cvv2!);

            if (!string.IsNullOrEmpty(card.Pin))
                media["Pin"] = Crypto.AesEncrypt(card.Pin!);

            var expire = (card.ExpireYear ?? string.Empty) + PadLeft2(card.ExpireMonth);
            if (DigitsOnly(expire).Length == 4)
                media["ExpireDate"] = Crypto.AesEncrypt(expire);

            if (!string.IsNullOrEmpty(card.Token))
                media["Token"] = card.Token;
            else if (!string.IsNullOrEmpty(card.Pan))
                media["Pan"] = Crypto.AesEncrypt(card.Pan!);

            if (!string.IsNullOrEmpty(pocketId))
                media["PocketId"] = pocketId;

            var body = new Dictionary<string, object?> { ["paymentMedia"] = media };
            if (extra is not null)
                foreach (var kv in extra) body[kv.Key] = kv.Value;

            body["Amount"] = amount;
            body["OrderId"] = orderId ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return body;
        }

        private async Task EnsureSecureChannelAsync(CancellationToken ct = default)
        {
            if (Store.Has(IvaConstants.StorageKeys.SharedKey) && Store.Has(IvaConstants.StorageKeys.WorkingKey))
                return;

            if (!Store.Has(IvaConstants.StorageKeys.RsaPublic))
                await TryDiscoverPublicKeyAsync(ct).ConfigureAwait(false);

            if (!Store.Has(IvaConstants.StorageKeys.RsaPublic))
                throw new InvalidOperationException(
                    "RSA public key could not be found. Provide it via SetPublicKey.");

            await KeyExchangeAsync(ct).ConfigureAwait(false);
        }

        public async Task TryDiscoverPublicKeyAsync(CancellationToken ct = default)
        {
            var version = _opts.AppVersion.Split('.');
            var query = new Dictionary<string, string?>
            {
                ["VersionCode"] = version.Length > 2 ? version[2] : _opts.AppVersion,
                ["ClientType"] = "3",
                ["MarketType"] = "4",
            };

            JsonElement config;
            try
            {
                config = await GetDataElementAsync(IvaConstants.Endpoints.AppConfiguration, query, ct).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            var key = FindPublicKey(config);
            if (!string.IsNullOrEmpty(key))
            {
                Store.Set(IvaConstants.StorageKeys.RsaPublic, key!);
            }
        }

        private async Task<JsonElement> GetDataElementAsync(
            string path, IReadOnlyDictionary<string, string?>? query, CancellationToken ct)
        {
            var url = _opts.BaseAddress + path + BuildQuery(query);
            using var res = await SendAuthorizedAsync(
                () => new HttpRequestMessage(HttpMethod.Get, url), ct).ConfigureAwait(false);

            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            ThrowIfErrorEnvelope(root, res.StatusCode);
            return root.TryGetProperty("data", out var data) ? data.Clone() : root.Clone();
        }

        public async Task<HttpResponseMessage> SendAuthorizedAsync(
            Func<HttpRequestMessage> requestFactory, CancellationToken ct = default)
        {
            if (IsAccessTokenExpired())
                await RefreshAuthAsync(ct).ConfigureAwait(false);

            var res = await SendOnceAsync(requestFactory(), ct).ConfigureAwait(false);
            if (res.StatusCode != HttpStatusCode.Unauthorized) return res;

            res.Dispose();
            await RefreshAuthAsync(ct).ConfigureAwait(false);
            return await SendOnceAsync(requestFactory(), ct).ConfigureAwait(false);
        }

        private Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string body = string.Empty;
            if (req.Content is not null)
                body = req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();

            ApplyHeaders(req, req.RequestUri?.AbsolutePath ?? string.Empty, body);
            return _http.SendAsync(req, ct);
        }

        private void ApplyHeaders(HttpRequestMessage req, string path, string serializedBody)
        {
            if (Store.Has(IvaConstants.StorageKeys.Token))
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Store.Get(IvaConstants.StorageKeys.Token));
                req.Headers.TryAddWithoutValidation("iva-versioncode", _opts.AppVersion.Replace(".", ""));
                req.Headers.TryAddWithoutValidation("iva-versionname", _opts.AppVersion);
            }

            if (!string.IsNullOrEmpty(serializedBody) && !IvaConstants.SignExclude.Contains(path))
            {
                req.Headers.TryAddWithoutValidation("Sign-Data", Crypto.Hmac(serializedBody));
            }
        }

        private async Task RefreshAuthAsync(CancellationToken ct = default)
        {
            var rt = Store.Get(IvaConstants.StorageKeys.RefreshToken)
                ?? throw new InvalidOperationException("No refresh token available.");

            var data = await PostDataAsync<TokenResult>(IvaConstants.Endpoints.RefreshToken,
                new { RefreshToken = rt }, ct).ConfigureAwait(false);

            PersistTokens(data);
            if (Store.Has(IvaConstants.StorageKeys.RsaPublic))
                await KeyExchangeAsync(ct).ConfigureAwait(false);
        }

        public bool IsAccessTokenExpired(int skewSeconds = 30)
        {
            var obtained = ParseLong(Store.Get(IvaConstants.StorageKeys.AccessTokenObtainedAt));
            var expiresIn = ParseLong(Store.Get(IvaConstants.StorageKeys.AccessTokenExpTime));
            if (obtained is null || expiresIn is null) return false;

            var expiresAt = obtained.Value + expiresIn.Value;
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt - skewSeconds;
        }

        private async Task<T> PostDataAsync<T>(string path, object body, CancellationToken ct)
        {
            var envelope = await PostAsync<T>(path, body, ct).ConfigureAwait(false);
            return envelope.Data ?? throw new Exception("Empty response data.");
        }

        private async Task<ApiResponse<T>> PostAsync<T>(string path, object body, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(body, Json);

            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.BaseAddress + path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            ApplyHeaders(req, path, json);

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            ApiResponse<T>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ApiResponse<T>>(text, Json);
            }
            catch (JsonException)
            {
                throw new Exception($"Unexpected response (HTTP {(int)res.StatusCode}).");
            }

            parsed ??= new ApiResponse<T>();

            if (parsed.Error is { } err && err.Code != "200")
                throw new Exception(err.Message ?? "Operation failed.");

            return parsed;
        }

        private async Task PostTolerantAsync(string path, object body, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(body, Json);
            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.BaseAddress + path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            ApplyHeaders(req, path, json);

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
                throw new Exception($"{path} failed (HTTP {(int)res.StatusCode}).");

            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    var env = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(text, Json);
                    if (env?.Error is { } err && err.Code != "200")
                        throw new Exception(err.Message ?? "Operation failed.");
                }
                catch (JsonException) { }
            }
        }

        private async Task<(HttpStatusCode Status, string Text)> PostSignedOnceAsync(
            string path, object body, string contentType, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(body, Json);

            using var content = new StringContent(json, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.BaseAddress + path) { Content = content };
            ApplyHeaders(req, path, json);

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (res.StatusCode, text);
        }

        private static ChargePurchaseResult ParseChargeOutcome(string text, HttpStatusCode status)
        {
            try
            {
                var env = JsonSerializer.Deserialize<ApiResponse<ChargePurchaseResult>>(text, Json);
                if (env?.Error is { } err && err.Code != "200")
                    return new ChargePurchaseResult { Success = false, ErrorCode = err.Code, Message = err.Message };

                var data = env?.Data ?? new ChargePurchaseResult();
                data.Success = true;
                return data;
            }
            catch (JsonException)
            {
                return new ChargePurchaseResult { Success = false, ErrorCode = ((int)status).ToString(), Message = $"HTTP {(int)status}" };
            }
        }

        private static string BuildQuery(IReadOnlyDictionary<string, string?>? query)
        {
            if (query is null || query.Count == 0) return string.Empty;
            var parts = query.Where(kv => kv.Value is not null)
                             .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
            var joined = string.Join("&", parts);
            return joined.Length == 0 ? string.Empty : "?" + joined;
        }

        private static void ThrowIfErrorEnvelope(JsonElement root, HttpStatusCode status)
        {
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("error", out var err) || err.ValueKind != JsonValueKind.Object) return;
            if (!err.TryGetProperty("code", out var code)) return;

            var codeStr = code.ValueKind == JsonValueKind.Number ? code.GetRawText() : code.GetString();
            if (codeStr is null || codeStr == "200") return;

            var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new Exception(message ?? "Operation failed.");
        }

        private static JsonElement? FindFirstArray(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Array) return el;
            if (el.ValueKind == JsonValueKind.Object)
                foreach (var prop in el.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        return prop.Value;
            return null;
        }

        private static string? FindPublicKey(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        var name = prop.Name.ToLowerInvariant();
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var val = prop.Value.GetString() ?? "";
                            var nameMatch = (name.Contains("public") && name.Contains("key")) ||
                                            name.Contains("rsapublic") || name == "rsapublickey";
                            if (nameMatch && LooksLikeKey(val)) return val;
                        }
                        var nested = FindPublicKey(prop.Value);
                        if (nested is not null) return nested;
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                    {
                        var nested = FindPublicKey(item);
                        if (nested is not null) return nested;
                    }
                    break;
            }
            return null;
        }

        private static bool LooksLikeKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.Contains("BEGIN", StringComparison.Ordinal)) return true;
            var compact = s.Replace("\r", "").Replace("\n", "").Trim();
            return compact.Length >= 200 &&
                   compact.All(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=');
        }

        private static string? TryGetString(JsonElement el, string name) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private static string PadLeft2(string? s) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s!.Length >= 2 ? s : s.PadLeft(2, '0'));

        private static string DigitsOnly(string s) =>
            new(s.Where(char.IsDigit).ToArray());

        private static long? ParseLong(string? s) =>
            long.TryParse(s, out var v) ? v : null;

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}