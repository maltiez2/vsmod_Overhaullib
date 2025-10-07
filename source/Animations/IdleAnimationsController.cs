using System.Diagnostics;
using Vintagestory.API.Common;

namespace CombatOverhaul.Animations;

public sealed class IdleAnimationsController
{
    public IdleAnimationsController(EntityPlayer player, Action<AnimationRequestByCode> playAnimationCallback, Action stopAnimationCallback, Func<ItemSlot> slotGetter, bool mainHand)
    {
        _player = player;
        _playAnimationCallback = playAnimationCallback;
        _stopAnimationCallback = stopAnimationCallback;
        _slotGetter = slotGetter;
        _mainHand = mainHand;
    }

    public void Start()
    {
        _currentItemId = GetItemId();
        _currentAnimation = EnumAnimationType.New;
        PlayNextAnimation();
    }

    public void Pause()
    {
        if (_currentAnimation == EnumAnimationType.None) return;

        _currentItemId = 0;
        _currentAnimation = EnumAnimationType.None;
    }

    public void Stop()
    {
        if (_currentAnimation == EnumAnimationType.None) return;
        
        _currentItemId = 0;
        _currentAnimation = EnumAnimationType.None;
        _stopAnimationCallback?.Invoke();
    }

    public void Update()
    {
        if (!NeedsUpdate()) return;

        PlayNextAnimation();
    }



    private enum EnumAnimationType
    {
        None = -1,
        New,
        Idle,
        Ready,
        Walk,
        Run,
        SwimIdle,
        Swim
    }
    private enum EnumPlayerState
    {
        Idle,
        Walk,
        Run,
        SwimIdle,
        Swim
    }

    private static readonly EnumAnimationType[] _repeatedAnimations = [
        EnumAnimationType.Walk,
        EnumAnimationType.Run,
        EnumAnimationType.Swim,
        EnumAnimationType.SwimIdle,
        EnumAnimationType.Ready
    ];

    private static readonly (EnumAnimationType from, EnumAnimationType to)[] _consecutiveAnimations = [
        (EnumAnimationType.Ready, EnumAnimationType.Idle)
    ];

    private readonly EntityPlayer _player;
    private readonly Action<AnimationRequestByCode> _playAnimationCallback;
    private readonly Action _stopAnimationCallback;
    private readonly Func<ItemSlot> _slotGetter;
    private readonly bool _mainHand;

    private EnumAnimationType _currentAnimation = EnumAnimationType.None;
    private int _currentItemId = 0;

    private (AnimationRequestByCode? request, EnumAnimationType animationType) GetNextAnimation()
    {
        ItemSlot slot = _slotGetter.Invoke();
        if (!HasAnyAnimations(slot)) return (null, EnumAnimationType.None);

        EnumAnimationType nextAnimationType = GetNextExistingAnimationType(_player, slot, _mainHand, _currentAnimation);
        AnimationRequestByCode? animationRequest = GetAnimation(_player, slot, _mainHand, nextAnimationType);

        if (animationRequest == null) return (null, nextAnimationType);

        float animationSpeed = GetAnimationSpeed(_player, nextAnimationType);

        AnimationRequestByCode nextAnimationRequest = new(animationRequest.Value, animationSpeed, AnimationCallback);

        return (nextAnimationRequest, nextAnimationType);
    }

    private bool AnimationCallback()
    {
        if (_currentItemId != GetItemId())
        {
            _currentItemId = GetItemId();
            Stop();
            return true;
        }

        if (!_repeatedAnimations.Contains(_currentAnimation))
        {
            return true;
        }

        PlayNextAnimation();

        return true;
    }

    private void PlayNextAnimation()
    {
        (AnimationRequestByCode? request, EnumAnimationType animationType) = GetNextAnimation();
        if (request != null)
        {
            _playAnimationCallback.Invoke(request.Value);
            _currentAnimation = animationType;
        }
        else
        {
            _stopAnimationCallback?.Invoke();
            _currentAnimation = EnumAnimationType.None;
        }
    }

