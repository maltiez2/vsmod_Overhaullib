using CombatOverhaul.Armor;
using CombatOverhaul.Utils;
using CombatOverhaul.Vanity;
using System.Net.Sockets;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Integration;

public static class CharacterTabPatch
{
    public class CharacterSlotsStatus
    {
        public bool Misc { get; set; } = false;
        public bool Belt { get; set; } = false;
        public bool Backpack { get; set; } = false;
        public bool Headgear { get; set; } = false;
        public bool FrontGear { get; set; } = false;
        public bool BackGear { get; set; } = false;
        public bool RightShoulderGear { get; set; } = false;
        public bool LeftShoulderGear { get; set; } = false;
        public bool WaistHear { get; set; } = false;
    }

    public static CharacterSlotsStatus SlotsStatus { get; set; } = new();

    public static ICoreClientAPI? Api => GuiDialogPatches.Api;

    public static bool VanityTabEnabled { get; set; } = false;

    public static readonly HashSet<int> SlotsThatCantBeHidden = [];

    public static readonly HashSet<int> HiddenSlots = [];

    public static event Action? OnSaveAndApply;

    public static bool GuiDialogCharacter_ComposeCharacterTab(GuiDialogCharacter __instance, GuiComposer compo)
    {
        if (Api?.Gui.Icons.CustomIcons.ContainsKey("armorhead") == false)
        {
            RegisterArmorIcons(Api);
        }

        switch (_currentTabType)
        {
            case CharacterTabType.Default:
                ComposeCharacterTab(__instance, compo);
                break;
            case CharacterTabType.Vanity:
                ComposeVanityTab(__instance, compo);
                break;
        }

        return false;
    }

    private enum CharacterTabType
    {
        Default,
        Vanity
    }

