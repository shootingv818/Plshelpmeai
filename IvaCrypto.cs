using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IvaScanner
{
    public sealed class IvaCrypto
    {
        private readonly IKeyStore _store;

        public IvaCrypto(IKeyStore store)
        {
            _store = store;
        }

        public byte[] GenerateKey(int? bytes = null)
        {
            return RandomNumberGenerator.GetBytes(bytes ?? 32);
        }

        public string AesEncrypt(string plaintext, string? keyBase64 = null)
        {
            return AesEncryptWithIv(plaintext, keyBase64, IvaConstants.DefaultAesIv);
        }

        public string AesEncrypt2(string plaintext, string? keyBase64 = null, byte[]? iv = null)
        {
            return AesEncryptWithIv(plaintext, keyBase64, iv ?? IvaConstants.CustomAesIv);
        }

        public string AesDecrypt(string hex, string? keyBase64 = null)
        {
            return AesDecryptWithIv(hex, keyBase64, IvaConstants.DefaultAesIv);
        }

        private byte[] ResolveSharedKey(string? keyBase64)
        {
            var b64 = keyBase64 ?? _store.Get(IvaConstants.StorageKeys.SharedKey)
                ?? throw new InvalidOperationException("Shared key is not set. Run KeyExchange first.");
            return Convert.FromBase64String(b64);
        }

        private string AesEncryptWithIv(string plaintext, string? keyBase64, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = ResolveSharedKey(keyBase64);
            aes.IV = iv;

            var data = Encoding.UTF8.GetBytes(plaintext);
            using var enc = aes.CreateEncryptor();
            var cipher = enc.TransformFinalBlock(data, 0, data.Length);
            return Convert.ToHexString(cipher).ToLowerInvariant();
        }

        private string AesDecryptWithIv(string hex, string? keyBase64, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = ResolveSharedKey(keyBase64);
            aes.IV = iv;

            var data = Convert.FromHexString(hex);
            using var dec = aes.CreateDecryptor();
            var plain = dec.TransformFinalBlock(data, 0, data.Length);
            return Encoding.UTF8.GetString(plain);
        }

        public string Hmac(string data)
        {
            var keyB64 = _store.Get(IvaConstants.StorageKeys.WorkingKey)
                ?? throw new InvalidOperationException("Working key is not set. Run KeyExchange first.");
            var key = Convert.FromBase64String(keyB64);
            using var hmac = new HMACSHA256(key);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash);
        }

        public string RsaEncrypt(string plaintext)
        {
            using var rsa = ImportPublicKey(
                _store.Get(IvaConstants.StorageKeys.RsaPublic)
                ?? throw new InvalidOperationException("RSA public key is not set."));
            var cipher = rsa.Encrypt(Encoding.UTF8.GetBytes(plaintext), RSAEncryptionPadding.Pkcs1);
            return Convert.ToHexString(cipher).ToLowerInvariant();
        }

        public static RSA ImportPublicKey(string key)
        {
            var rsa = RSA.Create();
            var trimmed = key.Trim();

            if (trimmed.Contains("BEGIN", StringComparison.Ordinal))
            {
                rsa.ImportFromPem(trimmed);
                return rsa;
            }

            var bytes = Convert.FromBase64String(StripBase64(trimmed));

            try
            {
                rsa.ImportSubjectPublicKeyInfo(bytes, out _);
                return rsa;
            }
            catch (CryptographicException)
            {
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = bytes,
                    Exponent = new byte[] { 0x01, 0x00, 0x01 },
                });
                return rsa;
            }
        }

        private static string StripBase64(string s) =>
            s.Replace("\r", "").Replace("\n", "").Replace(" ", "");

        public static string Base64ToHex(string base64) =>
            Convert.ToHexString(Convert.FromBase64String(base64)).ToLowerInvariant();

        public static string HexToBase64(string hex) =>
            Convert.ToBase64String(Convert.FromHexString(hex));

        public static string Base64ModulusToPem(string base64Modulus)
        {
            const string spkiPrefix =
                "30820122300D06092A864886F70D01010105000382010F003082010A0282010100";
            const string spkiSuffix = "0203010001";
            var der = spkiPrefix + Base64ToHex(base64Modulus) + spkiSuffix;
            var b64 = HexToBase64(der);
            var lines = string.Join("\n",
                Enumerable.Range(0, (b64.Length + 63) / 64)
                          .Select(i => b64.Substring(i * 64, Math.Min(64, b64.Length - i * 64))));
            return $"-----BEGIN PUBLIC KEY-----\n{lines}\n-----END PUBLIC KEY-----";
        }
    }
}