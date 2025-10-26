using CombatOverhaul.Utils;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration;

internal sealed class AnimatorCache : IDisposable
{
    private readonly Dictionary<ClientAnimator, EntityPlayer> _animators = [];
    private readonly Dictionary<ClientAnimator, long> _lastAccess = [];
    private readonly ReaderWriterLock _animatorsLock = new();
    private ICoreAPI? _api;
    private const int _cleanUpPeriodMs = 10 * 60 * 1000;
    private readonly long _cleanUpTimer = 0;

    public AnimatorCache(ICoreAPI api)
    {
        _api = api;
        _cleanUpTimer = api.World.RegisterGameTickListener(_ => Clean(), _cleanUpPeriodMs, _cleanUpPeriodMs);
    }

    public void Add(ClientAnimator animator, EntityPlayer entity)
    {
        _animatorsLock.AcquireWriterLock(5000);
        _animators[animator] = entity;
        _lastAccess[animator] = CurrentTime();
        _animatorsLock.ReleaseWriterLock();
    }

    public bool Get(ClientAnimator animator, [NotNullWhen(true)] out EntityPlayer? entity)
    {
        _animatorsLock.AcquireWriterLock(5000);

        bool success = _animators.TryGetValue(animator, out entity);
        if (success)
        {
            _lastAccess[animator] = CurrentTime();
        }

        _animatorsLock.ReleaseWriterLock();

        return success;
    }

    public void Clean()
    {
        long currentTime = CurrentTime();

        _animatorsLock.AcquireWriterLock(5000);

        try
        {
            LoggerUtil.Verbose(_api, this, $"Starting clean up. Current world time: {TimeSpan.FromMilliseconds(currentTime)}");
            
            HashSet<ClientAnimator> animatorsToRemove = [];
            HashSet<EntityPlayer> entities = [];
            foreach ((ClientAnimator animator, long lastAccess) in _lastAccess)
            {
                if (currentTime - lastAccess > _cleanUpPeriodMs)
                {
                    animatorsToRemove.Add(animator);
                    entities.Add(_animators[animator]);
                }
            }

            foreach (ClientAnimator animator in animatorsToRemove)
            {
                _animators.Remove(animator);
                _lastAccess.Remove(animator);
            }

            LoggerUtil.Verbose(_api, this, $"Cleaned up '{animatorsToRemove.Count}' animators for '{entities.Count}' player entities.");
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(_api, this, $"Error on animators cache cleanup:\n{exception}");
        }

        _animatorsLock.ReleaseWriterLock();
    }

    public void Clear()
    {
        _animatorsLock.AcquireWriterLock(5000);
        _animators.Clear();
        _lastAccess.Clear();
        _animatorsLock.ReleaseWriterLock();
    }

    public void Dispose()
    {
        _animatorsLock.AcquireWriterLock(5000);
        _animators.Clear();
        _lastAccess.Clear();
        _api?.World.UnregisterGameTickListener(_cleanUpTimer);
        _api = null;
        _animatorsLock.ReleaseWriterLock();
    }

    private long CurrentTime() => _api?.World.ElapsedMilliseconds ?? 0;
}