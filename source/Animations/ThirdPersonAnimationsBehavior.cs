using CombatOverhaul.Integration;
using CombatOverhaul.Integration.Transpilers;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.Animations;

public sealed class ThirdPersonAnimationsBehavior : EntityBehavior, IDisposable
{
    public ThirdPersonAnimationsBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player) throw new ArgumentException("Only for players");
        _player = player;
        _api = player.Api as ICoreClientAPI;
        _animationsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().PlayerAnimationsManager;
        _animationSystem = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ClientTpAnimationSystem ?? throw new Exception();
        _settings = player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;

        _composer = new(null, null, player);

        AnimationPatches.OnBeforeFrame += OnBeforeFrame;
        AnimationPatches.AnimationBehaviors[player.EntityId] = this;
        AnimationPatches.ActiveEntities.Add(player.EntityId);
        player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().OnDispose += Dispose;

        _mainPlayer = (entity as EntityPlayer)?.PlayerUID == _api?.Settings.String["playeruid"];

        if (player.Api.Side == EnumAppSide.Client)
        {
            if (_existingBehaviors.TryGetValue(_player.PlayerUID, out ThirdPersonAnimationsBehavior? previousBehavior))
            {
                previousBehavior.PartialDispose();
            }

            _existingBehaviors[_player.PlayerUID] = this;
        }
    }

    public override string PropertyName() => "ThirdPersonAnimations";

    public override void OnGameTick(float deltaTime)
    {
        if (!_player.IsRendered || _player.RightHandItemSlot == null || _player.LeftHandItemSlot == null) return;



        int mainHandItemId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        int offhandItemId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

        if (!_mainPlayer && (_mainHandItemId != mainHandItemId || _offHandItemId != offhandItemId))
        {
            if (!ItemHasIdleAnimations(mainHand: true) && !ItemHasIdleAnimations(mainHand: false))
            {
                _composer.StopAll();
            }
        }

        if (_mainHandItemId != mainHandItemId)
        {
            _mainHandItemId = mainHandItemId;
            MainHandItemChanged();
        }

        if (_offHandItemId != offhandItemId)
        {
            _offHandItemId = offhandItemId;
            OffhandItemChanged();
        }

        if (_api != null && _ownerEntityId == 0)
        {
            _ownerEntityId = _api.World?.Player?.Entity?.EntityId ?? 0;
        }
    }
    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        switch (despawn.Reason)
        {
            case EnumDespawnReason.Death:
                break;
            case EnumDespawnReason.Combusted:
                break;
            case EnumDespawnReason.OutOfRange:
                break;
            case EnumDespawnReason.PickedUp:
                break;
            case EnumDespawnReason.Unload:
                break;
            case EnumDespawnReason.Disconnect:
                PartialDispose();
                break;
            case EnumDespawnReason.Expire:
                break;
            case EnumDespawnReason.Removed:
                break;
        }
    }

    public PlayerItemFrame? FrameOverride { get; set; } = null;


    public void Play(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        if (_animationsManager == null) return;

        if (!_animationsManager.GetAnimation(out Animation? animation, requestByCode.Animation, _player, firstPerson: false))
        {
            LoggerUtil.Verbose(_api, this, $"Animation '{requestByCode.Animation}' was not found");
            Debug.WriteLine($"Animation '{requestByCode.Animation}' was not found");
            return;
        }

        AnimationRequest request = new(animation, requestByCode);

        PlayRequest(request, mainHand);
        StopIdleTimer(mainHand);

        if (_mainPlayer) _animationSystem.SendPlayPacket(requestByCode, mainHand, entity.EntityId, GetCurrentItemId(mainHand));
    }
    public void Play(bool mainHand, string animation, string category = "main", float animationSpeed = 1, float weight = 1, bool easeOut = true)
    {
        AnimationRequestByCode request = new(animation, animationSpeed, weight, category, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1), easeOut, null, null);
        Play(request, mainHand);
    }
    public void PlayReadyAnimation(bool mainHand = true)
    {
        if (mainHand)
        {
            IHasIdleAnimations? staticAnimation = _player.RightHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasIdleAnimations>();
            IHasDynamicIdleAnimations? dynamicAnimation = _player.RightHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasDynamicIdleAnimations>();

            if (staticAnimation != null)
            {
                Play(staticAnimation.ReadyAnimation, mainHand);
                StartIdleTimer(staticAnimation.IdleAnimation, mainHand);
            }
            if (dynamicAnimation != null)
            {
                AnimationRequestByCode? readyAnimation = dynamicAnimation.GetReadyAnimation(_player, _player.RightHandItemSlot, mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = dynamicAnimation.GetIdleAnimation(_player, _player.RightHandItemSlot, mainHand: mainHand);

                if (readyAnimation != null && idleAnimation != null)
                {
                    Play(readyAnimation.Value, mainHand);
                    StartIdleTimer(idleAnimation.Value, mainHand);
                }
            }
        }
        else
        {
            IHasIdleAnimations? staticAnimation = _player.LeftHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasIdleAnimations>();
            IHasDynamicIdleAnimations? dynamicAnimation = _player.LeftHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasDynamicIdleAnimations>();

            if (staticAnimation != null)
            {
                Play(staticAnimation.ReadyAnimation, mainHand);
                StartIdleTimer(staticAnimation.IdleAnimation, mainHand);
            }
            if (dynamicAnimation != null)
            {
                AnimationRequestByCode? readyAnimation = dynamicAnimation.GetReadyAnimation(_player, _player.LeftHandItemSlot, mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = dynamicAnimation.GetIdleAnimation(_player, _player.LeftHandItemSlot, mainHand: mainHand);

                if (readyAnimation != null && idleAnimation != null)
                {
                    Play(readyAnimation.Value, mainHand);
                    StartIdleTimer(idleAnimation.Value, mainHand);
                }
            }
        }
    }
    public void Stop(string category)
    {
        _composer.Stop(category);
        if (_mainPlayer) _animationSystem.SendStopPacket(category, entity.EntityId);
    }

    public void OnFrame(Entity targetEntity, ElementPose pose, AnimatorBase animator)
    {
        _frameApplied = true;

        if (!targetEntity.IsRendered || DebugWindowManager.PlayAnimationsInThirdPerson || IsFirstPerson(targetEntity)) return;

        if (FrameOverride != null)
        {
            ApplyFrame(FrameOverride.Value, pose, animator);
        }
        else
        {
            ApplyFrame(_lastFrame, pose, animator);
        }
    }

    private readonly Composer _composer;
    private readonly EntityPlayer _player;
    private readonly AnimationsManager? _animationsManager;
    private readonly AnimationSystemClient _animationSystem;

    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private readonly List<string> _offhandCategories = new();
    private readonly List<string> _mainHandCategories = new();
    private readonly bool _mainPlayer = false;
    private readonly Settings _settings;
    private bool _frameApplied = false;
    private int _offHandItemId = 0;
    private int _mainHandItemId = 0;
    private long _mainHandIdleTimer = -1;
    private long _offHandIdleTimer = -1;
    private readonly ICoreClientAPI? _api;
    private bool _disposed = false;
    private Animatable? _animatable = null;
    private float _pitch = 0;
    private Vector3 _eyePosition = new();
    private float _eyeHeight = 0;
    private long _ownerEntityId = 0;

    private static readonly TimeSpan _readyTimeout = TimeSpan.FromSeconds(3);
    private static Dictionary<string, ThirdPersonAnimationsBehavior> _existingBehaviors = new();

    private void OnBeforeFrame(Entity targetEntity, float dt)
    {
        if (_settings.DisableThirdPersonAnimations) return;

        if (entity.EntityId != targetEntity.EntityId || !targetEntity.IsRendered) return;

        if (!_frameApplied) return;



        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt));



        if (_composer.AnyActiveAnimations())
        {
            _animatable = (entity as EntityAgent)?.RightHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
            _pitch = targetEntity.Pos.HeadPitch;
            _eyePosition = new((float)entity.LocalEyePos.X, (float)entity.LocalEyePos.Y, (float)entity.LocalEyePos.Z);
            _eyeHeight = (float)entity.Properties.EyeHeight;
        }

        _frameApplied = false;


    }
    private void ApplyFrame(PlayerItemFrame frame, ElementPose pose, AnimatorBase animator)
    {
        EnumAnimatedElement element = EnumAnimatedElement.Unknown;

        ExtendedElementPose? extendedPoseValue = null;
        if (pose is ExtendedElementPose extendedPose)
        {
            element = extendedPose.ElementNameEnum;
            extendedPoseValue = extendedPose;
        }
        else
        {
            return;
        }

        if (element == EnumAnimatedElement.Unknown && animator is not ClientItemAnimator)
        {
            return;
        }

        if (_animatable != null && frame.DetachedAnchor)
        {
            _animatable.DetachedAnchor = true;
        }

        if (_animatable != null && frame.SwitchArms)
        {
            _animatable.SwitchArms = true;
        }

        if (element == EnumAnimatedElement.LowerTorso) return;

        if (extendedPoseValue != null)
        {
            frame.Apply(extendedPoseValue, element, _eyePosition, _eyeHeight, _pitch, _composer.AnyActiveAnimations());
        }
        else
        {
            frame.Apply(pose, element, _eyePosition, _eyeHeight, _pitch, _composer.AnyActiveAnimations());
        }
    }
    private bool IsFirstPerson(Entity entity)
    {
        bool owner = _ownerEntityId == entity.EntityId;
        if (!owner) return false;

        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };

        return firstPerson;
    }
    private void PlayRequest(AnimationRequest request, bool mainHand = true)
    {
        _composer.Play(request);
        if (mainHand)
        {
            _mainHandCategories.Add(request.Category);
        }
        else
        {
            _offhandCategories.Add(request.Category);
        }
    }

    private void MainHandItemChanged()
    {
        if (!_mainPlayer)
        {
            foreach (string category in _mainHandCategories)
            {
                Stop(category);
            }
            _mainHandCategories.Clear();
            return;
        }

        StopIdleTimer(mainHand: true);

        IHasIdleAnimations? staticAnimation = _player.RightHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasIdleAnimations>();
        IHasDynamicIdleAnimations? dynamicAnimation = _player.RightHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasDynamicIdleAnimations>();

        if (staticAnimation != null)
        {
            string readyCategory = staticAnimation.ReadyAnimation.Category;

            foreach (string category in _mainHandCategories.Where(element => element != readyCategory))
            {
                Stop(category);
            }
            _mainHandCategories.Clear();

            Play(staticAnimation.ReadyAnimation, true);
            StartIdleTimer(staticAnimation.IdleAnimation, true);
        }
        else if (dynamicAnimation != null)
        {
            AnimationRequestByCode? readyAnimation = dynamicAnimation.GetReadyAnimation(_player, _player.RightHandItemSlot, mainHand: true);
            AnimationRequestByCode? idleAnimation = dynamicAnimation.GetIdleAnimation(_player, _player.RightHandItemSlot, mainHand: true);

            if (readyAnimation == null || idleAnimation == null)
            {
                foreach (string category in _mainHandCategories)
                {
                    Stop(category);
                }
                _mainHandCategories.Clear();
            }
            else
            {
                string readyCategory = readyAnimation.Value.Category;

                foreach (string category in _mainHandCategories.Where(element => element != readyCategory))
                {
                    Stop(category);
                }
                _mainHandCategories.Clear();

                Play(readyAnimation.Value, true);
                StartIdleTimer(idleAnimation.Value, true);
            }
        }
        else
        {
            foreach (string category in _mainHandCategories)
            {
                Stop(category);
            }
            _mainHandCategories.Clear();
        }
    }
    private void OffhandItemChanged()
    {
        if (!_mainPlayer)
        {
            foreach (string category in _offhandCategories)
            {
                Stop(category);
            }
            _offhandCategories.Clear();
            return;
        }

        StopIdleTimer(false);

        IHasIdleAnimations? staticAnimation = _player.LeftHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasIdleAnimations>();
        IHasDynamicIdleAnimations? dynamicAnimation = _player.LeftHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasDynamicIdleAnimations>();

        if (staticAnimation != null)
        {
            string readyCategory = staticAnimation.ReadyAnimation.Category;

            foreach (string category in _offhandCategories.Where(element => element != readyCategory))
            {
                Stop(category);
            }
            _offhandCategories.Clear();

            Play(staticAnimation.ReadyAnimation, false);
            StartIdleTimer(staticAnimation.IdleAnimation, false);
        }
        else if (dynamicAnimation != null)
        {
            AnimationRequestByCode? readyAnimation = dynamicAnimation.GetReadyAnimation(_player, _player.LeftHandItemSlot, mainHand: false);
            AnimationRequestByCode? idleAnimation = dynamicAnimation.GetIdleAnimation(_player, _player.LeftHandItemSlot, mainHand: false);

            if (readyAnimation == null || idleAnimation == null)
            {
                foreach (string category in _offhandCategories)
                {
                    Stop(category);
                }
                _offhandCategories.Clear();
            }
            else
            {
                string readyCategory = readyAnimation.Value.Category;

                foreach (string category in _offhandCategories.Where(element => element != readyCategory))
                {
                    Stop(category);
                }
                _offhandCategories.Clear();

                Play(readyAnimation.Value, false);
                StartIdleTimer(idleAnimation.Value, false);
            }
        }
        else
        {
            foreach (string category in _offhandCategories)
            {
                Stop(category);
            }
            _offhandCategories.Clear();
        }
    }

    private void StartIdleTimer(AnimationRequestByCode request, bool mainHand)
    {
        if (_api?.IsGamePaused == true || !_mainPlayer) return;

        long timer = _api?.World.RegisterCallback(_ => PlayIdleAnimation(request, mainHand), (int)_readyTimeout.TotalMilliseconds) ?? -1;
        if (mainHand)
        {
            _mainHandIdleTimer = timer;
        }
        else
        {
            _offHandIdleTimer = timer;
        }
    }
    private void StopIdleTimer(bool mainHand)
    {
        if (mainHand)
        {
            if (_mainHandIdleTimer != -1)
            {
                _api?.World.UnregisterCallback(_mainHandIdleTimer);
                _mainHandIdleTimer = -1;
            }
        }
        else
        {
            if (_offHandIdleTimer != -1)
            {
                _api?.World.UnregisterCallback(_offHandIdleTimer);
                _offHandIdleTimer = -1;
            }
        }
    }
    private void PlayIdleAnimation(AnimationRequestByCode request, bool mainHand)
    {
        if (mainHand && _mainHandIdleTimer == -1) return;
        if (!mainHand && _offHandIdleTimer == -1) return;

        if (mainHand)
        {
            _mainHandIdleTimer = -1;
        }
        else
        {
            _offHandIdleTimer = -1;
        }

        Play(request, mainHand);
    }

    private bool ItemHasIdleAnimations(bool mainHand)
    {
        ItemSlot? slot = mainHand ? _player.RightHandItemSlot : _player.LeftHandItemSlot;
        if (slot == null) return false;

        IHasIdleAnimations? staticAnimation = slot.Itemstack?.Collectible?.GetCollectibleInterface<IHasIdleAnimations>();
        IHasDynamicIdleAnimations? dynamicAnimation = slot.Itemstack?.Collectible?.GetCollectibleInterface<IHasDynamicIdleAnimations>();

        return staticAnimation != null || dynamicAnimation != null;
    }

    private int GetCurrentItemId(bool mainHand) => mainHand ? _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0 : _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

    private void PartialDispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _offhandCategories.Clear();
            _mainHandCategories.Clear();
            AnimationPatches.OnBeforeFrame -= OnBeforeFrame;
            if (AnimationPatches.AnimationBehaviors[_player.EntityId] == this)
            {
                AnimationPatches.AnimationBehaviors.Remove(_player.EntityId);
                AnimationPatches.ActiveEntities.Remove(_player.EntityId);
            }
            _existingBehaviors.Remove(_player.PlayerUID);
        }
    }
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _offhandCategories.Clear();
            _mainHandCategories.Clear();
            AnimationPatches.OnBeforeFrame -= OnBeforeFrame;
            if (AnimationPatches.AnimationBehaviors[_player.EntityId] == this)
            {
                AnimationPatches.AnimationBehaviors.Remove(_player.EntityId);
                AnimationPatches.ActiveEntities.Remove(_player.EntityId);
            }
        }
        _existingBehaviors.Clear();
    }
}