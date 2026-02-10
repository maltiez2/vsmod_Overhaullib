using CombatOverhaul.Animations;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Inputs;
using CombatOverhaul.Integration;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.Utils;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public class MeleeWeapon : Item, IHasMultipleWeaponLogicModes, IHasWeaponLogic, IHasRangedWeaponLogic, IHasDynamicMoveAnimations, IHasMeleeWeaponActions, IHasServerBlockCallback, ISetsRenderingOffset, IMouseWheelInput, IOnGameTick, IRestrictAction
{
    public MeleeWeaponClient? ClientLogic => ClientModes?.CurrentMode;
    public MeleeWeaponServer? ServerLogic { get; private set; }
    public MeleeWeaponClientModesCollection? ClientModes { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IEnumerable<IClientWeaponLogic> IHasMultipleWeaponLogicModes.ClientLogicModes => ClientModes?.Clients.Select(entry => entry.Value) ?? [];
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public bool RenderingOffset { get; set; }
    

    public bool RestrictRightHandAction() => ClientLogic?.RestrictRightHandAction() ?? false;
    public bool RestrictLeftHandAction() => ClientLogic?.RestrictLeftHandAction() ?? false;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            LoadClientSide(clientAPI);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            LoadServerSide(serverAPI);
        }

        AltForInteractions = new()
        {
            MouseButton = EnumMouseButton.None,
            HotKeyCode = "Alt",
            ActionLangCode = "combatoverhaul:interaction-hold-alt"
        };
    }

    public AnimationRequestByCode? GetIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => ClientLogic?.GetIdleAnimation(player, slot, mainHand);
    public AnimationRequestByCode? GetReadyAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => ClientLogic?.GetReadyAnimation(player, slot, mainHand);
    public AnimationRequestByCode? GetWalkAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => ClientLogic?.GetWalkAnimation(player, slot, mainHand);
    public AnimationRequestByCode? GetRunAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => ClientLogic?.GetRunAnimation(player, slot, mainHand);
    public AnimationRequestByCode? GetSwimAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => ClientLogic?.GetSwimAnimation(player, slot, mainHand);
    public AnimationRequestByCode? GetSwimIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => ClientLogic?.GetSwimIdleAnimation(player, slot, mainHand);

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (DebugWindowManager.RenderDebugColliders)
        {
            ClientLogic?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public bool CanAttack(EntityPlayer player, bool mainHand) => (ClientLogic?.CanAttack(player, mainHand) ?? false);
    public bool CanBlock(EntityPlayer player, bool mainHand) => (ClientLogic?.CanBlock(player, mainHand) ?? false) || (ClientLogic?.CanParry(player, mainHand) ?? false);
    public bool CanThrow(EntityPlayer player, bool mainHand) => (ClientLogic?.CanThrow(player, mainHand) ?? false);
    public void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        ClientLogic?.OnGameTick(slot, player, ref state, mainHand);
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefaultAction;
    }
    public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        return remainingResistance;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        ClientLogic?.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine("");

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }

    public override WorldInteraction?[]? GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction?[]? interactionHelp = base.GetHeldInteractionHelp(inSlot);

        if (ChangeGripInteraction != null)
        {
            interactionHelp = interactionHelp?.Append(ChangeGripInteraction);
        }

        if (ClientModes?.Modes.Count > 1)
        {
            interactionHelp = interactionHelp?.Append(ModesSelectionInteraction);
        }

        return interactionHelp?.Append(AltForInteractions);
    }

    public virtual void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand, float damageBlocked, int attackTier, int blockTier)
    {
        int durabilityDamage = 1 + Math.Clamp(attackTier - blockTier, 0, attackTier);
        DamageItem(player.Entity.World, player.Entity, slot, durabilityDamage);
    }

    public virtual bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta) => ClientLogic?.OnMouseWheel(slot, byPlayer, delta) ?? false;

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        return ClientModes?.GetToolMode(byPlayer.Entity, slot) ?? 0;
    }
    public override SkillItem[]? GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if (ClientApi?.World.Player.Entity.EntityId == forPlayer.Entity.EntityId && ClientModes != null)
        {
            return ClientModes.GetToolModes(forPlayer.Entity, slot);
        }

        return null;
    }
    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if (ClientApi?.World.Player.Entity.EntityId == byPlayer.Entity.EntityId && ClientModes != null)
        {
            ClientModes.SetToolMode(byPlayer.Entity, slot, toolMode);
        }
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, GridRecipe byRecipe)
    {
        base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);

        GeneralUtils.MarkItemStack(outputSlot);
        outputSlot.MarkDirty();
    }


    protected WorldInteraction? AltForInteractions;
    protected WorldInteraction? ChangeGripInteraction;
    protected WorldInteraction? ModesSelectionInteraction;
    protected ICoreClientAPI? ClientApi;
    

    protected virtual void LoadClientSide(ICoreClientAPI clientAPI)
    {
        ClientApi = clientAPI;
        ClientModes = new(clientAPI, this);
        MeleeWeaponStats Stats = Attributes.AsObject<MeleeWeaponStats>();
        RenderingOffset = Stats.RenderingOffset;

        if (Stats.OneHandedStance?.GripMinLength != Stats.OneHandedStance?.GripMaxLength || Stats.TwoHandedStance?.GripMinLength != Stats.TwoHandedStance?.GripMaxLength)
        {
            ChangeGripInteraction = new()
            {
                MouseButton = EnumMouseButton.Wheel,
                ActionLangCode = "combatoverhaul:interaction-grip-change"
            };
        }

        ModesSelectionInteraction = new()
        {
            ActionLangCode = Lang.Get("combatoverhaul:interaction-mode-selection"),
            HotKeyCodes = ["toolmodeselect"],
            MouseButton = EnumMouseButton.None
        };
    }

    protected virtual void LoadServerSide(ICoreServerAPI serverAPI)
    {
        ServerLogic = new(serverAPI, this);
    }
}
