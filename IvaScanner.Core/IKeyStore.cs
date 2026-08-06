using System.Collections.Concurrent;

namespace IvaScanner.Core
{
    public interface IKeyStore
    {
        string? Get(string key);
        void Set(string key, string value);
        bool Has(string key);
        void Remove(string key);
    }

    public sealed class InMemoryKeyStore : IKeyStore
    {
        private readonly ConcurrentDictionary<string, string> _data = new();

        public string? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => _data[key] = value;
        public bool Has(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.TryRemove(key, out _);
    }
}