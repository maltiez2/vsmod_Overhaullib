using CombatOverhaul.Armor;
using CombatOverhaul.Utils;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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

    public static GuiDialogInventory? GuiDialogInventoryInstance { get; set; }

    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        _api = api;

        new Harmony(harmonyId).Patch(
                typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(GuiDialogPatches), nameof(GuiDialogCharacter_ComposeCharacterTab)))
            );

        new Harmony(harmonyId).Patch(
                typeof(GuiDialogCharacter).GetMethod("OnRenderGUI", AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(GuiDialogPatches), nameof(OnRenderGUI)))
            );

        new Harmony(harmonyId).Patch(
                typeof(GuiDialogInventory).GetMethod("ComposeSurvivalInvDialog", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(GuiDialogPatches), nameof(GuiDialogInventory_ComposeSurvivalInvDialog)))
            );

        new Harmony(harmonyId).Patch(
                typeof(GuiDialogInventory).GetMethod("OnNewScrollbarvalue", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(GuiDialogPatches), nameof(GuiDialogInventory_OnNewScrollbarvalue_Patch)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogCharacter).GetMethod("OnRenderGUI", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogInventory).GetMethod("ComposeSurvivalInvDialog", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogInventory).GetMethod("OnNewScrollbarvalue", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);

        _api = null;
    }

    private static ICoreClientAPI? _api;
    private static int _lastItemId = 0;
    private static bool _anySlotsHighlighted = false;
    // GuiDialogCharacter
    private static FieldInfo? GuiDialogCharacter_insetSlotBounds = typeof(GuiDialogCharacter).GetField("insetSlotBounds", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogCharacter_characterInv = typeof(GuiDialogCharacter).GetField("characterInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogCharacter_SendInvPacket = typeof(GuiDialogCharacter).GetMethod("SendInvPacket", BindingFlags.NonPublic | BindingFlags.Instance);
    // GuiDialogInventory
    private static FieldInfo? GuiDialogInventory_survivalInvDialog = typeof(GuiDialogInventory).GetField("survivalInvDialog", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogInventory_prevRows = typeof(GuiDialogInventory).GetField("prevRows", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogInventory_backPackInv = typeof(GuiDialogInventory).GetField("backPackInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogInventory_craftingInv = typeof(GuiDialogInventory).GetField("craftingInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogInventory_CloseIconPressed = typeof(GuiDialogInventory).GetMethod("CloseIconPressed", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogInventory_SendInvPacket = typeof(GuiDialogInventory).GetMethod("SendInvPacket", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogInventory_OnNewScrollbarvalue = typeof(GuiDialogInventory).GetMethod("OnNewScrollbarvalue", BindingFlags.NonPublic | BindingFlags.Instance);


    private static bool GuiDialogCharacter_ComposeCharacterTab(GuiDialogCharacter __instance, GuiComposer compo)
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

    private static bool GuiDialogInventory_ComposeSurvivalInvDialog(GuiDialogInventory __instance)
    {
        GuiDialogInventoryInstance = __instance;

        IInventory? backPackInv = (IInventory?)GuiDialogInventory_backPackInv?.GetValue(__instance);
        IInventory? craftingInv = (IInventory?)GuiDialogInventory_craftingInv?.GetValue(__instance);

        double elemToDlgPad = GuiStyle.ElementToDialogPadding;
        double pad = GuiElementItemSlotGrid.unscaledSlotPadding;
        int rows = (int)Math.Ceiling(backPackInv.Count / 6f);

        GuiDialogInventory_prevRows?.SetValue(__instance, rows);

        // 1. The bounds of the slot grid itself. It is offseted by slot padding. It determines the size of the dialog, so we build the dialog from the bottom up
        ElementBounds slotGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, pad, 6, 7).FixedGrow(2 * pad, 2 * pad);

        // 1a.) Determine the full size of scrollable area, required to calculate scrollbar handle size
        ElementBounds fullGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 6, rows);

        // 2. Around that is the 3 wide inset stroke
        ElementBounds insetBounds = slotGridBounds.ForkBoundingParent(3, 3, 3, 3);

        // 2a. The scrollable bounds is also the clipping bounds. Needs it's parent to be set.
        ElementBounds clippingBounds = slotGridBounds.CopyOffsetedSibling();
        clippingBounds.fixedHeight -= 3; // Why?

        ElementBounds gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 3, 3).FixedRightOf(insetBounds, 45);
        gridBounds.fixedY += 50;

        ElementBounds outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 1, 1).FixedRightOf(insetBounds, 45).FixedUnder(gridBounds, 20);
        outputBounds.fixedX += pad + GuiElementPassiveItemSlot.unscaledSlotSize;

        // 3. Around all that is the dialog centered to screen middle, with some extra spacing right for the scrollbar
        ElementBounds dialogBounds =
            insetBounds
            .ForkBoundingParent(elemToDlgPad, elemToDlgPad + 30, elemToDlgPad + gridBounds.fixedWidth + 20, elemToDlgPad)
        ;

        if (_api.Settings.Bool["immersiveMouseMode"])
        {
            dialogBounds
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-12, 0)
            ;
        }
        else
        {
            dialogBounds
                .WithAlignment(EnumDialogArea.CenterMiddle)
                .WithFixedAlignmentOffset(20, 0)
            ;
        }

        // 4. Don't forget the Scroll bar.  Sometimes mods add bags that are a bit big.
        ElementBounds scrollBarBounds = ElementStdBounds.VerticalScrollbar(insetBounds).WithParent(dialogBounds);
        scrollBarBounds.fixedOffsetX -= 2;
        scrollBarBounds.fixedWidth = 15;

        GuiComposer survivalInvDialog = _api.Gui.CreateCompo("inventory-backpack", dialogBounds);
        survivalInvDialog.AddShadedDialogBG(ElementBounds.Fill);
        survivalInvDialog.AddDialogTitleBar(Lang.Get("Inventory and Crafting"), () => CloseIconPressed(__instance));
        survivalInvDialog.AddVerticalScrollbar((data) => OnNewScrollbarvalue(__instance, data), scrollBarBounds, "scrollbar");

        survivalInvDialog.AddInset(insetBounds, 3, 0.85f);
        survivalInvDialog.BeginClip(clippingBounds);
        ComposeBackpackSlots(survivalInvDialog, __instance, backPackInv, fullGridBounds);
        survivalInvDialog.EndClip();

        survivalInvDialog.AddItemSlotGrid(craftingInv, (data) => SendInvPacket(__instance, data), 3, [0, 1, 2, 3, 4, 5, 6, 7, 8], gridBounds, "craftinggrid");
        survivalInvDialog.AddItemSlotGrid(craftingInv, (data) => SendInvPacket(__instance, data), 1, [9], outputBounds, "outputslot");

        try
        {
            survivalInvDialog.Compose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return true;
        }

        survivalInvDialog.GetScrollbar("scrollbar").SetHeights(
            (float)(slotGridBounds.fixedHeight),
            (float)(_rows.Select(rows => (rows + 0.4) * (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding)).Sum()) - 12)
        ;

        GuiDialogInventory_survivalInvDialog?.SetValue(__instance, survivalInvDialog);

        return false;
    }

    private static bool GuiDialogInventory_OnNewScrollbarvalue_Patch(GuiDialogInventory __instance, float value)
    {
        if (!__instance.IsOpened()) return false;

        GuiComposer? sid = (GuiComposer?)GuiDialogInventory_survivalInvDialog.GetValue(__instance);

        if (_api.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            return true;
        }
        else if (_api.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival && sid != null)
        {
            ElementBounds bounds = sid.GetSlotGridExcl("slotgrid").Bounds;
            bounds.fixedY = 10 - GuiElementItemSlotGrid.unscaledSlotPadding - value;
            bounds.CalcWorldBounds();

            int index = 0;
            while (true)
            {
                try
                {
                    if (sid.GetElement($"slotgrid-{index}") == null) break;

                    ElementBounds bounds2 = sid.GetSlotGridExcl($"slotgrid-{index}").Bounds;
                    ElementBounds bounds3 = sid.GetRichtext($"category-{index}").Bounds;
                    bounds3.fixedY = bounds.fixedY + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * _rows[index];
                    bounds2.fixedY = bounds.fixedY + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * (_rows[index] + 0.4);
                    bounds2.CalcWorldBounds();
                    bounds3.CalcWorldBounds();
                    bounds = bounds2;
                    index++;
                }
                catch
                {
                    break;
                }
            }
        }

        return false;
    }

    private static List<int> _rows = [];



    private static void registerArmorIcons()
    {
        _api.Gui.Icons.CustomIcons["armorhead"] = _api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-helmet.svg"));
        _api.Gui.Icons.CustomIcons["armorbody"] = _api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-body.svg"));
        _api.Gui.Icons.CustomIcons["armorlegs"] = _api.Gui.Icons.SvgIconSource(new AssetLocation("textures/icons/character/armor-legs.svg"));
    }
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
    private static void ComposeBackpackSlots(GuiComposer composer, GuiDialogInventory __instance, IInventory? backPackInv, ElementBounds fullGridBounds)
    {
        if (backPackInv == null) return;

        _rows.Clear();

        List<int> generalSlots = [];
        List<(float order, string category, List<int> indexes)> specialSlots = [];

        for (int slotIndex = 4; slotIndex < backPackInv.Count; slotIndex++)
        {
            ItemSlot slot = backPackInv[slotIndex];
            if (slot is IHasSlotBackpackCategory slotWithCategory && slotWithCategory.BackpackCategoryCode != "")
            {
                bool categoryExists = false;
                foreach ((float order, string category, List<int> indexes) in specialSlots)
                {
                    if (category == slotWithCategory.BackpackCategoryCode)
                    {
                        indexes.Add(slotIndex);
                        categoryExists = true;
                        break;
                    }
                }
                if (!categoryExists)
                {
                    specialSlots.Add((slotWithCategory.OrderPriority, slotWithCategory.BackpackCategoryCode, [slotIndex]));
                }
            }
            else
            {
                generalSlots.Add(slotIndex);
            }
        }

        specialSlots.Sort((a, b) => Math.Sign(b.order - a.order));

        ElementBounds generalGridBounds = fullGridBounds;

        composer.AddItemSlotGridExcl(backPackInv, (data) => SendInvPacket(__instance, data), 6, GetInvertedIndexes(generalSlots, backPackInv.Count), generalGridBounds, "slotgrid");

        _rows.Add((int)Math.Ceiling(generalSlots.Count / 6f));

        ElementBounds specialGridBounds = fullGridBounds.FlatCopy();
        for (int categoryIndex = 0; categoryIndex < specialSlots.Count; categoryIndex++)
        {
            _rows.Add((int)Math.Ceiling(specialSlots[categoryIndex].indexes.Count / 6f));

            double Y = specialGridBounds.fixedY + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * _rows[categoryIndex];

            specialGridBounds = fullGridBounds.FlatCopy();
            specialGridBounds.fixedY = Y;

            composer.AddRichtext(Lang.Get($"slotcategory-{specialSlots[categoryIndex].category}"), CairoFont.WhiteSmallText().WithFontSize(14), specialGridBounds, $"category-{categoryIndex}");

            specialGridBounds = fullGridBounds.FlatCopy();
            specialGridBounds.fixedY = Y + (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding) * 0.4;

            composer.AddItemSlotGridExcl(
                backPackInv,
                (data) => SendInvPacket(__instance, data),
                6,
                GetInvertedIndexes(specialSlots[categoryIndex].indexes, backPackInv.Count),
                specialGridBounds,
                $"slotgrid-{categoryIndex}");
        }
    }
    private static int[] GetInvertedIndexes(List<int> indexes, int total)
    {
        List<int> result = [];
        for (int index = 0; index < total; index++)
        {
            if (!indexes.Contains(index))
            {
                result.Add(index);
            }
        }
        return result.ToArray();
    }

    private static void CloseIconPressed(GuiDialogInventory __instance) => GuiDialogInventory_CloseIconPressed?.Invoke(__instance, []);
    private static void OnNewScrollbarvalue(GuiDialogInventory __instance, float data) => GuiDialogInventory_OnNewScrollbarvalue?.Invoke(__instance, [data]);
    private static void SendInvPacket(GuiDialogInventory __instance, object data) => GuiDialogInventory_SendInvPacket?.Invoke(__instance, [data]);
}
