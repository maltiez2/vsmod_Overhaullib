using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public enum SlingState
{
    Unloaded,
    Load,
    PreLoaded,
    Loaded,
    WindUp,
    Swinging,
    Releasing
}

public sealed class SlingStats : WeaponStats
{
    public string LoadAnimation { get; set; } = "";
    public string WindUpAnimation { get; set; } = "";
    public string WindUpAfterLoadAnimation { get; set; } = "";
    public string SwingAnimation { get; set; } = "";
    public string ReleaseAnimation { get; set; } = "";
    public AimingStatsJson Aiming { get; set; } = new();
    public float BulletDamageMultiplier { get; set; } = 1;
    public int BulletDamageTier { get; set; } = 1;
    public float BulletVelocity { get; set; } = 1;
    public string BulletWildcard { get; set; } = "*slingbullet-*";
    public float Zeroing { get; set; } = 1.5f;
    public float[] DispersionMOA { get; set; } = [0, 0];
    public float ScreenShakeStrength { get; set; } = 0.2f;
    public bool TwoHanded { get; set; } = false;
    public float MaxSwingSpeed { get; set; } = 1;
    public float MinSwingSpeed { get; set; } = 0.5f;
    public float SwingSpeedPerSwing { get; set; } = 0.11f;
    public float SwingAnimationSpeed { get; set; } = 1f;
}

public class SlingClient : RangeWeaponClient
{
    public SlingClient(ICoreClientAPI api, Item item, AmmoSelector ammoSelector) : base(api, item)
    {
        Api = api;
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Sling should have AnimatableAttachable behavior.");
        BulletTransform = new(item.Attributes["BulletTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();

        Stats = item.Attributes.AsObject<SlingStats>();
        AimingStats = Stats.Aiming.ToStats();
        AmmoSelector = ammoSelector;
        TwoHanded = Stats.TwoHanded;

        Settings = api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;

#if DEBUG
        DebugWindowManager.RegisterTransformByCode(BulletTransform, $"Sling bullet - {item.Code}");
        //DebugWidgets.FloatDrag("test", "test3", $"{item.Code}-followX", () => AimingStats.AnimationFollowX, (value) => AimingStats.AnimationFollowX = value)
        //DebugWidgets.FloatDrag("test", "test3", $"{item.Code}-followY", () => AimingStats.AnimationFollowY, (value) => AimingStats.AnimationFollowY = value)
#endif
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
#if DEBUG
        //ItemSlot? bulletSlot = GetBulletSlot(player);
        //if (bulletSlot == null) return;
        //Attachable.SetAttachment(player.EntityId, "bullet", bulletSlot.Itemstack, BulletTransform);
        //AttachmentSystem.SendAttachPacket(player.EntityId, "bullet", bulletSlot.Itemstack, BulletTransform);
#endif
    }

    public override void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
        PlayerBehavior?.SetState((int)BowState.Unloaded);
        AimingSystem.StopAiming();
    }

    public override void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        base.OnRegistered(behavior, api);
        AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    protected readonly ICoreClientAPI Api;
    protected readonly AnimatableAttachable Attachable;
    protected readonly ModelTransform BulletTransform;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly SlingStats Stats;
    protected readonly AimingStats AimingStats;
    protected readonly AmmoSelector AmmoSelector;
    protected readonly Settings Settings;
    protected AimingAnimationController? AimingAnimationController;
    protected bool AfterLoad = false;
    protected float CurrentSwingSpeed = 0f;
    protected bool ReleaseWhenReady = false;
    protected bool Released = false;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (state != (int)SlingState.Unloaded || !CheckForOtherHandEmpty(mainHand, player)) return false;
        if (CanBlockWithOtherHand(player, mainHand)) return false;

        ItemSlot? bulletSlot = GetBulletSlot(player);

        if (bulletSlot == null) return false;

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);
        
