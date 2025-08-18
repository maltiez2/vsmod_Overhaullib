﻿using CombatOverhaul.Integration;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using ProtoBuf;
using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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
    AnimationRequestByCode? GetIdleAnimation(bool mainHand);
    AnimationRequestByCode? GetReadyAnimation(bool mainHand);
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
        
        SoundsSynchronizerClient soundsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientSoundsSynchronizer ?? throw new Exception();
        ParticleEffectsManager particleEffectsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ParticleEffectsManager ?? throw new Exception();
        _composer = new(soundsManager, particleEffectsManager, player);

        if (!_mainPlayer) return;
        
        HarmonyPatches.OnBeforeFrame += OnBeforeFrame;
        HarmonyPatches.OnFrame += OnFrame;
        player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().OnDispose += Dispose;
    }

    public override string PropertyName() => "CombatOverhaul:FirstPersonAnimations";

    public override void OnGameTick(float deltaTime)
    {
        if (!_mainPlayer || _player.RightHandItemSlot == null || _player.LeftHandItemSlot == null) return;

        LoggerUtil.Mark(_api, "fpan-ogt-0");
        
        int mainHandItemId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        int offhandItemId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

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

        LoggerUtil.Mark(_api, "fpan-ogt-1");
        
        foreach ((AnimationRequest request, bool mainHand, bool skip, int itemId) in _playRequests)
        {
            if (!skip) PlayRequest(request, mainHand);
        }
        _playRequests.Clear();
        
        LoggerUtil.Mark(_api, "fpan-ogt-2");
    }

    public PlayerItemFrame? FrameOverride { get; set; } = null;
    public static float CurrentFov { get; set; } = ClientSettings.FieldOfView;

    public void Play(AnimationRequest request, bool mainHand = true)
    {
        _playRequests.Add((request, mainHand, false, CurrentItemId(mainHand)));
        StopIdleTimer(mainHand);
    }
    public void Play(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        if (_animationsManager == null) return;

        string modelPrefix = "-" + entity.WatchedAttributes.GetString("skinModel", "seraph").Replace(':', '-');

        if (_animationsManager.Animations.TryGetValue(requestByCode.Animation + modelPrefix, out Animation? animation))
        {

        }
        else if (!_animationsManager.Animations.TryGetValue(requestByCode.Animation, out animation))
        {
            return;
        }

        AnimationRequest request = new(animation, requestByCode);

        Play(request, mainHand);
    }
    public void Play(bool mainHand, string animation, string category = "main", float animationSpeed = 1, float weight = 1, System.Func<bool>? callback = null, Action<string>? callbackHandler = null, bool easeOut = true)
    {
        AnimationRequestByCode request = new(animation, animationSpeed, weight, category, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1), easeOut, callback, callbackHandler);
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
                AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: mainHand);

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
                AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: mainHand);

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
    //private readonly Dictionary<string, AnimatedElement> _posesNames = Enum.GetValues<AnimatedElement>().ToDictionary(value => value.ToString(), value => value);
    //private readonly List<ElementPose?> _posesCache = Enum.GetValues<AnimatedElement>().Select(_ => (ElementPose?)null).ToList();
    //private readonly List<bool> _posesSet = Enum.GetValues<AnimatedElement>().Select(_ => false).ToList();
    //private bool _updatePosesCache = false;
    private bool _frameApplied = false;
    private int _offHandItemId = 0;
    private int _mainHandItemId = 0;
    private long _mainHandIdleTimer = -1;
    private long _offHandIdleTimer = -1;
    private bool _resetFov = false;
    private readonly ICoreClientAPI _api;
    private readonly List<(AnimationRequest request, bool mainHand, bool skip, int itemId)> _playRequests = new();
    private float _previousHeadBobbingAmplitudeFactor = 1;
    private Animatable? _animatable = null;
    private Vector3 _eyePosition = new();
    private float _eyeHeight = 0;

    private static readonly TimeSpan _readyTimeout = TimeSpan.FromSeconds(3);

    private readonly FieldInfo _mainCameraInfo = typeof(ClientMain).GetField("MainCamera", BindingFlags.Public | BindingFlags.Instance) ?? throw new Exception();
    private readonly FieldInfo _cameraFov = typeof(Camera).GetField("Fov", BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new Exception();

    private void OnBeforeFrame(Entity targetEntity, float dt)
    {
        if (HarmonyPatches.Settings.DisableThirdPersonAnimations) return;

        if (!_frameApplied) return;

        if (!IsOwner(targetEntity)) return;

        LoggerUtil.Mark(_api, "fpan-obf-0");

        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt));

        SetFov(_lastFrame.Player.FovMultiplier, true);
        _resetFov = true;

        _animatable = (targetEntity as EntityAgent)?.RightHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
        _eyePosition = new((float)targetEntity.LocalEyePos.X, (float)targetEntity.LocalEyePos.Y, (float)targetEntity.LocalEyePos.Z);
        _eyeHeight = (float)targetEntity.Properties.EyeHeight;

        if (Math.Abs(_lastFrame.Player.PitchFollow - PlayerFrame.DefaultPitchFollow) >= PlayerFrame.Epsilon)
        {
            if (entity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
            {
                renderer.HeldItemPitchFollowOverride = _lastFrame.Player.PitchFollow;
            }
        }
        else
        {
            if (entity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
            {
                renderer.HeldItemPitchFollowOverride = null;
            }
        }

        /*if (_updatePosesCache)
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
        }*/

        _frameApplied = false;

        LoggerUtil.Mark(_api, "fpan-obf-2");
    }
    private void OnFrame(Entity targetEntity, ElementPose pose, ClientAnimator animator)
    {
        if (HarmonyPatches.Settings.DisableThirdPersonAnimations) return;
        
        LoggerUtil.Mark(_api, "fpan-of-0");
        
        _frameApplied = true;
        
        if (IsImmersiveFirstPerson(targetEntity)) return;
        if (!DebugWindowManager.PlayAnimationsInThirdPerson && !IsFirstPerson(targetEntity)) return;
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
            ApplyFrame(FrameOverride.Value, pose, animator is ClientItemAnimator);
        }
        else
        {
            ApplyFrame(_lastFrame, pose, animator is ClientItemAnimator);
        }
        
        LoggerUtil.Mark(_api, "fpan-of-1");
    }
    private void ApplyFrame(PlayerItemFrame frame, ElementPose pose, bool itemAnimator)
    {
        if (!Enum.TryParse(pose.ForElement.Name, out AnimatedElement element)) // Cant cache ElementPose because they are new each frame
        {
            element = AnimatedElement.Unknown;
        }

        if (!itemAnimator && element == AnimatedElement.Unknown)
        {
            return;
        }

        /*AnimatedElement element = AnimatedElement.Unknown;
        for (int index = 1; index < _posesCache.Count; index++)
        {
            if (_posesCache[index] == pose)
            {
                element = (AnimatedElement)index;
                _posesSet[index] = true;
                break;
            }
        }

        if (element == AnimatedElement.Unknown && _updatePosesCache && _posesNames.TryGetValue(pose.ForElement.Name, out element))
        {
            _posesCache[(int)element] = pose;
        }*/

        LoggerUtil.Mark(_api, "fpan-appf-1");
        
        frame.Apply(pose, element, _eyePosition, _eyeHeight);

        _player.HeadBobbingAmplitude /= _previousHeadBobbingAmplitudeFactor;
        _previousHeadBobbingAmplitudeFactor = _lastFrame.Player.BobbingAmplitude;
        _player.HeadBobbingAmplitude *= _previousHeadBobbingAmplitudeFactor;

        if (_animatable != null && frame.DetachedAnchor)
        {
            _animatable.DetachedAnchor = true;
        }

        if (_animatable != null && frame.SwitchArms)
        {
            _animatable.SwitchArms = true;
        }
        
        LoggerUtil.Mark(_api, "fpan-appf-3");
    }
    private static bool IsOwner(Entity entity) => (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
    private static bool IsFirstPerson(Entity entity)
    {
        bool owner = (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
        if (!owner) return false;

        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };

        return firstPerson;
    }
    private static bool IsImmersiveFirstPerson(Entity entity)
    {
        return ((entity.Api as ICoreClientAPI)?.Settings.Bool["immersiveFpMode"] ?? false) && IsFirstPerson(entity);
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

        PlayerCamera? camera = (PlayerCamera?)_mainCameraInfo.GetValue(client);
        if (camera == null) return;

        float? fovField = (float?)_cameraFov.GetValue(camera);
        if (fovField == null) return;

        float equalizeMultiplier = MathF.Sqrt(ClientSettings.FieldOfView / (float)ClientSettings.FpHandsFoV);

        PlayerRenderingPatches.HandsFovMultiplier = multiplier * (equalizeFov ? equalizeMultiplier : 1);
        _cameraFov.SetValue(camera, ClientSettings.FieldOfView * GameMath.DEG2RAD * multiplier);

        CurrentFov = ClientSettings.FieldOfView * multiplier;
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

    private void MainHandItemChanged()
    {
        StopIdleTimer(mainHand: true);

        if (_player.RightHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
        {
            string readyCategory = item.ReadyAnimation.Category;

            foreach (string category in _mainHandCategories.Where(element => element != readyCategory))
            {
                _composer.Stop(category);
            }
            StopRequestFromPreviousItem(true);
            _mainHandCategories.Clear();

            Play(item.ReadyAnimation, true);
            StartIdleTimer(item.IdleAnimation, true);
        }
        else if (_player.RightHandItemSlot.Itemstack?.Item is IHasDynamicIdleAnimations item2)
        {
            AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: true);
            AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: true);

            if (readyAnimation == null || idleAnimation == null)
            {
                foreach (string category in _mainHandCategories)
                {
                    _composer.Stop(category);
                }
                StopRequestFromPreviousItem(true);
                _mainHandCategories.Clear();
            }
            else
            {
                string readyCategory = readyAnimation.Value.Category;

                foreach (string category in _mainHandCategories.Where(element => element != readyCategory))
                {
                    _composer.Stop(category);
                }
                StopRequestFromPreviousItem(true);
                _mainHandCategories.Clear();

                Play(readyAnimation.Value, true);
                StartIdleTimer(idleAnimation.Value, true);
            }
        }
        else
        {
            foreach (string category in _mainHandCategories)
            {
                _composer.Stop(category);
            }
            StopRequestFromPreviousItem(true);
            _mainHandCategories.Clear();
        }
    }
    private void OffhandItemChanged()
    {
        StopIdleTimer(false);

        if (_player.LeftHandItemSlot.Itemstack?.Item is IHasIdleAnimations item)
        {
            string readyCategory = item.ReadyAnimation.Category;

            foreach (string category in _offhandCategories.Where(element => element != readyCategory))
            {
                _composer.Stop(category);
            }
            StopRequestFromPreviousItem(false);
            _offhandCategories.Clear();

            Play(item.ReadyAnimation, false);
            StartIdleTimer(item.IdleAnimation, false);
        }
        else if (_player.LeftHandItemSlot.Itemstack?.Item is IHasDynamicIdleAnimations item2)
        {
            AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: false);
            AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: false);

            if (readyAnimation == null || idleAnimation == null)
            {
                foreach (string category in _offhandCategories)
                {
                    _composer.Stop(category);
                }
                StopRequestFromPreviousItem(false);
                _offhandCategories.Clear();
            }
            else
            {
                string readyCategory = readyAnimation.Value.Category;

                foreach (string category in _offhandCategories.Where(element => element != readyCategory))
                {
                    _composer.Stop(category);
                }
                StopRequestFromPreviousItem(false);
                _offhandCategories.Clear();

                Play(readyAnimation.Value, false);
                StartIdleTimer(idleAnimation.Value, false);
            }
        }
        else
        {
            foreach (string category in _offhandCategories)
            {
                _composer.Stop(category);
            }
            StopRequestFromPreviousItem(false);
            _offhandCategories.Clear();
        }
    }

    private void StartIdleTimer(AnimationRequestByCode request, bool mainHand)
    {
        if (_api?.IsGamePaused == true) return;

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
    
    public void Dispose()
    {
        HarmonyPatches.OnBeforeFrame -= OnBeforeFrame;
        HarmonyPatches.OnFrame -= OnFrame;
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
        
        _composer = new(null, null, player);

        HarmonyPatches.OnBeforeFrame += OnBeforeFrame;
        HarmonyPatches.OnFrame += OnFrame;
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

        LoggerUtil.Mark(_api, "tpan-ogt-0");

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
        
        LoggerUtil.Mark(_api, "tpan-ogt-1");
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
        
        string modelPrefix = "-" + entity.WatchedAttributes.GetString("skinModel", "seraph").Replace(':','-');

        if (_animationsManager.Animations.TryGetValue(requestByCode.Animation + "-tp" + modelPrefix, out Animation? animation))
        {

        }
        else if (_animationsManager.Animations.TryGetValue(requestByCode.Animation + modelPrefix, out animation))
        {

        }
        else if(_animationsManager.Animations.TryGetValue(requestByCode.Animation + "-tp", out animation))
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
                AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: mainHand);

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
                AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: mainHand);
                AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: mainHand);

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


    private readonly Composer _composer;
    private readonly EntityPlayer _player;
    private readonly AnimationsManager? _animationsManager;
    private readonly AnimationSystemClient _animationSystem;

    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private readonly List<string> _offhandCategories = new();
    private readonly List<string> _mainHandCategories = new();
    private readonly bool _mainPlayer = false;
    private readonly Dictionary<string, AnimatedElement> _posesNames = Enum.GetValues<AnimatedElement>().ToDictionary(value => value.ToString(), value => value);
    private readonly List<ElementPose?> _posesCache = Enum.GetValues<AnimatedElement>().Select(_ => (ElementPose?)null).ToList();
    private readonly List<bool> _posesSet = Enum.GetValues<AnimatedElement>().Select(_ => false).ToList();
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

    private static readonly TimeSpan _readyTimeout = TimeSpan.FromSeconds(3);
    private static Dictionary<string, ThirdPersonAnimationsBehavior> _existingBehaviors = new();

    private void OnBeforeFrame(Entity targetEntity, float dt)
    {
        if (entity.EntityId != targetEntity.EntityId || !targetEntity.IsRendered) return;

        if (!_frameApplied) return;
        
        LoggerUtil.Mark(_api, "tpan-obf-0");

        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt));
        
        LoggerUtil.Mark(_api, "tpan-obf-1");

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

        _frameApplied = false;

        LoggerUtil.Mark(_api, "tpan-obf-2");
    }
    private void OnFrame(Entity targetEntity, ElementPose pose, ClientAnimator animator)
    {
        LoggerUtil.Mark(_api, "tpan-of-0");

        if (entity.EntityId != targetEntity.EntityId) return;

        _frameApplied = true;
        
        if (!targetEntity.IsRendered || DebugWindowManager.PlayAnimationsInThirdPerson || IsFirstPerson(targetEntity)) return;

        if (FrameOverride != null)
        {
            ApplyFrame(FrameOverride.Value, pose, animator is ClientItemAnimator);
        }
        else
        {
            ApplyFrame(_lastFrame, pose, animator is ClientItemAnimator);
        }
        
        LoggerUtil.Mark(_api, "tpan-of-1");
    }
    private void ApplyFrame(PlayerItemFrame frame, ElementPose pose, bool itemAnimator)
    {
        AnimatedElement element = AnimatedElement.Unknown;
        for (int index = 1; index < _posesCache.Count; index++)
        {
            if (_posesCache[index] == pose)
            {
                element = (AnimatedElement)index;
                _posesSet[index] = true;
                break;
            }
        }

        if (element == AnimatedElement.Unknown && _updatePosesCache && _posesNames.TryGetValue(pose.ForElement.Name, out element))
        {
            _posesCache[(int)element] = pose;
        }

        if (!itemAnimator && element == AnimatedElement.Unknown)
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

        LoggerUtil.Mark(_api, "tpan-appf-1");

        if (element == AnimatedElement.LowerTorso) return;
        
        frame.Apply(pose, element, _eyePosition, _eyeHeight, _pitch, true, false);

        LoggerUtil.Mark(_api, "tpan-appf-2");
    }
    private static bool IsFirstPerson(Entity entity)
    {
        bool owner = (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
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
            AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: true);
            AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: true);

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
            AnimationRequestByCode? readyAnimation = item2.GetReadyAnimation(mainHand: false);
            AnimationRequestByCode? idleAnimation = item2.GetIdleAnimation(mainHand: false);

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
            HarmonyPatches.OnBeforeFrame -= OnBeforeFrame;
            HarmonyPatches.OnFrame -= OnFrame;
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
            HarmonyPatches.OnBeforeFrame -= OnBeforeFrame;
            HarmonyPatches.OnFrame -= OnFrame;
        }
        _existingBehaviors.Clear();
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimationRequestPacket
{
    public bool MainHand { get; set; }
    public string Animation { get; set; } = "";
    public float AnimationSpeed { get; set; }
    public float Weight { get; set; }
    public string Category { get; set; } = "";
    public double EaseOutDurationMs { get; set; }
    public double EaseInDurationMs { get; set; }
    public bool EaseOut { get; set; }
    public long EntityId { get; set; }
    public int ItemId { get; set; }

    public AnimationRequestPacket()
    {

    }

    public AnimationRequestPacket(AnimationRequestByCode request, bool mainHand, long entityId, int itemId)
    {
        MainHand = mainHand;
        Animation = request.Animation;
        AnimationSpeed = request.AnimationSpeed;
        Weight = request.Weight;
        Category = request.Category;
        EaseOutDurationMs = request.EaseOutDuration.TotalMilliseconds;
        EaseInDurationMs = request.EaseInDuration.TotalMilliseconds;
        EaseOut = request.EaseOut;
        EntityId = entityId;
        ItemId = itemId;
    }

    public (AnimationRequestByCode request, bool mainHand) ToRequest()
    {
        AnimationRequestByCode request = new(Animation, AnimationSpeed, Weight, Category, TimeSpan.FromMilliseconds(EaseOutDurationMs), TimeSpan.FromMilliseconds(EaseInDurationMs), EaseOut);
        return (request, MainHand);
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AnimationStopRequestPacket
{
    public string Category { get; set; }
    public long EntityId { get; set; }

    public AnimationStopRequestPacket()
    {

    }

    public AnimationStopRequestPacket(string category, long entityId)
    {
        Category = category;
        EntityId = entityId;
    }
}


public sealed class AnimationSystemClient
{
    public AnimationSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel("CO-animationsystem")
            .RegisterMessageType<AnimationRequestPacket>()
            .RegisterMessageType<AnimationStopRequestPacket>()
            .SetMessageHandler<AnimationRequestPacket>(HandlePacket)
            .SetMessageHandler<AnimationStopRequestPacket>(HandlePacket);
    }

    public void SendPlayPacket(AnimationRequestByCode request, bool mainHand, long entityId, int itemId)
    {
        _clientChannel.SendPacket(new AnimationRequestPacket(request, mainHand, entityId, itemId));
    }
    public void SendStopPacket(string category, long entityId)
    {
        _clientChannel.SendPacket(new AnimationStopRequestPacket(category, entityId));
    }

    private readonly IClientNetworkChannel _clientChannel;
    private readonly ICoreClientAPI _api;

    private void HandlePacket(AnimationRequestPacket packet)
    {
        EntityPlayer? player = _api.World.GetEntityById(packet.EntityId) as EntityPlayer;

        if (player == null) return;

        if (GetCurrentItemId(packet.MainHand, player) != packet.ItemId) return;

        (AnimationRequestByCode request, bool mainHnad) = packet.ToRequest();

        player.GetBehavior<ThirdPersonAnimationsBehavior>()?.Play(request, mainHnad);
    }

    private void HandlePacket(AnimationStopRequestPacket packet)
    {
        _api.World.GetEntityById(packet.EntityId)?.GetBehavior<ThirdPersonAnimationsBehavior>()?.Stop(packet.Category);
    }

    private int GetCurrentItemId(bool mainHand, EntityPlayer player) => mainHand ? player?.RightHandItemSlot?.Itemstack?.Item?.Id ?? 0 : player?.LeftHandItemSlot?.Itemstack?.Item?.Id ?? 0;
}

public sealed class AnimationSystemServer
{
    public AnimationSystemServer(ICoreServerAPI api)
    {
        _serverChannel = api.Network.RegisterChannel("CO-animationsystem")
            .RegisterMessageType<AnimationRequestPacket>()
            .RegisterMessageType<AnimationStopRequestPacket>()
            .SetMessageHandler<AnimationRequestPacket>(HandlePacket)
            .SetMessageHandler<AnimationStopRequestPacket>(HandlePacket);
    }


    private readonly IServerNetworkChannel _serverChannel;

    private void HandlePacket(IServerPlayer player, AnimationRequestPacket packet)
    {
        _serverChannel.BroadcastPacket(packet, player);
    }

    private void HandlePacket(IServerPlayer player, AnimationStopRequestPacket packet)
    {
        _serverChannel.BroadcastPacket(packet, player);
    }
}