    private bool NeedsUpdate()
    {
        ItemSlot slot = _slotGetter.Invoke();
        if (!HasAnyAnimations(slot)) return false;

        EnumAnimationType nextAnimationType = GetNextExistingAnimationType(_player, slot, _mainHand, _currentAnimation);

        if (_consecutiveAnimations.Contains((_currentAnimation, nextAnimationType))) return false;

        if (nextAnimationType == _currentAnimation) return false;

        AnimationRequestByCode? animationRequest = GetAnimation(_player, slot, _mainHand, nextAnimationType);

        if (animationRequest == null) return false;

        return true;
    }

    private int GetItemId() => _slotGetter.Invoke().Itemstack?.Item?.Id ?? 0;

    private static float GetAnimationSpeed(EntityPlayer player, EnumAnimationType animationType)
    {
        return animationType switch
        {
            EnumAnimationType.Walk => GetWalkAnimationSpeed(player),
            EnumAnimationType.Run => GetWalkAnimationSpeed(player),
            EnumAnimationType.Swim => GetWalkAnimationSpeed(player),
            _ => 1
        };
    }
    private static float GetWalkAnimationSpeed(EntityPlayer player)
    {
        const double _stepPeriod = 0.95f;
        const double _defaultFrequency = 1.2f;

        EntityControls controls = player.Controls;
        double frequency = controls.MovespeedMultiplier * player.GetWalkSpeedMultiplier(0.3) * (controls.Sprint ? 0.9 : 1.2) * (controls.Sneak ? 1.2f : 1);

        return (float)(frequency / _defaultFrequency / _stepPeriod);
    }