        RangedWeaponSystem.Reload(slot, bulletSlot, 1, mainHand, ReloadCallback);
        AnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed, weight: 1000, callback: LoadAnimationCallback, callbackHandler: code => LoadAnimationCallback(code, bulletSlot.Itemstack, player));
        TpAnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed, weight: 1000);

        AimingAnimationController?.Play(mainHand);

        state = (int)SlingState.Load;

        AfterLoad = true;

        return true;
    }
    protected virtual void ReloadCallback(bool success)
    {
        SlingState state = GetState<SlingState>(mainHand: true);

        if (success)
        {
            switch (state)
            {
                case SlingState.PreLoaded:
                    SetState(SlingState.Loaded, mainHand: true);
                    break;
                case SlingState.Load:
                    SetState(SlingState.PreLoaded, mainHand: true);
                    break;
            }
        }
        else
        {
            AnimationBehavior?.PlayReadyAnimation(true);
            TpAnimationBehavior?.PlayReadyAnimation(true);
            SetState(SlingState.Unloaded, mainHand: true);
        }
    }

    protected virtual void LoadAnimationCallback(string code, ItemStack stack, EntityPlayer player)
    {
        switch (code)
        {
            case "attach":
                Attachable.SetAttachment(player.EntityId, "bullet", stack, BulletTransform);
                AttachmentSystem.SendAttachPacket(player.EntityId, "bullet", stack, BulletTransform);
                break;
        }
    }

    protected virtual bool LoadAnimationCallback()
    {
        SlingState state = GetState<SlingState>(mainHand: true);

        switch (state)
        {
            case SlingState.PreLoaded:
                SetState(SlingState.Loaded, mainHand: true);
                break;
            case SlingState.Load:
                SetState(SlingState.PreLoaded, mainHand: true);
                break;
        }

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Swing(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (state != (int)SlingState.Loaded || !CheckForOtherHandEmpty(mainHand, player)) return false;

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        AnimationRequestByCode request = new(
            AfterLoad ? Stats.WindUpAfterLoadAnimation : Stats.WindUpAnimation,
            1,
            1,
            "main",
            TimeSpan.FromSeconds(0.2),
            TimeSpan.FromSeconds(0.2),
            true,
            () => WindUpCallback(slot, player, mainHand));
        AnimationBehavior?.Play(request, mainHand);
        TpAnimationBehavior?.Play(request, mainHand);

        AimingStats.CursorType = Enum.Parse<AimingCursorType>(Settings.SlingsAimingCursorType);
        AimingStats.VerticalLimit = Settings.SlingsAimingVerticalLimit * Stats.Aiming.VerticalLimit;
        AimingStats.HorizontalLimit = Settings.SlingsAimingHorizontalLimit * Stats.Aiming.HorizontalLimit;
        AimingSystem.ResetAim();
        AimingSystem.StartAiming(AimingStats);
        AimingSystem.AimingState = WeaponAimingState.Blocked;

        AfterLoad = false;

        state = (int)SlingState.WindUp;

        if (!AimingSystem.Aiming)
        {
            AimingStats aimingStats = AimingStats.Clone();
            aimingStats.AimDifficulty *= stackStats.AimingDifficulty;

            AimingStats.CursorType = Enum.Parse<AimingCursorType>(Settings.SlingsAimingCursorType);
            AimingStats.VerticalLimit = Settings.SlingsAimingVerticalLimit * Stats.Aiming.VerticalLimit;
            AimingStats.HorizontalLimit = Settings.SlingsAimingHorizontalLimit * Stats.Aiming.HorizontalLimit;
            AimingSystem.StartAiming(aimingStats);
            AimingSystem.AimingState = WeaponAimingState.Blocked;

            AimingAnimationController?.Play(mainHand);
        }

        return true;
    }

    protected virtual bool WindUpCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        if (CheckState(true, SlingState.WindUp))
        {
            SetState(SlingState.Swinging, true);
            AimingSystem.AimingState = WeaponAimingState.PartCharge;
            CurrentSwingSpeed = Stats.MinSwingSpeed;

            AnimationRequestByCode request = new(
                Stats.SwingAnimation,
                GetAnimationSpeed(player, Stats.ProficiencyStat) * CurrentSwingSpeed * Stats.SwingAnimationSpeed,
                1,
                "main",
                TimeSpan.FromSeconds(0.2),
                TimeSpan.FromSeconds(0.2),
                true,
                () => WindUpCallback(slot, player, mainHand));
            AnimationBehavior?.Play(request, true);
            TpAnimationBehavior?.Play(request, true);

            ReleaseWhenReady = false;

            return true;
        }

        if (ReleaseWhenReady && CheckState(true, SlingState.Swinging))
        {
            ReleaseWhenReady = false;
            Released = false;
            SetState(SlingState.Releasing, true);

            AnimationRequestByCode request = new(
                Stats.ReleaseAnimation,
                GetAnimationSpeed(player, Stats.ProficiencyStat) * CurrentSwingSpeed * Stats.SwingAnimationSpeed,
                1,
                "main",
                TimeSpan.FromSeconds(0.2),
                TimeSpan.FromSeconds(0.2),
                true,
                () => ShootCallback(slot, player, mainHand),
                code => ShootCallback(code, slot, player, mainHand));
            AnimationBehavior?.Play(request, true);
            TpAnimationBehavior?.Play(request, true);
        }

        if (CheckState(true, SlingState.Swinging))
        {
            CurrentSwingSpeed = GameMath.Clamp(CurrentSwingSpeed + Stats.SwingSpeedPerSwing, Stats.MinSwingSpeed, Stats.MaxSwingSpeed);

            AnimationRequestByCode request = new(
                Stats.SwingAnimation,
                GetAnimationSpeed(player, Stats.ProficiencyStat) * CurrentSwingSpeed * Stats.SwingAnimationSpeed,
                1,
                "main",
                TimeSpan.FromSeconds(0.2),
                TimeSpan.FromSeconds(0.2),
                true,
                () => WindUpCallback(slot, player, mainHand));
            AnimationBehavior?.Play(request, true);
            TpAnimationBehavior?.Play(request, true);
            
            if (CurrentSwingSpeed >= Stats.MaxSwingSpeed)
            {
                AimingSystem.AimingState = WeaponAimingState.FullCharge;
            }
            
            return true;
        }

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Release(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (CheckState(state, SlingState.Load, SlingState.PreLoaded))
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            Attachable.ClearAttachments(player.EntityId);
            AttachmentSystem.SendClearPacket(player.EntityId);
            state = (int)SlingState.Unloaded;
            ReleaseWhenReady = false;
            AimingSystem.StopAiming();
            return true;
        }

        if (CheckState(state, SlingState.Loaded, SlingState.WindUp))
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            state = (int)SlingState.Loaded;
            AfterLoad = false;
            ReleaseWhenReady = false;
            AimingSystem.StopAiming();
            return true;
        }

        if (state == (int)SlingState.Swinging)
        {
            ReleaseWhenReady = true;
            return true;
        }

        return false;
    }

    protected virtual void ShootCallback(string code, ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        switch (code)
        {
            case "detach":
                Attachable.ClearAttachments(player.EntityId);
                AttachmentSystem.SendClearPacket(player.EntityId);
                break;

            case "release":
                if (Released) return;
                Released = true;
                Vec3d position = player.LocalEyePos + player.Pos.XYZ;
                Vector3 targetDirection = AimingSystem.TargetVec;
                targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.Zeroing);
                RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { }, GetAdditionalData(slot, player, mainHand));
                Api.World.AddCameraShake(Stats.ScreenShakeStrength * CurrentSwingSpeed / Stats.MaxSwingSpeed);
                break;
        }
    }

    protected virtual bool ShootCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        SetState(SlingState.Unloaded);

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        AimingSystem.StopAiming();

        return true;
    }

    protected virtual byte[] GetAdditionalData(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        float swingSpeed = CurrentSwingSpeed / Stats.MaxSwingSpeed;
        
        MemoryStream stream = new();
        BinaryWriter writer = new(stream);
        writer.Write(swingSpeed);
        writer.Write(GetAnimationSpeed(player, Stats.ProficiencyStat));
        return stream.ToArray();
    }

    protected virtual ItemSlot? GetBulletSlot(EntityPlayer player)
    {
        ItemSlot? bulletSlot = null;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(AmmoSelector.SelectedAmmo, slot.Itemstack.Item.Code.ToString()))
            {
                bulletSlot = slot;
                return false;
            }

            return true;
        });

        if (bulletSlot == null)
        {
            player.WalkInventory(slot =>
            {
                if (slot?.Itemstack?.Item == null) return true;

                if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.BulletWildcard, slot.Itemstack.Item.Code.ToString()))
                {
                    bulletSlot = slot;
                    return false;
                }

                return true;
            });
        }

        return bulletSlot;
    }

    protected bool CanBlockWithOtherHand(EntityPlayer player, bool mainHand = true)
    {
        ItemSlot otherHandSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        return (otherHandSlot.Itemstack?.Item as IHasMeleeWeaponActions)?.CanBlock(!mainHand) ?? false;
    }
}

