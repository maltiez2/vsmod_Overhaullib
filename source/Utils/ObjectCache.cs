using CombatOverhaul.Utils;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration;

public sealed class ObjectCache<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _mapping = [];
    private readonly Dictionary<TKey, long> _lastAccess = [];
    private readonly ReaderWriterLock _lock = new();
    private ICoreAPI? _api;
    private readonly int _cleanUpPeriodMs;
    private readonly long _cleanUpTimer = 0;
    private readonly int _cleanUpThreshold;
    private readonly string _loggedCacheName;
    private readonly bool _threadSafe;
    private long _addCountBetweenCleanUps = 0;
    private long _getCountBetweenCleanUps = 0;

    public ObjectCache(ICoreAPI api, string loggedCacheName = "cache", int cleanUpThreshold = 100, int cleanUpPeriod = 10 * 60 * 1000, bool threadSafe = true)
    {
        _api = api;
        _cleanUpThreshold = cleanUpThreshold;
        _cleanUpPeriodMs = cleanUpPeriod;
        _loggedCacheName = loggedCacheName;
        _threadSafe = threadSafe;
        _cleanUpTimer = api.World.RegisterGameTickListener(_ => Clean(), _cleanUpPeriodMs, _cleanUpPeriodMs);
    }

    public void Add(TKey key, TValue value)
    {
        bool requiresCleanUp = false;
        bool threadSafe = _threadSafe;

        if (threadSafe) _lock.AcquireWriterLock(5000);
        _addCountBetweenCleanUps++;
        _mapping[key] = value;
        _lastAccess[key] = CurrentTime();
        requiresCleanUp = _mapping.Count > _cleanUpThreshold;
        if (threadSafe) _lock.ReleaseWriterLock();

        if (requiresCleanUp)
        {
            Clean();
        }
    }

    public bool Get(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        bool threadSafe = _threadSafe;
        if (threadSafe) _lock.AcquireWriterLock(5000);

        _getCountBetweenCleanUps++;
        bool success = _mapping.TryGetValue(key, out value);
        if (success)
        {
            _lastAccess[key] = CurrentTime();
        }

        if (threadSafe) _lock.ReleaseWriterLock();

        return success;
    }

    public void Clean()
    {
        long currentTime = CurrentTime();
        bool threadSafe = _threadSafe;

        if (threadSafe) _lock.AcquireWriterLock(5000);

        try
        {
            LoggerUtil.Notify(_api, this, $"({_loggedCacheName}) Starting clean up. Current world time: {TimeSpan.FromMilliseconds(currentTime)}\nSize: {_mapping.Count}\n'Get' count: {_getCountBetweenCleanUps}\n'Add' count: {_addCountBetweenCleanUps}");
            _getCountBetweenCleanUps = 0;
            _addCountBetweenCleanUps = 0;

            HashSet<TKey> keysToRemove = [];
            HashSet<TValue> entities = [];
            foreach ((TKey key, long lastAccess) in _lastAccess)
            {
                if (currentTime - lastAccess > _cleanUpPeriodMs)
                {
                    keysToRemove.Add(key);
                    entities.Add(_mapping[key]);
                }
            }

            foreach (TKey key in keysToRemove)
            {
                _mapping.Remove(key);
                _lastAccess.Remove(key);
            }

            LoggerUtil.Notify(_api, this, $"({_loggedCacheName}) Cleaned up '{keysToRemove.Count}' keys for '{entities.Count}' values.");
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(_api, this, $"({_loggedCacheName}) Error on cache cleanup:\n{exception}");
        }

        if (threadSafe) _lock.ReleaseWriterLock();
    }

    public void Clear()
    {
        if (_threadSafe) _lock.AcquireWriterLock(5000);
        _mapping.Clear();
        _lastAccess.Clear();
        if (_threadSafe) _lock.ReleaseWriterLock();
    }

    public void Dispose()
    {
        if (_threadSafe) _lock.AcquireWriterLock(5000);
        _mapping.Clear();
        _lastAccess.Clear();
        _api?.World.UnregisterGameTickListener(_cleanUpTimer);
        _api = null;
        if (_threadSafe) _lock.ReleaseWriterLock();
    }

    private long CurrentTime() => _api?.World.ElapsedMilliseconds ?? 0;
}