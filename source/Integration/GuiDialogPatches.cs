using CombatOverhaul.Armor;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace CombatOverhaul.Integration;

internal static class GuiDialogPatches
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

    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        _api = api;

        new Harmony(harmonyId).Patch(
                typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(GuiDialogPatches), nameof(ComposeCharacterTab)))
            );

        new Harmony(harmonyId).Patch(
                typeof(GuiDialogCharacter).GetMethod("OnRenderGUI", AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(GuiDialogPatches), nameof(OnRenderGUI)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogCharacter).GetMethod("OnRenderGUI", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);

        _api = null;
    }

    private static ICoreClientAPI? _api;
    private static FieldInfo? GuiDialogCharacter_insetSlotBounds = typeof(GuiDialogCharacter).GetField("insetSlotBounds", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogCharacter_characterInv = typeof(GuiDialogCharacter).GetField("characterInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogCharacter_SendInvPacket = typeof(GuiDialogCharacter).GetMethod("SendInvPacket", BindingFlags.NonPublic | BindingFlags.Instance);
    private static int _lastItemId = 0;

    private static bool ComposeCharacterTab(GuiDialogCharacter __instance, GuiComposer compo)
    {
        if (!_api.Gui.Icons.CustomIcons.ContainsKey("armorhead")) registerArmorIcons();

        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;

        ElementBounds leftSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 6).FixedGrow(0, pad);

        ElementBounds leftArmorSlotBoundsHead = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 1).FixedGrow(0, pad);
        ElementBounds leftArmorSlotBoundsBody = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 51, 1, 1).FixedGrow(0, pad);
        ElementBounds leftArmorSlotBoundsLegs = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 102, 1, 1).FixedGrow(0, pad);

        ElementBounds leftMiscSlotBounds1 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 153, 1, 1).FixedGrow(0, pad);
        ElementBounds leftMiscSlotBounds2 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 204, 1, 1).FixedGrow(0, pad);
        ElementBounds leftMiscSlotBounds3 = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 255, 1, 1).FixedGrow(0, pad);

        leftSlotBounds.FixedRightOf(leftArmorSlotBoundsLegs, 2);

        IInventory? characterInv = (IInventory?)GuiDialogCharacter_characterInv?.GetValue(__instance);

        Action<object> SendInvPacket = (object parameter) => GuiDialogCharacter_SendInvPacket?.Invoke(__instance, [parameter]);

        ElementBounds insetSlotBounds = ElementBounds.Fixed(0, 20 + 2 + pad, 250 - 60, leftSlotBounds.fixedHeight - 2 * pad - 4);

        GuiDialogCharacter_insetSlotBounds?.SetValue(__instance, insetSlotBounds);

        insetSlotBounds.FixedRightOf(leftSlotBounds, 10);

        ElementBounds rightSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 6).FixedGrow(0, pad);
        rightSlotBounds.FixedRightOf(insetSlotBounds, 10);
        ElementBounds rightGearSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 1).FixedGrow(0, pad);
        rightGearSlotBounds.FixedRightOf(rightSlotBounds);

        ElementBounds additionalSlots1Bounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 306, 1, 1).FixedGrow(0, pad);
        ElementBounds additionalSlots2Bounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad + 306, 1, 1).FixedGrow(0, pad).FixedRightOf(additionalSlots1Bounds, 161);

        leftSlotBounds.fixedHeight -= 6;
        rightSlotBounds.fixedHeight -= 6;
        rightGearSlotBounds.fixedHeight -= 6;

        compo
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
            .AddInset(insetSlotBounds, 0)
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [6, 7, 8, 10, 5, 9], rightSlotBounds, "rightSlots")
            .AddIf(SlotsStatus.Headgear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 0], rightGearSlotBounds, "gearSlots1")
            .EndIf()
            .AddIf(SlotsStatus.FrontGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 1], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad), "gearSlots2")
            .EndIf()
            .AddIf(SlotsStatus.BackGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 2], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots3")
            .EndIf()
            .AddIf(SlotsStatus.RightShoulderGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 3], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots4")
            .EndIf()
            .AddIf(SlotsStatus.LeftShoulderGear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 4], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots5")
            .EndIf()
            .AddIf(SlotsStatus.WaistHear)
                .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._armorSlotsLastIndex + 5], rightGearSlotBounds.BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad).BelowCopy(fixedDeltaY: pad), "gearSlots6")
            .EndIf()
            .AddIf(SlotsStatus.Belt)
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 9], additionalSlots1Bounds, "additionalSlots10")
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 10], additionalSlots1Bounds.RightCopy(), "additionalSlots11")
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 11], additionalSlots1Bounds.RightCopy().RightCopy(), "additionalSlots12")
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 12], additionalSlots1Bounds.RightCopy().RightCopy().RightCopy(), "additionalSlots13")
            .EndIf()
            .AddIf(SlotsStatus.Backpack)
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 13], additionalSlots2Bounds, "additionalSlots20")
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 14], additionalSlots2Bounds.RightCopy(), "additionalSlots21")
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 15], additionalSlots2Bounds.RightCopy().RightCopy(), "additionalSlots22")
                .AddItemSlotGrid(characterInv, SendInvPacket, 9, [ArmorInventory._armorSlotsLastIndex + 16], additionalSlots2Bounds.RightCopy().RightCopy().RightCopy(), "additionalSlots23")
            .EndIf()
        //.AddItemSlotGrid(characterInv, SendInvPacket, 9, Enumerable.Range(ArmorInventory._armorSlotsLastIndex + 9, 4).ToArray(), additionalSlots1Bounds, "additionalSlots1")
        //.AddItemSlotGrid(characterInv, SendInvPacket, 9, Enumerable.Range(ArmorInventory._armorSlotsLastIndex + 13, 4).ToArray(), additionalSlots2Bounds, "additionalSlots2")
        ;

        return false;
    }

    private static void registerArmorIcons()
    {
        _api.Gui.Icons.CustomIcons["armorhead"] = _api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-helmet.svg"));
        _api.Gui.Icons.CustomIcons["armorbody"] = _api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-body.svg"));
        _api.Gui.Icons.CustomIcons["armorlegs"] = _api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-legs.svg"));
    }

    private static bool _anySlotsHighlighted = false;
    private static void OnRenderGUI()
    {
        int currentItem = _api?.World.Player.InventoryManager.MouseItemSlot?.Itemstack?.Item?.ItemId ?? 0;

        if (_anySlotsHighlighted && currentItem == 0 && _lastItemId == 0)
        {
            InventoryCharacter? inventory2 = GeneralUtils.GetCharacterInventory(_api?.World.Player);
            if (inventory2 == null) return;

            foreach (ItemSlot slot in inventory2)
            {
                if (slot is ClothesSlot clothesSlot)
                {
                    clothesSlot.HexBackgroundColor = clothesSlot.PreviousColor;
                }
                else
                {
                    slot.HexBackgroundColor = null;
                }
            }

            _anySlotsHighlighted = false;
            return;
        }

        if (currentItem == _lastItemId) return;
        _lastItemId = currentItem;

        InventoryCharacter? inventory = GeneralUtils.GetCharacterInventory(_api?.World.Player);
        if (inventory == null) return;

        ItemSlot mouseSlot = _api.World.Player.InventoryManager.MouseItemSlot;

        _anySlotsHighlighted = false;
        foreach (ItemSlot slot in inventory)
        {
            if (slot.CanHold(mouseSlot))
            {
                if (slot is ClothesSlot clothesSlot && clothesSlot.HexBackgroundColor != "#5fbed4")
                {
                    clothesSlot.PreviousColor = clothesSlot.HexBackgroundColor;
                }
                slot.HexBackgroundColor = "#5fbed4";
                _anySlotsHighlighted = true;
            }
            else
            {
                if (slot is ClothesSlot clothesSlot)
                {
                    clothesSlot.HexBackgroundColor = clothesSlot.PreviousColor;
                }
                else
                {
                    slot.HexBackgroundColor = null;
                }
            }
        }
    }
}