public class SlingServer : RangeWeaponServer
{
    public SlingServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        ProjectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<SlingStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.BulletWildcard, ammoSlot.Itemstack.Item.Code.ToString()))
        {
            BulletSlots[player.Entity.EntityId] = (ammoSlot.Inventory, ammoSlot.Inventory.GetSlotId(ammoSlot));
            return true;
        }

        if (ammoSlot == null)
        {
            BulletSlots.Remove(player.Entity.EntityId);
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (!BulletSlots.ContainsKey(player.Entity.EntityId)) return false;

        (InventoryBase inventory, int slotId) = BulletSlots[player.Entity.EntityId];

        if (inventory.Count <= slotId) return false;

        ItemSlot? arrowSlot = inventory[slotId];

        if (arrowSlot?.Itemstack == null || arrowSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = arrowSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(arrowSlot.Itemstack);

        if (stats == null)
        {
            BulletSlots.Remove(player.Entity.EntityId);
            return false;
        }

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        Vector3d playerVelocity = new(player.Entity.ServerPos.Motion.X, player.Entity.ServerPos.Motion.Y, player.Entity.ServerPos.Motion.Z);

        GetDamageMultiplier(packet, out float swingSpeed, out float manipulationSpeed);

        float speedFactor = MathF.Sqrt(swingSpeed * manipulationSpeed);

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = Stats.BulletDamageMultiplier * stackStats.DamageMultiplier * swingSpeed * Math.Clamp(manipulationSpeed, 1, 2),
            DamageTier = Stats.BulletDamageTier + stackStats.DamageTierBonus,
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = GetDirectionWithDispersion(packet.Velocity, [Stats.DispersionMOA[0] * stackStats.DispersionMultiplier, Stats.DispersionMOA[1] * stackStats.DispersionMultiplier]) * Stats.BulletVelocity * stackStats.ProjectileSpeed * speedFactor + playerVelocity
        };

        ProjectileSystem.Spawn(packet.ProjectileId[0], stats, spawnStats, arrowSlot.TakeOut(1), slot.Itemstack, shooter);

        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1 + stats.AdditionalDurabilityCost);

        slot.MarkDirty();
        arrowSlot.MarkDirty();
        return true;
    }

    protected virtual void GetDamageMultiplier(ShotPacket packet, out float swingSpeed, out float manipulationSpeed)
    {
        MemoryStream stream = new(packet.Data);
        BinaryReader writer = new(stream);
        swingSpeed = writer.ReadSingle();
        manipulationSpeed = writer.ReadSingle();
    }

    protected readonly Dictionary<long, (InventoryBase, int)> BulletSlots = new();
    protected readonly SlingStats Stats;
}

