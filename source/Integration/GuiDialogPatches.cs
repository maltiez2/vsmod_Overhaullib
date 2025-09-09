using CombatOverhaul.Armor;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace CombatOverhaul.Integration;

public static class GuiDialogPatches
{
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
        ElementBounds rightGearSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 20 + pad, 1, 6).FixedGrow(0, pad);
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
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._gearSlotsLastIndex - 11], leftMiscSlotBounds1, "miscSlot1")
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._gearSlotsLastIndex - 10], leftMiscSlotBounds2, "miscSlot2")
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [ArmorInventory._gearSlotsLastIndex - 9], leftMiscSlotBounds3, "miscSlot3")
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [0, 1, 2, 11, 3, 4], leftSlotBounds, "leftSlots")
            .AddInset(insetSlotBounds, 0)
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, [6, 7, 8, 10, 5, 9], rightSlotBounds, "rightSlots")
            .AddItemSlotGrid(characterInv, SendInvPacket, 1, Enumerable.Range(ArmorInventory._armorSlotsLastIndex, ArmorInventory._gearSlotsCount - 11).ToArray(), rightGearSlotBounds, "gearSlots")
            .AddItemSlotGrid(characterInv, SendInvPacket, 9, Enumerable.Range(ArmorInventory._armorSlotsLastIndex + 9, 4).ToArray(), additionalSlots1Bounds, "additionalSlots1")
            .AddItemSlotGrid(characterInv, SendInvPacket, 9, Enumerable.Range(ArmorInventory._armorSlotsLastIndex + 13, 4).ToArray(), additionalSlots2Bounds, "additionalSlots2")
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

        if (_anySlotsHighlighted && currentItem == 0 && _lastItemId  == 0)
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
