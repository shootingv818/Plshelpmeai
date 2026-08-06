using StackExchange.Redis;
using System.Text.Json;

namespace IvaScanner.Master.Services
{
    public interface IRedisService
    {
        Task<bool> SetStringAsync(string key, string value, TimeSpan? expiry = null);
        Task<string?> GetStringAsync(string key);
        Task<bool> DeleteKeyAsync(string key);
        Task<bool> KeyExistsAsync(string key);
        Task<long> ListPushAsync(string key, string value);
        Task<string?> ListPopAsync(string key);
        Task<long> ListLengthAsync(string key);
        Task<bool> HashSetAsync(string key, string field, string value);
        Task<string?> HashGetAsync(string key, string field);
        Task<bool> HashDeleteAsync(string key, string field);
        Task<Dictionary<string, string>> HashGetAllAsync(string key);
        Task<long> StreamAddAsync(string stream, string field, string value);
        Task<StreamEntry[]> StreamReadAsync(string stream, string position, int count = 10);
        Task PublishAsync(string channel, string message);
        Task SubscribeAsync(string channel, Action<string, string> handler);
    }

    public class RedisService : IRedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _database;
        private readonly ISubscriber _subscriber;
        private readonly ILogger<RedisService> _logger;

        public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
        {
            _redis = redis;
            _database = redis.GetDatabase();
            _subscriber = redis.GetSubscriber();
            _logger = logger;
        }

        public async Task<bool> SetStringAsync(string key, string value, TimeSpan? expiry = null)
        {
            try
            {
                return await _database.StringSetAsync(key, value, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting Redis string key {Key}", key);
                return false;
            }
        }

        public async Task<string?> GetStringAsync(string key)
        {
            try
            {
                var value = await _database.StringGetAsync(key);
                return value.HasValue ? value.ToString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Redis string key {Key}", key);
                return null;
            }
        }

        public async Task<bool> DeleteKeyAsync(string key)
        {
            try
            {
                return await _database.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Redis key {Key}", key);
                return false;
            }
        }

        public async Task<bool> KeyExistsAsync(string key)
        {
            try
            {
                return await _database.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Redis key existence {Key}", key);
                return false;
            }
        }

        public async Task<long> ListPushAsync(string key, string value)
        {
            try
            {
                return await _database.ListRightPushAsync(key, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pushing to Redis list {Key}", key);
                return 0;
            }
        }

        public async Task<string?> ListPopAsync(string key)
        {
            try
            {
                var value = await _database.ListLeftPopAsync(key);
                return value.HasValue ? value.ToString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error popping from Redis list {Key}", key);
                return null;
            }
        }

        public async Task<long> ListLengthAsync(string key)
        {
            try
            {
                return await _database.ListLengthAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Redis list length {Key}", key);
                return 0;
            }
        }

        public async Task<bool> HashSetAsync(string key, string field, string value)
        {
            try
            {
                return await _database.HashSetAsync(key, field, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting Redis hash {Key}.{Field}", key, field);
                return false;
            }
        }

        public async Task<string?> HashGetAsync(string key, string field)
        {
            try
            {
                var value = await _database.HashGetAsync(key, field);
                return value.HasValue ? value.ToString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Redis hash {Key}.{Field}", key, field);
                return null;
            }
        }

        public async Task<bool> HashDeleteAsync(string key, string field)
        {
            try
            {
                return await _database.HashDeleteAsync(key, field);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Redis hash field {Key}.{Field}", key, field);
                return false;
            }
        }

        public async Task<Dictionary<string, string>> HashGetAllAsync(string key)
        {
            try
            {
                var hash = await _database.HashGetAllAsync(key);
                return hash.ToDictionary(
                    item => item.Name.ToString(),
                    item => item.Value.ToString()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all Redis hash fields {Key}", key);
                return new Dictionary<string, string>();
            }
        }

        public async Task<long> StreamAddAsync(string stream, string field, string value)
        {
            try
            {
                var id = await _database.StreamAddAsync(stream, field, value);
                return 1; // Success indicator
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding to Redis stream {Stream}", stream);
                return 0;
            }
        }

        public async Task<StreamEntry[]> StreamReadAsync(string stream, string position, int count = 10)
        {
            try
            {
                return await _database.StreamReadAsync(stream, position, count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Redis stream {Stream}", stream);
                return Array.Empty<StreamEntry>();
            }
        }

        public async Task PublishAsync(string channel, string message)
        {
            try
            {
                await _subscriber.PublishAsync(channel, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing to Redis channel {Channel}", channel);
            }
        }

        public async Task SubscribeAsync(string channel, Action<string, string> handler)
        {
            try
            {
                await _subscriber.SubscribeAsync(channel, (ch, msg) => handler(ch!, msg!));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to Redis channel {Channel}", channel);
            }
        }
    }
}