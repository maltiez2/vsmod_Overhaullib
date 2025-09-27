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
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public enum BowState
{
    Unloaded,
    Load,
    PreLoaded,
    Loaded,
    Draw,
    Drawn
}

public sealed class BowStats : WeaponStats
{
    public string LoadAnimation { get; set; } = "";
    public string DrawAnimation { get; set; } = "";
    public string DrawAfterLoadAnimation { get; set; } = "";
    public string ReleaseAnimation { get; set; } = "";
    public string TpAimAnimation { get; set; } = "";
    public AimingStatsJson Aiming { get; set; } = new();
    public float ArrowDamageMultiplier { get; set; } = 1;
    public int ArrowDamageTier { get; set; } = 1;
    public float ArrowVelocity { get; set; } = 1;
    public string ArrowWildcard { get; set; } = "*arrow-*";
    public float Zeroing { get; set; } = 1.5f;
    public float[] DispersionMOA { get; set; } = [0, 0];
    public float ScreenShakeStrength { get; set; } = 0.2f;
    public bool TwoHanded { get; set; } = true;
}

public class BowClient : RangeWeaponClient
{
    public BowClient(ICoreClientAPI api, Item item, AmmoSelector ammoSelector) : base(api, item)
    {
        Api = api;
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Bow should have AnimatableAttachable behavior.");
        ArrowTransform = new(item.Attributes["ArrowTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();

        Stats = item.Attributes.AsObject<BowStats>();
        AimingStats = Stats.Aiming.ToStats();
        AmmoSelector = ammoSelector;
        TwoHanded = Stats.TwoHanded;

        Settings = api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;

        //DebugWidgets.FloatDrag("test", "test3", $"{item.Code}-followX", () => AimingStats.AnimationFollowX, (value) => AimingStats.AnimationFollowX = value)
        //DebugWidgets.FloatDrag("test", "test3", $"{item.Code}-followY", () => AimingStats.AnimationFollowY, (value) => AimingStats.AnimationFollowY = value)
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
    }

    public override void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
        PlayerBehavior?.SetState((int)BowState.Unloaded);
        AimingSystem.StopAiming();

        AnimationBehavior?.StopVanillaAnimation(Stats.TpAimAnimation, mainHand);
    }

    public override void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        base.OnRegistered(behavior, api);
        AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    protected readonly ICoreClientAPI Api;
    protected readonly AnimatableAttachable Attachable;
    protected readonly ModelTransform ArrowTransform;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly BowStats Stats;
    protected readonly AimingStats AimingStats;
    protected readonly AmmoSelector AmmoSelector;
    protected readonly Settings Settings;
    protected AimingAnimationController? AimingAnimationController;
    protected bool AfterLoad = false;

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (state != (int)BowState.Unloaded || !CheckForOtherHandEmpty(mainHand, player)) return false;

        ItemSlot? arrowSlot = GetArrowSlot(player);

        if (arrowSlot == null) return false;

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        Attachable.SetAttachment(player.EntityId, "Arrow", arrowSlot.Itemstack, ArrowTransform);
        AttachmentSystem.SendAttachPacket(player.EntityId, "Arrow", arrowSlot.Itemstack, ArrowTransform);
        RangedWeaponSystem.Reload(slot, arrowSlot, 1, mainHand, ReloadCallback);

        AnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed, callback: LoadAnimationCallback);
        TpAnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed);

        AimingStats.CursorType = Enum.Parse<AimingCursorType>(Settings.BowsAimingCursorType);
        AimingStats.VerticalLimit = Settings.BowsAimingVerticalLimit * Stats.Aiming.VerticalLimit;
        AimingStats.HorizontalLimit = Settings.BowsAimingHorizontalLimit * Stats.Aiming.HorizontalLimit;
        AimingSystem.ResetAim();
        AimingSystem.StartAiming(AimingStats);
        AimingSystem.AimingState = WeaponAimingState.Blocked;

        AimingAnimationController?.Play(mainHand);

        state = (int)BowState.Load;

        AfterLoad = true;

        return true;
    }
    protected virtual void ReloadCallback(bool success)
    {
        BowState state = GetState<BowState>(mainHand: true);

        if (success)
        {
            switch (state)
            {
                case BowState.PreLoaded:
                    SetState(BowState.Loaded, mainHand: true);
                    break;
                case BowState.Load:
                    SetState(BowState.PreLoaded, mainHand: true);
                    break;
            }
        }
        else
        {
            AnimationBehavior?.PlayReadyAnimation(true);
            TpAnimationBehavior?.PlayReadyAnimation(true);
            SetState(BowState.Unloaded, mainHand: true);
        }
    }
    protected virtual bool LoadAnimationCallback()
    {
        BowState state = GetState<BowState>(mainHand: true);

        switch (state)
        {
            case BowState.PreLoaded:
                SetState(BowState.Loaded, mainHand: true);
                break;
            case BowState.Load:
                SetState(BowState.PreLoaded, mainHand: true);
                break;
        }

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (state != (int)BowState.Loaded || !CheckForOtherHandEmpty(mainHand, player)) return false;

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        AnimationRequestByCode request = new(AfterLoad ? Stats.DrawAfterLoadAnimation : Stats.DrawAnimation, GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), true, FullLoadCallback);
        AnimationBehavior?.Play(request, mainHand);
        TpAnimationBehavior?.Play(request, mainHand);

        AfterLoad = false;

        state = (int)BowState.Draw;

        if (!AimingSystem.Aiming)
        {
            AimingStats aimingStats = AimingStats.Clone();
            aimingStats.AimDifficulty *= stackStats.AimingDifficulty;

            AimingStats.CursorType = Enum.Parse<AimingCursorType>(Settings.BowsAimingCursorType);
            AimingStats.VerticalLimit = Settings.BowsAimingVerticalLimit * Stats.Aiming.VerticalLimit;
            AimingStats.HorizontalLimit = Settings.BowsAimingHorizontalLimit * Stats.Aiming.HorizontalLimit;
            AimingSystem.StartAiming(aimingStats);
            AimingSystem.AimingState = WeaponAimingState.Blocked;

            AimingAnimationController?.Play(mainHand);
        }

        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.TpAimAnimation, mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        AimingSystem.StopAiming();

        AnimationBehavior?.StopVanillaAnimation(Stats.TpAimAnimation, mainHand);

        if (CheckState(state, BowState.Load, BowState.PreLoaded))
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            Attachable.ClearAttachments(player.EntityId);
            AttachmentSystem.SendClearPacket(player.EntityId);
            state = (int)BowState.Unloaded;
            return true;
        }

        if (CheckState(state, BowState.Draw, BowState.Loaded))
        {
            AnimationBehavior?.PlayReadyAnimation(mainHand);
            TpAnimationBehavior?.PlayReadyAnimation(mainHand);
            state = (int)BowState.Loaded;
            AfterLoad = false;
            return true;
        }

        if (state != (int)BowState.Drawn) return false;

        AnimationBehavior?.Play(mainHand, Stats.ReleaseAnimation, callback: () => ShootCallback(slot, player, mainHand));
        TpAnimationBehavior?.Play(mainHand, Stats.ReleaseAnimation);

        return true;
    }

    protected virtual bool ShootCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        PlayerBehavior?.SetState(0, mainHand);

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;
        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.Zeroing);

        RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, _ => { });
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
        AimingAnimationController?.Stop(mainHand);

        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);

        Api.World.AddCameraShake(Stats.ScreenShakeStrength);

        return true;
    }

    protected virtual bool FullLoadCallback()
    {
        PlayerBehavior?.SetState((int)BowState.Drawn);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;
        return true;
    }

    protected virtual ItemSlot? GetArrowSlot(EntityPlayer player)
    {
        ItemSlot? arrowSlot = null;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(AmmoSelector.SelectedAmmo, slot.Itemstack.Item.Code.ToString()))
            {
                arrowSlot = slot;
                return false;
            }

            return true;
        });

        if (arrowSlot == null)
        {
            player.WalkInventory(slot =>
            {
                if (slot?.Itemstack?.Item == null) return true;

                if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.ArrowWildcard, slot.Itemstack.Item.Code.ToString()))
                {
                    arrowSlot = slot;
                    return false;
                }

                return true;
            });
        }

        return arrowSlot;
    }
}