public class SlingItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasMoveAnimations
{
    public SlingClient? ClientLogic { get; private set; }
    public SlingServer? ServerLogic { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public AnimationRequestByCode IdleAnimation { get; set; }
    public AnimationRequestByCode ReadyAnimation { get; set; }
    public AnimationRequestByCode WalkAnimation { get; set; }
    public AnimationRequestByCode RunAnimation { get; set; }
    public AnimationRequestByCode SwimAnimation { get; set; }
    public AnimationRequestByCode SwimIdleAnimation { get; set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            SlingStats stats = Attributes.AsObject<SlingStats>();
            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            WalkAnimation = new(stats.WalkAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            RunAnimation = new(stats.RunAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            SwimAnimation = new(stats.SwimAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            SwimIdleAnimation = new(stats.SwimIdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);

            _stats = stats;
            _ammoSelector = new(clientAPI, _stats.BulletWildcard);
            _clientApi = clientAPI;

            ClientLogic = new(clientAPI, this, _ammoSelector);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }

        _altForInteractions = new()
        {
            MouseButton = EnumMouseButton.None,
            HotKeyCode = "Alt",
            ActionLangCode = "combatoverhaul:interaction-hold-alt"
        };

        _ammoSelection = new()
        {
            ActionLangCode = Lang.Get("combatoverhaul:interaction-ammoselection"),
            HotKeyCodes = new string[1] { "toolmodeselect" },
            MouseButton = EnumMouseButton.None
        };
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (_stats != null && _stats.ProficiencyStat != "")
        {
            string description = Lang.Get("combatoverhaul:iteminfo-proficiency", Lang.Get($"combatoverhaul:proficiency-{_stats.ProficiencyStat}"));
            dsc.AppendLine(description);
        }

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        if (_stats == null) return;

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(inSlot.Itemstack);

        dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", $"{_stats.BulletDamageMultiplier * stackStats.DispersionMultiplier:F1}", _stats.BulletDamageTier + stackStats.DamageTierBonus));
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction[] interactions = base.GetHeldInteractionHelp(inSlot);

        return interactions.Append(_ammoSelection).Append(_altForInteractions);
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        if (_clientApi?.World.Player.Entity.EntityId == byPlayer.Entity.EntityId)
        {
            return _ammoSelector?.GetToolMode(slot, byPlayer, blockSelection) ?? 0;
        }

        return 0;
    }
    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if (_clientApi?.World.Player.Entity.EntityId == forPlayer.Entity.EntityId)
        {
            return _ammoSelector?.GetToolModes(slot, forPlayer, blockSel) ?? Array.Empty<SkillItem>();
        }

        return Array.Empty<SkillItem>();
    }
    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if (_clientApi?.World.Player.Entity.EntityId == byPlayer.Entity.EntityId)
        {
            _ammoSelector?.SetToolMode(slot, byPlayer, blockSelection, toolMode);
        }
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    private SlingStats? _stats;
    private AmmoSelector? _ammoSelector;
    private ICoreClientAPI? _clientApi;
    private WorldInteraction? _altForInteractions;
    private WorldInteraction? _ammoSelection;
}