    private static FieldInfo? GuiDialogCharacter_insetSlotBounds = typeof(GuiDialogCharacter).GetField("insetSlotBounds", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogCharacter_characterInv = typeof(GuiDialogCharacter).GetField("characterInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogCharacter_SendInvPacket = typeof(GuiDialogCharacter).GetMethod("SendInvPacket", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogCharacter_ComposeGuis = typeof(GuiDialogCharacter).GetMethod("ComposeGuis", BindingFlags.NonPublic | BindingFlags.Instance);
    private static CharacterTabType _currentTabType = CharacterTabType.Default;
    
    private static int _vanityScrollBarPosition = 0;


    private static void ComposeCharacterTab(GuiDialogCharacter dialog, GuiComposer composer)
    {
        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;

        ElementBounds vanityButtonBounds = ElementBounds.Fixed(260, -16, 120, 24);

        ElementBounds outerInsetBounds = ElementBounds.Fixed(-0, 22, 414, 356);

        ElementBounds leftSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 6).FixedGrow(0, pad);

        ElementBounds leftArmorSlotBoundsHead = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0 + 23, 1, 1).FixedGrow(0, pad);
        ElementBounds leftArmorSlotBoundsBody = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0 + 23 + 51, 1, 1).FixedGrow(0, pad);
        ElementBounds leftArmorSlotBoundsLegs = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0 + 23 + 102, 1, 1).FixedGrow(0, pad);

        ElementBounds leftMiscSlotBounds1 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 153, 1, 1).FixedGrow(0, pad);
        ElementBounds leftMiscSlotBounds2 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 204, 1, 1).FixedGrow(0, pad);
        ElementBounds leftMiscSlotBounds3 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 255, 1, 1).FixedGrow(0, pad);

        leftSlotBounds.FixedRightOf(leftArmorSlotBoundsLegs, 2);

        ElementBounds insetSlotBounds = ElementBounds.Fixed(0, 20 + 2 + pad, 250 - 60, leftSlotBounds.fixedHeight - 2 * pad - 4);

        GuiDialogCharacter_insetSlotBounds?.SetValue(dialog, insetSlotBounds);

        insetSlotBounds.FixedRightOf(leftSlotBounds, 10);

        ElementBounds rightSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 6).FixedGrow(0, pad).FixedRightOf(insetSlotBounds, 10);
        ElementBounds rightGearSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 1).FixedGrow(0, pad).FixedRightOf(rightSlotBounds);

        ElementBounds additionalSlots1Bounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 2, 20 + pad + 306, 1, 1).FixedGrow(0, pad);
        ElementBounds additionalSlots2Bounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 2, 20 + pad + 306, 1, 1).FixedGrow(0, pad).FixedRightOf(additionalSlots1Bounds, 161);

        leftSlotBounds.fixedHeight -= 6;
        rightSlotBounds.fixedHeight -= 6;
        rightGearSlotBounds.fixedHeight -= 6;

        CairoFont hoverTextFont = CairoFont.WhiteSmallText();
        CairoFont vanityButtonFont = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);

        IInventory? characterInv = (IInventory?)GuiDialogCharacter_characterInv?.GetValue(dialog);
        Action<object> SendInvPacket = (object parameter) => GuiDialogCharacter_SendInvPacket?.Invoke(dialog, [parameter]);

        composer
            .AddInset(outerInsetBounds, 0)
            .AddIf(!ArmorInventory._disableVanillaArmorSlots)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [12], leftArmorSlotBoundsHead, "armorSlotsHead")
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [13], leftArmorSlotBoundsBody, "armorSlotsBody")
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [14], leftArmorSlotBoundsLegs, "armorSlotsLegs")
            .EndIf()
            .AddIf(SlotsStatus.Misc)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._gearSlotsLastIndex - 11], leftMiscSlotBounds1, "miscSlot1")
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._gearSlotsLastIndex - 10], leftMiscSlotBounds2, "miscSlot2")
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._gearSlotsLastIndex - 9], leftMiscSlotBounds3, "miscSlot3")
            .EndIf()
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [0, 1, 2, 11, 3, 4], leftSlotBounds, "leftSlots")
            .AddInset(insetSlotBounds, 2)
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [6, 7, 8, 10, 5, 9], rightSlotBounds, "rightSlots")
            .AddIf(SlotsStatus.Headgear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 0], rightGearSlotBounds, "gearSlots1")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-headgear"), hoverTextFont, 200, rightGearSlotBounds)
            .EndIf()
            .AddIf(SlotsStatus.FrontGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 1], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad), "gearSlots2")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-frontgear"), hoverTextFont, 200, rightGearSlotBounds.BelowCopy(fixedDeltaY: pad))
            .EndIf()
            .AddIf(SlotsStatus.BackGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 2], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots3")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-backgear"), hoverTextFont, 200, rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad))
            .EndIf()
            .AddIf(SlotsStatus.RightShoulderGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 3], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots4")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-rightshouldergear"), hoverTextFont, 200, rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad))
            .EndIf()
            .AddIf(SlotsStatus.LeftShoulderGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 4], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots5")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-leftshouldergear"), hoverTextFont, 200, rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad))
            .EndIf()
            .AddIf(SlotsStatus.WaistHear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 5], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots6")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-waistgear"), hoverTextFont, 200, rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad))
            .EndIf()
            .AddIf(SlotsStatus.Belt)
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 9], additionalSlots1Bounds, "additionalSlots10")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBeltLeft"), hoverTextFont, 200, additionalSlots1Bounds)
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 10], additionalSlots1Bounds.RightCopy(), "additionalSlots11")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBeltRight"), hoverTextFont, 200, additionalSlots1Bounds.RightCopy())
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 11], additionalSlots1Bounds.RightCopy().RightCopy(), "additionalSlots12")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBeltBack"), hoverTextFont, 200, additionalSlots1Bounds.RightCopy().RightCopy())
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 12], additionalSlots1Bounds.RightCopy().RightCopy().RightCopy(), "additionalSlots13")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBeltFront"), hoverTextFont, 200, additionalSlots1Bounds.RightCopy().RightCopy().RightCopy())
            .EndIf()
            .AddIf(SlotsStatus.Backpack)
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 13], additionalSlots2Bounds, "additionalSlots20")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBackpack1"), hoverTextFont, 200, additionalSlots2Bounds)
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 14], additionalSlots2Bounds.RightCopy(), "additionalSlots21")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBackpack2"), hoverTextFont, 200, additionalSlots2Bounds.RightCopy())
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 15], additionalSlots2Bounds.RightCopy().RightCopy(), "additionalSlots22")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBackpack3"), hoverTextFont, 200, additionalSlots2Bounds.RightCopy().RightCopy())
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 16], additionalSlots2Bounds.RightCopy().RightCopy().RightCopy(), "additionalSlots23")
                .AddAutoSizeHoverText(Lang.Get($"combatoverhaul:slot-addBackpack4"), hoverTextFont, 200, additionalSlots2Bounds.RightCopy().RightCopy().RightCopy())
            .EndIf()
            .AddIf(VanityTabEnabled)
                .AddButton("to vanity tab", () => SwitchTab(dialog, composer), vanityButtonBounds, vanityButtonFont, EnumButtonStyle.Small, "vanityButton")
            .EndIf()
        ;
    }

    private static void ComposeVanityTab(GuiDialogCharacter dialog, GuiComposer composer)
    {
        if (Api == null) return;

        ArmorInventory? characterInventory = (ArmorInventory?)GuiDialogCharacter_characterInv?.GetValue(dialog);
        VanityInventory? vanityInventory = GeneralUtils.GetVanityInventory(Api.World.Player);

        if (vanityInventory  == null || characterInventory == null) return;

        vanityInventory.RecolorSlots();

        List<ItemSlot> slots = vanityInventory.ToList();

        slots.OfType<IClickableSlot>().Foreach(slot => slot.OnSlotClicked = OnSlotClicked);

        int[] slotIndexes = slots
            .Select(slot => vanityInventory.GetSlotId(slot))
            .Where(id => !ArmorInventory._disableVanillaArmorSlots || !ArmorInventory.IsVanillaArmorSlot(id))
            .Where(id => ArmorInventory._disableVanillaArmorSlots || !ArmorInventory.IsModdedArmorSlot(id))
            .ToArray();

        int skillItemsHeight = slotIndexes.Length * 44 / 4 + 176 - 44 + 3;

        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;

        ElementBounds vanityButtonBounds = ElementBounds.Fixed(260, -16, 120, 24);
        ElementBounds outerInsetBounds = ElementBounds.Fixed(-0, 22, 414 + 2, 356 + 5);
        ElementBounds insetSlotBounds = ElementBounds.Fixed(2, 2, 250 - 60 - 16, outerInsetBounds.fixedHeight - 4 - 44).WithParent(outerInsetBounds);
        ElementBounds saveButtonBounds = ElementBounds.Fixed(4, 28, 172, 36).FixedUnder(insetSlotBounds);

        GuiDialogCharacter_insetSlotBounds?.SetValue(dialog, insetSlotBounds);

        ElementBounds rightInsetBounds = ElementBounds.Fixed(0, 2, 212, outerInsetBounds.fixedHeight - 2).WithParent(outerInsetBounds).RightOf(insetSlotBounds, 4);
        ElementBounds childBounds = ElementBounds.Fixed(0, 2, 218, skillItemsHeight);
        ElementBounds clipBounds = rightInsetBounds.FlatCopy().FixedRightOf(insetSlotBounds, 4).FixedGrow(6, 11).WithFixedHeight(outerInsetBounds.fixedHeight - 2);
        ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 4, 0, 4, vanityInventory.Count / 4 + 1).FixedGrow(0, pad);
        ElementBounds scrollbarBounds = rightInsetBounds.FlatCopy().FixedRightOf(rightInsetBounds, 224 - 46).WithFixedWidth(20);
        scrollbarBounds.absInnerHeight = 300;

        CairoFont hoverTextFont = CairoFont.WhiteSmallText();
        CairoFont vanityButtonFont = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);

        // BUILD START
        composer.AddInset(outerInsetBounds, 0);
        composer.AddInset(insetSlotBounds, 1);
        composer.AddInset(rightInsetBounds, 1);

        composer.BeginChildElements(rightInsetBounds);
        composer.BeginClip(clipBounds);
        composer.BeginChildElements(childBounds);

        composer.AddItemSlotGrid(vanityInventory, SendInventoryPacket, 4, slotIndexes, slotBounds, "vanitySlots");

        composer.EndChildElements();
        composer.EndClip();
        composer.AddVerticalScrollbar(value => OnNewScrollbarValue(dialog, composer, childBounds, value), scrollbarBounds, "scrollbar");
        composer.EndChildElements();

        composer.AddButton("to clothes tab", () => SwitchTab(dialog, composer), vanityButtonBounds, vanityButtonFont, EnumButtonStyle.Small, "vanityButton");
        composer.AddToggleButton("Hide backpack", vanityButtonFont , SetHideBackpack, saveButtonBounds, "hideBackpackButton");
        // BUILD END

        int scrollbarPosition = _vanityScrollBarPosition;
        composer.GetScrollbar("scrollbar").SetHeights(
            (float)outerInsetBounds.fixedHeight - 2,
            skillItemsHeight
        );
        composer.GetScrollbar("scrollbar").SetScrollbarPosition(scrollbarPosition);
        SetChildBoundsOffset(childBounds, scrollbarPosition);

        composer.GetToggleButton("hideBackpackButton").SetValue(IsBackpackHidden());
    }

    private static bool IsBackpackHidden()
    {
        return Api?.World?.Player?.Entity?.WatchedAttributes?.GetBool(VanitySystemServer.HideBackpackAttribute) ?? false;
    }

    private static void SetHideBackpack(bool hide)
    {
        Api?.ModLoader?.GetModSystem<CombatOverhaulSystem>()?.ClientVanitySystem?.HideBackpack(hide);
    }

    private static void SendInventoryPacket(object packet)
    {
        Api?.Network.SendPacketClient(packet);
    }

    private static bool OnSlotClicked(ItemSlot slot, ItemSlot sourceSlot, ref ItemStackMoveOperation operation)
    {
        return false;
    }

    private static void RegisterArmorIcons(ICoreClientAPI api)
    {
        api.Gui.Icons.CustomIcons["armorhead"] = Api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-helmet.svg"));
        api.Gui.Icons.CustomIcons["armorbody"] = Api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-body.svg"));
        api.Gui.Icons.CustomIcons["armorlegs"] = Api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-legs.svg"));
    }

    private static bool SwitchTab(GuiDialogCharacter dialog, GuiComposer composer)
    {
        SwitchCurrentTabType();
        GuiDialogCharacter_ComposeGuis?.Invoke(dialog, []);
        return true;
    }

    private static bool SaveAndApply(GuiDialogCharacter dialog, GuiComposer composer)
    {
        OnSaveAndApply?.Invoke();

        Api?.World?.Player?.Entity?.MarkShapeModified();

        return true;
    }

    private static void OnNewScrollbarValue(GuiDialogCharacter dialog, GuiComposer composer, ElementBounds childBounds, float value)
    {
        if (childBounds != null)
        {
            SetChildBoundsOffset(childBounds, value);
        }

        _vanityScrollBarPosition = (int)(composer.GetScrollbar("scrollbar").CurrentYPosition / composer.GetScrollbar("scrollbar").ScrollConversionFactor);
    }

    private static void SetChildBoundsOffset(ElementBounds childBounds, float value)
    {
        childBounds.fixedY = 2 - value;
        childBounds.fixedY = childBounds.fixedY - childBounds.fixedY % 51 + 2;
        childBounds.CalcWorldBounds();
    }

    private static void SwitchCurrentTabType()
    {
        _currentTabType = (CharacterTabType)(((int)_currentTabType + 1) % Enum.GetValues<CharacterTabType>().Length);
    }

    private static int GetSlotIndex(ItemSlot slot) => slot.Inventory?.GetSlotId(slot) ?? 0;

    private static bool IsSlotItemHidden(ItemSlot slot)
    {
        int index = GetSlotIndex(slot);

        return HiddenSlots.Contains(index);
    }

    private static bool IsSlotEnabled(ItemSlot slot)
    {
        int index = GetSlotIndex(slot);

        return !SlotsThatCantBeHidden.Contains(index);
    }

    private static string GetSlotColor(ItemSlot slot)
    {
        bool active = !IsSlotItemHidden(slot);
        bool enabled = IsSlotEnabled(slot);

        if (!enabled)
        {
            return "#333333FF";
        }

        return active ? "#FFFFFFFF" : "#FF5555FF";
    }
}