public class BowServer : RangeWeaponServer
{
    public BowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        ProjectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        Stats = item.Attributes.AsObject<BowStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.ArrowWildcard, ammoSlot.Itemstack.Item.Code.ToString()))
        {
            ArrowSlots[player.Entity.EntityId] = (ammoSlot.Inventory, ammoSlot.Inventory.GetSlotId(ammoSlot));
            return true;
        }

        if (ammoSlot == null)
        {
            ArrowSlots.Remove(player.Entity.EntityId);
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (!ArrowSlots.ContainsKey(player.Entity.EntityId)) return false;

        (InventoryBase inventory, int slotId) = ArrowSlots[player.Entity.EntityId];

        if (inventory.Count <= slotId) return false;

        ItemSlot? arrowSlot = inventory[slotId];

        if (arrowSlot?.Itemstack == null || arrowSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = arrowSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(arrowSlot.Itemstack);

        if (stats == null)
        {
            ArrowSlots.Remove(player.Entity.EntityId);
            return false;
        }

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        Vector3d playerVelocity = new(player.Entity.ServerPos.Motion.X, player.Entity.ServerPos.Motion.Y, player.Entity.ServerPos.Motion.Z);

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = Stats.ArrowDamageMultiplier * stackStats.DamageMultiplier,
            DamageTier = Stats.ArrowDamageTier + stackStats.DamageTierBonus,
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = GetDirectionWithDispersion(packet.Velocity, [Stats.DispersionMOA[0] * stackStats.DispersionMultiplier, Stats.DispersionMOA[1] * stackStats.DispersionMultiplier]) * Stats.ArrowVelocity * stackStats.ProjectileSpeed + playerVelocity
        };

        ProjectileSystem.Spawn(packet.ProjectileId[0], stats, spawnStats, arrowSlot.TakeOut(1), slot.Itemstack, shooter);

        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1 + stats.AdditionalDurabilityCost);

        slot.MarkDirty();
        arrowSlot.MarkDirty();
        return true;
    }


    protected readonly Dictionary<long, (InventoryBase, int)> ArrowSlots = new();
    protected readonly BowStats Stats;
}

public class BowItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasMoveAnimations
{
    public BowClient? ClientLogic { get; private set; }
    public BowServer? ServerLogic { get; private set; }

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
            BowStats stats = Attributes.AsObject<BowStats>();
            IdleAnimation = new(stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            WalkAnimation = new(stats.WalkAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            RunAnimation = new(stats.RunAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            SwimAnimation = new(stats.SwimAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            SwimIdleAnimation = new(stats.SwimIdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);

            _stats = stats;
            _ammoSelector = new(clientAPI, _stats.ArrowWildcard);
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

        dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", $"{_stats.ArrowDamageMultiplier * stackStats.DispersionMultiplier:F1}", _stats.ArrowDamageTier + stackStats.DamageTierBonus));
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

    private BowStats? _stats;
    private AmmoSelector? _ammoSelector;
    private ICoreClientAPI? _clientApi;
    private WorldInteraction? _altForInteractions;
    private WorldInteraction? _ammoSelection;
}
