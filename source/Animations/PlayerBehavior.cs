using CombatOverhaul.Integration;
using CombatOverhaul.Integration.Transpilers;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace CombatOverhaul.Animations;

public interface IHasIdleAnimations
{
    AnimationRequestByCode IdleAnimation { get; }
    AnimationRequestByCode ReadyAnimation { get; }
}

public interface IHasDynamicIdleAnimations
{
    AnimationRequestByCode? GetIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetReadyAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
}

public interface IHasMoveAnimations : IHasIdleAnimations
{
    AnimationRequestByCode WalkAnimation { get; }
    AnimationRequestByCode RunAnimation { get; }
    AnimationRequestByCode SwimAnimation { get; }
    AnimationRequestByCode SwimIdleAnimation { get; }
}

public interface IHasDynamicMoveAnimations : IHasDynamicIdleAnimations
{
    AnimationRequestByCode? GetWalkAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetRunAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetSwimAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetSwimIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
}

public sealed class FirstPersonAnimationsBehavior : EntityBehavior, IDisposable
{
    public FirstPersonAnimationsBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player) throw new ArgumentException("Only for players");

        _player = player;
        _api = player.Api as ICoreClientAPI ?? throw new ArgumentException("Only client side");
        _animationsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().PlayerAnimationsManager ?? throw new Exception();
        _vanillaAnimationsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ClientVanillaAnimations ?? throw new Exception();
        _mainPlayer = (entity as EntityPlayer)?.PlayerUID == _api.Settings.String["playeruid"];
        _settings = player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;

        SoundsSynchronizerClient soundsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientSoundsSynchronizer ?? throw new Exception();
        ParticleEffectsManager particleEffectsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ParticleEffectsManager ?? throw new Exception();
        _composer = new(soundsManager, particleEffectsManager, player);

        _MainHandIdleAnimationsController = new(player, request => PlayImpl(request, mainHand: true), () => Stop("main"), () => _player.RightHandItemSlot, mainHand: true);
        _OffHandIdleAnimationsController = new(player, request => PlayImpl(request, mainHand: false), () => Stop("mainOffhand"), () => _player.LeftHandItemSlot, mainHand: false);

        if (!_mainPlayer) return;

        AnimationPatches.OnBeforeFrame += OnBeforeFrame;
        AnimationPatches.FirstPersonAnimationBehavior = this;
        AnimationPatches.OwnerEntityId = player.EntityId;
        player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().OnDispose += Dispose;
    }

    public override string PropertyName() => "CombatOverhaul:FirstPersonAnimations";

    public override void OnGameTick(float deltaTime)
    {
        if (!_mainPlayer || _player.RightHandItemSlot == null || _player.LeftHandItemSlot == null) return;

        int mainHandItemId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        int offhandItemId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

        if (_mainHandItemId != mainHandItemId)
        {
            _mainHandItemId = mainHandItemId;
            InHandItemChanged(mainHand: true);
        }

        if (_offHandItemId != offhandItemId)
        {
            _offHandItemId = offhandItemId;
            InHandItemChanged(mainHand: false);
        }

        _MainHandIdleAnimationsController.Update();
        _OffHandIdleAnimationsController.Update();

        foreach ((AnimationRequest request, bool mainHand, bool skip, int itemId) in _playRequests)
        {
            if (!skip) PlayRequest(request, mainHand);
        }
        _playRequests.Clear();

        _settingsUpdateTimeSec += deltaTime;
        if (_settingsUpdateTimeSec > _settingsUpdatePeriodSec)
        {
            _settingsUpdateTimeSec = 0;
            _settingsFOV = ClientSettings.FieldOfView;
            _settingsHandsFOV = ClientSettings.FieldOfView;
        }

        if (_api != null && _ownerEntityId == 0)
        {
            _ownerEntityId = _api.World?.Player?.Entity?.EntityId ?? 0;
        }
    }

    public void OnFrame(Entity entity, ElementPose pose, AnimatorBase animator)
    {
        _frameApplied = true;

        //if (IsImmersiveFirstPerson(entity)) return;
        if (!DebugWindowManager.PlayAnimationsInThirdPerson && !IsFirstPerson(entity)) return;
        if (!_composer.AnyActiveAnimations() && FrameOverride == null)
        {
            if (_resetFov)
            {
                SetFov(1, false);
                _player.HeadBobbingAmplitude /= _previousHeadBobbingAmplitudeFactor;
                _previousHeadBobbingAmplitudeFactor = 1;
                _resetFov = false;
            }
            return;
        }

        if (FrameOverride != null)
        {
            ApplyFrame(FrameOverride.Value, pose, animator);
        }
        else
        {
            ApplyFrame(_lastFrame, pose, animator);
        }
    }

    public PlayerItemFrame? FrameOverride { get; set; } = null;
    public static float CurrentFov { get; set; } = ClientSettings.FieldOfView;

    public void Play(AnimationRequest request, bool mainHand = true)
    {
        if (request.Category == GetIdleAnimationCategory(mainHand))
        {
            (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Pause();
        }
        _playRequests.Add((request, mainHand, false, CurrentItemId(mainHand)));
    }
    public void Play(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        Animation? animation = GetAnimationFromRequest(requestByCode);

        if (animation == null) return;

        AnimationRequest request = new(animation, requestByCode);

        Play(request, mainHand);

        _immersiveFpModeSetting = ((entity.Api as ICoreClientAPI)?.Settings.Bool["immersiveFpMode"] ?? false); // calling here just to reduce number of calls to it
    }
    public void Play(bool mainHand, string animation, string category = "main", float animationSpeed = 1, float weight = 1, System.Func<bool>? callback = null, Action<string>? callbackHandler = null, bool easeOut = true)
    {
        AnimationRequestByCode request = new(animation, animationSpeed, weight, category, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1), easeOut, callback, callbackHandler);
        Play(request, mainHand);
    }
    public void PlayReadyAnimation(bool mainHand = true)
    {
        (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Start();
    }
    public void Stop(string category)
    {
        _composer.Stop(category);
        for (int index = 0; index < _playRequests.Count; index++)
        {
            if (_playRequests[index].request.Category == category)
            {
                _playRequests[index] = (new(), _playRequests[index].mainHand, true, -1);
            }
        }
    }
    public void PlayVanillaAnimation(string code, bool mainHand)
    {
        if (code == "") return;

        _vanillaAnimationsManager?.StartAnimation(code);
        if (mainHand)
        {
            _mainHandVanillaAnimations.Add(code);
        }
        else
        {
            _offhandVanillaAnimations.Add(code);
        }
    }
    public void StopVanillaAnimation(string code, bool mainHand)
    {
        _vanillaAnimationsManager?.StopAnimation(code);
        if (mainHand)
        {
            _mainHandVanillaAnimations.Remove(code);
        }
        else
        {
            _offhandVanillaAnimations.Remove(code);
        }
    }
    public void StopAllVanillaAnimations(bool mainHand)
    {
        HashSet<string> animations = mainHand ? _mainHandVanillaAnimations : _offhandVanillaAnimations;
        foreach (string code in animations)
        {
            _vanillaAnimationsManager?.StopAnimation(code);
        }
    }
    public void SetSpeedModifier(AnimationSpeedModifierDelegate modifier) => _composer.SetSpeedModifier(modifier);
    public void StopSpeedModifier() => _composer.StopSpeedModifier();
    public bool IsSpeedModifierActive() => _composer.IsSpeedModifierActive();

    private readonly Composer _composer;
    private readonly EntityPlayer _player;
    private readonly AnimationsManager _animationsManager;
    private readonly VanillaAnimationsSystemClient _vanillaAnimationsManager;
    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private readonly List<string> _offhandCategories = new();
    private readonly List<string> _mainHandCategories = new();
    private readonly HashSet<string> _offhandVanillaAnimations = new();
    private readonly HashSet<string> _mainHandVanillaAnimations = new();
    private readonly bool _mainPlayer = false;
    private readonly Settings _settings;
    private readonly IdleAnimationsController _MainHandIdleAnimationsController;
    private readonly IdleAnimationsController _OffHandIdleAnimationsController;
    private bool _frameApplied = false;
    private int _offHandItemId = 0;
    private int _mainHandItemId = 0;
    private bool _resetFov = false;
    private readonly ICoreClientAPI _api;
    private readonly List<(AnimationRequest request, bool mainHand, bool skip, int itemId)> _playRequests = new();
    private float _previousHeadBobbingAmplitudeFactor = 1;
    private int _settingsFOV = ClientSettings.FieldOfView;
    private int _settingsHandsFOV = ClientSettings.FpHandsFoV;
    private const float _settingsUpdatePeriodSec = 3f;
    private float _settingsUpdateTimeSec = 0;
    private Animatable? _animatable = null;
    private Vector3 _eyePosition = new();
    private float _eyeHeight = 0;
    private bool _immersiveFpModeSetting = false;
    private long _ownerEntityId = 0;

    private void OnBeforeFrame(Entity targetEntity, float dt)
    {
        if (!_frameApplied) return;

        if (!IsOwner(targetEntity)) return;

        

        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt));

        

        if (_composer.AnyActiveAnimations())
        {
            if (_lastFrame.Player.FovMultiplier != 1) SetFov(_lastFrame.Player.FovMultiplier, true);
            _resetFov = true;

            _animatable = (targetEntity as EntityAgent)?.RightHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
            _eyePosition = new((float)targetEntity.LocalEyePos.X, (float)targetEntity.LocalEyePos.Y, (float)targetEntity.LocalEyePos.Z);
            _eyeHeight = (float)targetEntity.Properties.EyeHeight;

            if (Math.Abs(_lastFrame.Player.PitchFollow - PlayerFrame.DefaultPitchFollow) >= PlayerFrame.Epsilon)
            {
                if (targetEntity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
                {
                    renderer.HeldItemPitchFollowOverride = _lastFrame.Player.PitchFollow;
                }
            }
            else
            {
                if (targetEntity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
                {
                    renderer.HeldItemPitchFollowOverride = null;
                }
            }
        }

        _frameApplied = false;

        
    }

    private void PlayImpl(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        Animation? animation = GetAnimationFromRequest(requestByCode);

        if (animation == null) return;

        AnimationRequest request = new(animation, requestByCode);

        _playRequests.Add((request, mainHand, false, CurrentItemId(mainHand)));
    }

    private void ApplyFrame(PlayerItemFrame frame, ElementPose pose, AnimatorBase animator)
    {
        EnumAnimatedElement element;

        ExtendedElementPose? extendedPoseValue = null;
        if (pose is ExtendedElementPose extendedPose)
        {
            element = extendedPose.ElementNameEnum;
            extendedPoseValue = extendedPose;
        }
        else
        {
            if (!Enum.TryParse(pose.ForElement.Name, out element)) // Cant cache ElementPose because they are new each frame
            {
                element = EnumAnimatedElement.Unknown;
            }
        }

        if (element == EnumAnimatedElement.Unknown && animator is not ClientItemAnimator)
        {
            return;
        }

        if (element == EnumAnimatedElement.LowerTorso && IsImmersiveFirstPerson(_player))
        {
            return;
        }

        if (IsImmersiveFirstPerson(_player))
        {
            PlayerRenderingPatches.SetOffset(0);
        }
        else
        {
            //PlayerRenderingPatches.ResetOffset();
        }

        if (extendedPoseValue != null)
        {
            frame.Apply(extendedPoseValue, element, _eyePosition, _eyeHeight);
        }
        else
        {
            frame.Apply(pose, element, _eyePosition, _eyeHeight);
        }

        _player.HeadBobbingAmplitude /= _previousHeadBobbingAmplitudeFactor;
        _previousHeadBobbingAmplitudeFactor = frame.Player.BobbingAmplitude;
        _player.HeadBobbingAmplitude *= _previousHeadBobbingAmplitudeFactor;

        if (_animatable != null && frame.DetachedAnchor)
        {
            _animatable.DetachedAnchor = true;
        }

        if (_animatable != null && frame.SwitchArms)
        {
            _animatable.SwitchArms = true;
        }

        _resetFov = true;
    }
    private static bool IsOwner(Entity entity) => (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
    private bool IsFirstPerson(Entity entity)
    {
        bool owner = _ownerEntityId == entity.EntityId;
        if (!owner) return false;

        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };

        return firstPerson;
    }
    private bool IsImmersiveFirstPerson(Entity entity)
    {
        return _immersiveFpModeSetting && IsFirstPerson(entity);
    }
    public static void SetFirstPersonHandsPitch(IClientPlayer player, float value)
    {
        if (player.Entity.Properties.Client.Renderer is not EntityPlayerShapeRenderer renderer) return;

        renderer.HeldItemPitchFollowOverride = 0.8f * value;
    }
    private void SetFov(float multiplier, bool equalizeFov = true)
    {
        ClientMain? client = _api?.World as ClientMain;
        if (client == null) return;

        PlayerCamera? camera = client.MainCamera;
        if (camera == null) return;

        float equalizeMultiplier = MathF.Sqrt(_settingsFOV / (float)_settingsHandsFOV);

        PlayerRenderingPatches.HandsFovMultiplier = multiplier * (equalizeFov ? equalizeMultiplier : 1);
        camera.Fov = _settingsFOV * GameMath.DEG2RAD * multiplier;

        CurrentFov = _settingsFOV * multiplier;
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
    private void StopRequestFromPreviousItem(bool mainHand)
    {
        int currentItem = CurrentItemId(mainHand);
        for (int index = 0; index < _playRequests.Count; index++)
        {
            if (_playRequests[index].itemId != currentItem)
            {
                _playRequests[index] = (new(), _playRequests[index].mainHand, true, -1);
            }
        }
    }

    private string GetIdleAnimationCategory(bool mainHand) => mainHand ? "main" : "mainOffhand";

    private void InHandItemChanged(bool mainHand)
    {
        (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Stop();

        string readyCategory = GetIdleAnimationCategory(mainHand);

        foreach (string category in _mainHandCategories.Where(element => element != readyCategory))
        {
            _composer.Stop(category);
        }
        StopRequestFromPreviousItem(true);
        _mainHandCategories.Clear();

        (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Start();
    }

    private int CurrentItemId(bool mainHand)
    {
        if (mainHand)
        {
            return _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        }
        else
        {
            return _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;
        }
    }

    private Animation? GetAnimationFromRequest(AnimationRequestByCode request)
    {
        if (_animationsManager == null) return null;

        string modelPrefix = "-" + entity.WatchedAttributes.GetString("skinModel", "seraph").Replace(':', '-');

        if (_animationsManager.Animations.TryGetValue(request.Animation + modelPrefix, out Animation? animation))
        {

        }
        else if (!_animationsManager.Animations.TryGetValue(request.Animation, out animation))
        {
            return null;
        }

        return animation;
    }

    public void Dispose()
    {
        AnimationPatches.OnBeforeFrame -= OnBeforeFrame;
        if (AnimationPatches.FirstPersonAnimationBehavior == this)
        {
            AnimationPatches.FirstPersonAnimationBehavior = null;
        }
    }
}

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

        string modelPrefix = "-" + entity.WatchedAttributes.GetString("skinModel", "seraph").Replace(':', '-');

        if (_animationsManager.Animations.TryGetValue(requestByCode.Animation + "-tp" + modelPrefix, out Animation? animation))
        {

        }
        else if (_animationsManager.Animations.TryGetValue(requestByCode.Animation + modelPrefix, out animation))
        {

        }
        else if (_animationsManager.Animations.TryGetValue(requestByCode.Animation + "-tp", out animation))
        {

        }
        else if (!_animationsManager.Animations.TryGetValue(requestByCode.Animation, out animation))
        {
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
            if (_player.RightHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
            {
                Play(item.ReadyAnimation, mainHand);
                StartIdleTimer(item.IdleAnimation, mainHand);
            }
            if (_player.RightHandItemSlot.Itemstack?.Item is IHasDynamicIdleAnimations item2)
            {
                AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(_player, _player.RightHandItemSlot, mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(_player, _player.RightHandItemSlot, mainHand: mainHand);

                if (readyAnimation != null && idleAnimation != null)
                {
                    Play(readyAnimation.Value, mainHand);
                    StartIdleTimer(idleAnimation.Value, mainHand);
                }
            }
        }
        else
        {
            if (_player.LeftHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
            {
                Play(item.ReadyAnimation, mainHand);
                StartIdleTimer(item.IdleAnimation, mainHand);
            }
            if (_player.LeftHandItemSlot.Itemstack?.Item is IHasDynamicIdleAnimations item2)
            {
                AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(_player, _player.LeftHandItemSlot, mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(_player, _player.LeftHandItemSlot, mainHand: mainHand);

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
    private readonly Dictionary<string, EnumAnimatedElement> _posesNames = Enum.GetValues<EnumAnimatedElement>().ToDictionary(value => value.ToString(), value => value);
    private readonly List<ElementPose?> _posesCache = Enum.GetValues<EnumAnimatedElement>().Select(_ => (ElementPose?)null).ToList();
    private readonly List<bool> _posesSet = Enum.GetValues<EnumAnimatedElement>().Select(_ => false).ToList();
    private readonly Settings _settings;
    private bool _updatePosesCache = false;
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

            if (_updatePosesCache)
            {
                _updatePosesCache = false;
            }
            else
            {
                for (int index = 0; index < _posesSet.Count; index++)
                {
                    if (!_posesSet[index])
                    {
                        _updatePosesCache = true;
                    }

                    _posesSet[index] = false;
                }
            }
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
            for (int index = 1; index < _posesCache.Count; index++)
            {
                if (_posesCache[index] == pose)
                {
                    element = (EnumAnimatedElement)index;
                    _posesSet[index] = true;
                    break;
                }
            }

            if (element == EnumAnimatedElement.Unknown && _updatePosesCache && _posesNames.TryGetValue(pose.ForElement.Name, out element))
            {
                _posesCache[(int)element] = pose;
            }
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

        if (_player.RightHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
        {
            string readyCategory = item.ReadyAnimation.Category;

            foreach (string category in _mainHandCategories.Where(element => element != readyCategory))
            {
                Stop(category);
            }
            _mainHandCategories.Clear();

            Play(item.ReadyAnimation, true);
            StartIdleTimer(item.IdleAnimation, true);
        }
        else if (_player.RightHandItemSlot.Itemstack?.Item is IHasDynamicIdleAnimations item2)
        {
            AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(_player, _player.RightHandItemSlot, mainHand: true);
            AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(_player, _player.RightHandItemSlot, mainHand: true);

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

        if (_player.LeftHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
        {
            string readyCategory = item.ReadyAnimation.Category;

            foreach (string category in _offhandCategories.Where(element => element != readyCategory))
            {
                Stop(category);
            }
            _offhandCategories.Clear();

            Play(item.ReadyAnimation, false);
            StartIdleTimer(item.IdleAnimation, false);
        }
        else if (_player.LeftHandItemSlot.Itemstack?.Item is IHasDynamicIdleAnimations item2)
        {
            AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(_player, _player.LeftHandItemSlot, mainHand: false);
            AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(_player, _player.LeftHandItemSlot, mainHand: false);

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

        Item? item = slot.Itemstack?.Item;

        return item is IHasDynamicIdleAnimations || item is IHasIdleAnimations;
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