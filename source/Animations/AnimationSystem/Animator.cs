using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace CombatOverhaul.Animations;

public class Animator
{
    public Animator(Animation animation, SoundsSynchronizerClient? soundsManager, ParticleEffectsManager? particleEffectsManager, EntityPlayer player, float animationSpeed)
    {
        _currentAnimation = animation;
        _soundsManager = soundsManager;
        _animationSpeed = animationSpeed;
        _player = player;
        _particleEffectsManager = particleEffectsManager;
    }

    public bool FinishOverride { get; set; } = false;

    public void Play(Animation animation, TimeSpan duration) => Play(animation, (float)(animation.TotalDuration / duration));
    public void Play(Animation animation, float animationSpeed)
    {
        _currentAnimation = animation;
        _animationSpeed = animationSpeed;
        _currentDuration = TimeSpan.Zero;
        _previousAnimationFrame = _lastFrame;
        _unfiredCallbacks.Clear();
        _unfiredCallbacks.AddRange(animation.CallbackFrames.OrderBy(frame => frame.DurationFraction).Select(frame => frame.Code));
    }

    public PlayerItemFrame Animate(TimeSpan delta, out IEnumerable<string> callbacks)
    {
        TimeSpan previousDuration = _currentDuration * _animationSpeed;
        _currentDuration += delta;
        TimeSpan adjustedDuration = _currentDuration * _animationSpeed;

        if (_soundsManager != null) _currentAnimation.PlaySounds(_soundsManager, previousDuration, adjustedDuration);
        if (_particleEffectsManager != null) _currentAnimation.SpawnParticles(_player, _particleEffectsManager, previousDuration, adjustedDuration);

        callbacks = _currentAnimation.GetCallbacks(previousDuration, adjustedDuration);

        callbacks.Foreach(callback => _unfiredCallbacks.Remove(callback));

        _lastFrame = _currentAnimation.Interpolate(_previousAnimationFrame, adjustedDuration);
        return _lastFrame;
    }
    public bool Stopped() => _currentAnimation.TotalDuration <= _currentDuration * _animationSpeed;
    public bool Finished() => FinishOverride || (Stopped() && !_currentAnimation.Hold);
    public IEnumerable<string> GetUnfiredCallbacks() => _unfiredCallbacks;
    public void ClearUnfiredCallbacks() => _unfiredCallbacks.Clear();

    private PlayerItemFrame _previousAnimationFrame = PlayerItemFrame.Zero;
    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private TimeSpan _currentDuration = TimeSpan.Zero;
    private float _animationSpeed;
    private Animation _currentAnimation;
    private readonly SoundsSynchronizerClient? _soundsManager;
    private readonly ParticleEffectsManager? _particleEffectsManager;
    private readonly EntityPlayer _player;
    private readonly List<string> _unfiredCallbacks = [];
}