    private static bool HasAnyAnimations(ItemSlot slot)
    {
        return !slot.Empty && (
            slot.Itemstack?.Collectible?.GetCollectibleInterface<IHasIdleAnimations>() != null ||
            slot.Itemstack?.Collectible?.GetCollectibleInterface<IHasMoveAnimations>() != null ||
            slot.Itemstack?.Collectible?.GetCollectibleInterface<IHasDynamicIdleAnimations>() != null ||
            slot.Itemstack?.Collectible?.GetCollectibleInterface<IHasDynamicMoveAnimations>() != null
            );
    }
    private static bool HasAnimation(EntityPlayer player, ItemSlot slot, bool mainHand, EnumAnimationType animationType)
    {
        IHasIdleAnimations? idleAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasIdleAnimations>();
        IHasMoveAnimations? moveAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasMoveAnimations>();
        IHasDynamicIdleAnimations? idleDynamicAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasDynamicIdleAnimations>();
        IHasDynamicMoveAnimations? moveDynamicAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasDynamicMoveAnimations>();

        return animationType switch
        {
            EnumAnimationType.Idle => idleAnimationsProvider?.IdleAnimation ?? idleDynamicAnimationsProvider?.GetIdleAnimation(player, slot, mainHand),
            EnumAnimationType.Ready => idleAnimationsProvider?.ReadyAnimation ?? idleDynamicAnimationsProvider?.GetReadyAnimation(player, slot, mainHand),
            EnumAnimationType.Walk => moveAnimationsProvider?.WalkAnimation ?? moveDynamicAnimationsProvider?.GetWalkAnimation(player, slot, mainHand),
            EnumAnimationType.Run => moveAnimationsProvider?.RunAnimation ?? moveDynamicAnimationsProvider?.GetRunAnimation(player, slot, mainHand),
            EnumAnimationType.SwimIdle => moveAnimationsProvider?.SwimIdleAnimation ?? moveDynamicAnimationsProvider?.GetSwimIdleAnimation(player, slot, mainHand),
            EnumAnimationType.Swim => moveAnimationsProvider?.SwimAnimation ?? moveDynamicAnimationsProvider?.GetSwimAnimation(player, slot, mainHand),
            _ => null,
        } != null;
    }
    private static AnimationRequestByCode? GetAnimation(EntityPlayer player, ItemSlot slot, bool mainHand, EnumAnimationType animationType)
    {
        IHasIdleAnimations? idleAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasIdleAnimations>();
        IHasMoveAnimations? moveAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasMoveAnimations>();
        IHasDynamicIdleAnimations? idleDynamicAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasDynamicIdleAnimations>();
        IHasDynamicMoveAnimations? moveDynamicAnimationsProvider = slot.Itemstack.Collectible.GetCollectibleInterface<IHasDynamicMoveAnimations>();

        return animationType switch
        {
            EnumAnimationType.Idle => idleAnimationsProvider?.IdleAnimation ?? idleDynamicAnimationsProvider?.GetIdleAnimation(player, slot, mainHand),
            EnumAnimationType.Ready => idleAnimationsProvider?.ReadyAnimation ?? idleDynamicAnimationsProvider?.GetReadyAnimation(player, slot, mainHand),
            EnumAnimationType.Walk => moveAnimationsProvider?.WalkAnimation ?? moveDynamicAnimationsProvider?.GetWalkAnimation(player, slot, mainHand),
            EnumAnimationType.Run => moveAnimationsProvider?.RunAnimation ?? moveDynamicAnimationsProvider?.GetRunAnimation(player, slot, mainHand),
            EnumAnimationType.SwimIdle => moveAnimationsProvider?.SwimIdleAnimation ?? moveDynamicAnimationsProvider?.GetSwimIdleAnimation(player, slot, mainHand),
            EnumAnimationType.Swim => moveAnimationsProvider?.SwimAnimation ?? moveDynamicAnimationsProvider?.GetSwimAnimation(player, slot, mainHand),
            _ => null,
        };
    }
    private static EnumAnimationType GetNextExistingAnimationType(EntityPlayer player, ItemSlot slot, bool mainHand, EnumAnimationType animationType)
    {
        EnumAnimationType result = GetNextAnimationType(player, animationType);
        while (!HasAnimation(player, slot, mainHand, result) && result != EnumAnimationType.None)
        {
            result = NextAnimationTypeIfNotExists(result);
        }
        return result;
    }
    private static EnumAnimationType NextAnimationTypeIfNotExists(EnumAnimationType animationType)
    {
        return animationType switch
        {
            EnumAnimationType.None => EnumAnimationType.None,
            EnumAnimationType.New => EnumAnimationType.Ready,
            EnumAnimationType.Idle => EnumAnimationType.None,
            EnumAnimationType.Ready => EnumAnimationType.Idle,
            EnumAnimationType.Walk => EnumAnimationType.Idle,
            EnumAnimationType.Run => EnumAnimationType.Walk,
            EnumAnimationType.Swim => EnumAnimationType.Idle,
            EnumAnimationType.SwimIdle => EnumAnimationType.Swim,
            _ => EnumAnimationType.None
        };
    }
    private static EnumAnimationType GetNextAnimationType(EntityPlayer player, EnumAnimationType currentAnimation)
    {
        EnumPlayerState playerState = GetPlayerState(player);

        return (currentAnimation, playerState) switch
        {
            (EnumAnimationType.None, _) => EnumAnimationType.None,
            (EnumAnimationType.New, _) => EnumAnimationType.Ready,
            (EnumAnimationType.Ready, EnumPlayerState.Idle) => EnumAnimationType.Idle,
            (_, EnumPlayerState.Idle) => EnumAnimationType.Idle,
            (_, EnumPlayerState.Walk) => EnumAnimationType.Walk,
            (_, EnumPlayerState.Run) => EnumAnimationType.Run,
            (_, EnumPlayerState.Swim) => EnumAnimationType.Swim,
            (_, EnumPlayerState.SwimIdle) => EnumAnimationType.SwimIdle,
            _ => EnumAnimationType.None
        };
    }
    private static EnumPlayerState GetPlayerState(EntityPlayer player)
    {
        bool triesToMove = player.Controls.Forward || player.Controls.Right || player.Controls.Left;
        bool triesToRun = player.Controls.Sprint && triesToMove;
        bool swimming = player.Swimming;

        return (triesToMove, triesToRun, swimming) switch
        {
            (false, false, false) => EnumPlayerState.Idle,
            (true, false, false) => EnumPlayerState.Walk,
            (true, true, false) => EnumPlayerState.Run,
            (true, _, true) => EnumPlayerState.Swim,
            (false, _, true) => EnumPlayerState.SwimIdle,
            _ => EnumPlayerState.Idle,
        };
    }
}