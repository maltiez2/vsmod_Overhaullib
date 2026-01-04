using CombatOverhaul.Armor;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

public enum DamageReceivedCalculationType
{
    OnlyArmor,
    WithBodyParts,
    HitChance,
    Average,
    TypesNumber
}

internal static class GuiDialogPatches
{
    public static GuiDialogInventory? GuiDialogInventoryInstance { get; set; }

    public static void Patch(string harmonyId, ICoreClientAPI api)
    {
        Api = api;

        new Harmony(harmonyId).Patch(
                typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CharacterTabPatch), nameof(CharacterTabPatch.GuiDialogCharacter_ComposeCharacterTab)))
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

        new Harmony(harmonyId).Patch(
                typeof(CharacterSystem).GetMethod("StartClientSide", AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(GuiDialogPatches), nameof(CharacterSystem_StartClientSide)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogCharacter).GetMethod("ComposeCharacterTab", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogCharacter).GetMethod("OnRenderGUI", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogInventory).GetMethod("ComposeSurvivalInvDialog", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(GuiDialogInventory).GetMethod("OnNewScrollbarvalue", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(CharacterSystem).GetMethod("StartClientSide", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);

        Api = null;
    }

    public static void RecalculateArmorStatsForGui()
    {
        try
        {
            _recalcualteArmorStats?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    public static ICoreClientAPI? Api { get; set; }

    private static int _lastItemId = 0;
    private static bool _anySlotsHighlighted = false;
    private static List<int> _rows = [];
    private static ElementBounds? _childBounds;
    private static int _currentAttackTier = 1;
    private static readonly Dictionary<DamageZone, string> _zonesStatsTextIds = new()
    {
        { DamageZone.Head, "textHeadStats" },
        { DamageZone.Face, "textFaceStats" },
        { DamageZone.Neck, "textNeckStats" },
        { DamageZone.Torso, "textTorsoStats" },
        { DamageZone.Arms, "textArmsStats" },
        { DamageZone.Hands, "textHandsStats" },
        { DamageZone.Legs, "textLegsStats" },
        { DamageZone.Feet, "textFeetStats" }
    };
    private static Action? _recalcualteArmorStats;
    private static DamageReceivedCalculationType _calculationType = DamageReceivedCalculationType.OnlyArmor;

    // GuiDialogInventory
    private static FieldInfo? GuiDialogInventory_survivalInvDialog = typeof(GuiDialogInventory).GetField("survivalInvDialog", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogInventory_prevRows = typeof(GuiDialogInventory).GetField("prevRows", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogInventory_backPackInv = typeof(GuiDialogInventory).GetField("backPackInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? GuiDialogInventory_craftingInv = typeof(GuiDialogInventory).GetField("craftingInv", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogInventory_CloseIconPressed = typeof(GuiDialogInventory).GetMethod("CloseIconPressed", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogInventory_SendInvPacket = typeof(GuiDialogInventory).GetMethod("SendInvPacket", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo? GuiDialogInventory_OnNewScrollbarvalue = typeof(GuiDialogInventory).GetMethod("OnNewScrollbarvalue", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo? CharacterSystem_charDlg = typeof(CharacterSystem).GetField("charDlg", BindingFlags.NonPublic | BindingFlags.Instance);

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

        if (Api.Settings.Bool["immersiveMouseMode"])
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

        GuiComposer survivalInvDialog = Api.Gui.CreateCompo("inventory-backpack", dialogBounds);
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
        if (!__instance.IsOpened()) return true;

        GuiComposer? sid = (GuiComposer?)GuiDialogInventory_survivalInvDialog.GetValue(__instance);

        if (Api.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            return true;
        }
        else if (Api.World.Player.WorldData.CurrentGameMode == EnumGameMode.Survival && sid != null)
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

    private static void CharacterSystem_StartClientSide(CharacterSystem __instance, ICoreClientAPI api)
    {
        if (!api.ModLoader.IsModEnabled("combatoverhaul")) return;

        GuiDialogCharacterBase? dialog = (GuiDialogCharacterBase?)CharacterSystem_charDlg?.GetValue(__instance);
        if (dialog == null) return;

        dialog.Tabs.Add(new GuiTab() { Name = Lang.Get("charactertab-armor"), DataInt = dialog.Tabs.Count });
        dialog.RenderTabHandlers.Add(composeArmorTab);
    }

    private static void composeArmorTab(GuiComposer compo)
    {
        if (!ArmorInventory._disableVanillaArmorSlots)
        {
            return;
        }

        double gap = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        double textGap = gap;
        double bgPadding = GuiElement.scaled(9);
        double textWidth = 60;

        IInventory _inv = Api.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (_inv is not ArmorInventory inv)
        {
            return;
        }
        CairoFont textFont = CairoFont.WhiteSmallText();
        CairoFont textFontMiddle = CairoFont.WhiteSmallText();
        CairoFont buttonFont = CairoFont.ButtonText();
        textFont.Orientation = EnumTextOrientation.Right;
        textFontMiddle.Orientation = EnumTextOrientation.Center;

        int height = 356 - 50 - 30;

        ElementBounds topInsetBounds = ElementBounds.Fixed(-0, 22, 414, 80);
        ElementBounds tierTextBounds = ElementBounds.Fixed(-0, 22, 80, 40);
        ElementBounds tierSliderBounds = ElementBounds.Fixed(-0, 22, 130, 40).RightOf(tierTextBounds, 4);
        ElementBounds button1Bounds = ElementBounds.Fixed(0, 22, 194, 36).FixedRightOf(tierSliderBounds, 84).WithFixedOffset(0, 2);
        ElementBounds button1TextBounds = button1Bounds.FlatCopy();
        ElementBounds leftLegendTextBounds = ElementBounds.Fixed(0, 0, 80, 40).FixedUnder(button1Bounds, 4);
        ElementBounds middleLegendTextBounds = ElementBounds.Fixed(0, 0, 120, 40).FixedUnder(button1Bounds, 4).RightOf(leftLegendTextBounds, 4);
        ElementBounds rightLegendTextBounds = ElementBounds.Fixed(0, 0, 190, 40).FixedUnder(button1Bounds, 4).RightOf(middleLegendTextBounds, 12);

        ElementBounds outerInsetBounds = ElementBounds.Fixed(-0, 0/*50 + 22*/, 414, height).FixedUnder(topInsetBounds, 8);
        ElementBounds baseBounds = ElementBounds.Fixed(0, -52, 388, height).FixedUnder(topInsetBounds, 8);
        ElementBounds childBounds = ElementBounds.Fixed(0, 2, 388, height + 140);
        ElementBounds textBounds = ElementStdBounds.Slot(0, 0).WithFixedWidth(textWidth).WithFixedAlignmentOffset(0, 12);
        ElementBounds slot0Bounds = ElementStdBounds.Slot(textBounds.RightCopy(8).fixedX, textBounds.RightCopy().fixedY);
        ElementBounds slot1Bounds = ElementStdBounds.Slot(slot0Bounds.RightCopy(gap).fixedX, slot0Bounds.RightCopy().fixedY);
        ElementBounds slot2Bounds = ElementStdBounds.Slot(slot1Bounds.RightCopy(gap).fixedX, slot1Bounds.RightCopy().fixedY);
        ElementBounds statsText1Bounds = ElementStdBounds.Slot(slot2Bounds.RightCopy(gap).fixedX, slot2Bounds.RightCopy().fixedY).WithFixedWidth(170 / 3).WithFixedAlignmentOffset(0, 12);
        ElementBounds statsText2Bounds = statsText1Bounds.RightCopy();
        ElementBounds statsText3Bounds = statsText2Bounds.RightCopy();


        ElementBounds scrollbarBounds = baseBounds.CopyOffsetedSibling(baseBounds.fixedWidth + 7, -6, 0, 0).WithFixedWidth(20);

        ElementBounds clipBounds = baseBounds.FlatCopy().FixedGrow(6, 11).WithFixedOffset(0, -6).WithFixedHeight(height);

        compo.AddInset(topInsetBounds, 2);
        compo.AddDynamicText("", textFont, tierTextBounds.WithFixedOffset(0, 10), "textTier");
        compo.AddHoverText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-tier-hint"), CairoFont.WhiteSmallText(), 400, tierTextBounds.FlatCopy().WithFixedOffset(0, -10));
        compo.AddSlider(value => { _currentAttackTier = value; SetStatsValues(compo, Api.World.Player, value); return true; }, tierSliderBounds, "tierSlider");

        compo.AddButton("", () => ToggleBodyPartsMultipliers(compo), button1Bounds, textFont, EnumButtonStyle.Small, "button1");
        compo.AddDynamicText(GetCalcTypeText(), textFontMiddle, button1TextBounds.WithFixedOffset(0, 8), "textButton");
        compo.AddHoverText(GetCalcTypeHoverText(), CairoFont.WhiteSmallText(), 600, button1TextBounds.FlatCopy().WithFixedOffset(0, -10));

        compo.AddDynamicText("", textFontMiddle, leftLegendTextBounds.WithFixedOffset(0, 10), "textLeftLegend");
        compo.AddHoverText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-legend-left-hint"), CairoFont.WhiteSmallText(), 400, leftLegendTextBounds.FlatCopy().WithFixedOffset(0, -10));
        compo.AddDynamicText("", textFontMiddle, middleLegendTextBounds.WithFixedOffset(0, 10), "textMiddleLegend");
        compo.AddHoverText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-legend-middle-hint"), CairoFont.WhiteSmallText(), 400, middleLegendTextBounds.FlatCopy().RightOf(leftLegendTextBounds, 12).WithFixedOffset(0, -10));
        compo.AddDynamicText("", textFontMiddle, rightLegendTextBounds.WithFixedOffset(0, 10), "textRightLegend");
        compo.AddHoverText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-legend-right-hint"), CairoFont.WhiteSmallText(), 500, rightLegendTextBounds.FlatCopy().RightOf(middleLegendTextBounds, 12).WithFixedOffset(0, -10));


        compo.AddInset(outerInsetBounds, 2);

        compo.BeginChildElements(baseBounds);
        compo.BeginClip(clipBounds);
        compo.BeginChildElements(childBounds);

        compo.AddDynamicText("", textFont, textBounds, "textHead");
        compo.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textFace");
        compo.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textNeck");
        compo.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textTorso");
        compo.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textArms");
        compo.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textHands");
        compo.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textLegs");
        compo.AddDynamicText("", textFont, BelowCopySet(ref textBounds, fixedDeltaY: textGap), "textFeet");

        AddSlotHere(compo, inv, ArmorLayers.Outer, DamageZone.Head, ref slot0Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Outer, DamageZone.Face, ref slot0Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Outer, DamageZone.Neck, ref slot0Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Outer, DamageZone.Torso, ref slot0Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Outer, DamageZone.Arms, ref slot0Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Outer, DamageZone.Hands, ref slot0Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Outer, DamageZone.Legs, ref slot0Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Outer, DamageZone.Feet, ref slot0Bounds, gap);

        AddSlotHere(compo, inv, ArmorLayers.Middle, DamageZone.Head, ref slot1Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Middle, DamageZone.Face, ref slot1Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Middle, DamageZone.Neck, ref slot1Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Middle, DamageZone.Torso, ref slot1Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Middle, DamageZone.Arms, ref slot1Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Middle, DamageZone.Hands, ref slot1Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Middle, DamageZone.Legs, ref slot1Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Middle, DamageZone.Feet, ref slot1Bounds, gap);

        AddSlotHere(compo, inv, ArmorLayers.Skin, DamageZone.Head, ref slot2Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Skin, DamageZone.Face, ref slot2Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Skin, DamageZone.Neck, ref slot2Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Skin, DamageZone.Torso, ref slot2Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Skin, DamageZone.Arms, ref slot2Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Skin, DamageZone.Hands, ref slot2Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Skin, DamageZone.Legs, ref slot2Bounds, gap);
        AddSlot(compo, inv, ArmorLayers.Skin, DamageZone.Feet, ref slot2Bounds, gap);

        compo.AddDynamicText("", textFontMiddle, statsText1Bounds, "textHeadStats1");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText1Bounds, fixedDeltaY: textGap), "textFaceStats1");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText1Bounds, fixedDeltaY: textGap), "textNeckStats1");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText1Bounds, fixedDeltaY: textGap), "textTorsoStats1");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText1Bounds, fixedDeltaY: textGap), "textArmsStats1");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText1Bounds, fixedDeltaY: textGap), "textHandsStats1");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText1Bounds, fixedDeltaY: textGap), "textLegsStats1");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText1Bounds, fixedDeltaY: textGap), "textFeetStats1");

        compo.AddDynamicText("", textFontMiddle, statsText2Bounds, "textHeadStats2");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText2Bounds, fixedDeltaY: textGap), "textFaceStats2");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText2Bounds, fixedDeltaY: textGap), "textNeckStats2");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText2Bounds, fixedDeltaY: textGap), "textTorsoStats2");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText2Bounds, fixedDeltaY: textGap), "textArmsStats2");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText2Bounds, fixedDeltaY: textGap), "textHandsStats2");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText2Bounds, fixedDeltaY: textGap), "textLegsStats2");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText2Bounds, fixedDeltaY: textGap), "textFeetStats2");

        compo.AddDynamicText("", textFontMiddle, statsText3Bounds, "textHeadStats3");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText3Bounds, fixedDeltaY: textGap), "textFaceStats3");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText3Bounds, fixedDeltaY: textGap), "textNeckStats3");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText3Bounds, fixedDeltaY: textGap), "textTorsoStats3");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText3Bounds, fixedDeltaY: textGap), "textArmsStats3");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText3Bounds, fixedDeltaY: textGap), "textHandsStats3");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText3Bounds, fixedDeltaY: textGap), "textLegsStats3");
        compo.AddDynamicText("", textFontMiddle, BelowCopySet(ref statsText3Bounds, fixedDeltaY: textGap), "textFeetStats3");

        compo.EndChildElements();
        compo.EndClip();
        compo.AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar");
        compo.EndChildElements();

        compo.GetSlider("tierSlider").SetValues(_currentAttackTier, 0, 12, 1, unit: "");

        compo.GetDynamicText("textTier")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-tier"));
        compo.GetDynamicText("textLeftLegend")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-legend-left"));
        compo.GetDynamicText("textMiddleLegend")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-legend-middle"));
        compo.GetDynamicText("textRightLegend")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-attack-legend-right"));

        compo.GetDynamicText("textHead")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-head"));
        compo.GetDynamicText("textFace")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-face"));
        compo.GetDynamicText("textNeck")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-neck"));
        compo.GetDynamicText("textTorso")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-torso"));
        compo.GetDynamicText("textArms")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-arms"));
        compo.GetDynamicText("textHands")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-hands"));
        compo.GetDynamicText("textLegs")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-legs"));
        compo.GetDynamicText("textFeet")?.SetNewText(Lang.Get("combatoverhaul:armor-inventory-dialog-feet"));

        compo.GetDynamicText("textHeadStats1")?.SetNewText("0%");
        compo.GetDynamicText("textFaceStats1")?.SetNewText("0%");
        compo.GetDynamicText("textNeckStats1")?.SetNewText("0%");
        compo.GetDynamicText("textTorsoStats1")?.SetNewText("0%");
        compo.GetDynamicText("textArmsStats1")?.SetNewText("0%");
        compo.GetDynamicText("textHandsStats1")?.SetNewText("0%");
        compo.GetDynamicText("textLegsStats1")?.SetNewText("0%");
        compo.GetDynamicText("textFeetStats1")?.SetNewText("0%");

        compo.GetDynamicText("textHeadStats2")?.SetNewText("0%");
        compo.GetDynamicText("textFaceStats2")?.SetNewText("0%");
        compo.GetDynamicText("textNeckStats2")?.SetNewText("0%");
        compo.GetDynamicText("textTorsoStats2")?.SetNewText("0%");
        compo.GetDynamicText("textArmsStats2")?.SetNewText("0%");
        compo.GetDynamicText("textHandsStats2")?.SetNewText("0%");
        compo.GetDynamicText("textLegsStats2")?.SetNewText("0%");
        compo.GetDynamicText("textFeetStats2")?.SetNewText("0%");

        compo.GetDynamicText("textHeadStats3")?.SetNewText("0%");
        compo.GetDynamicText("textFaceStats3")?.SetNewText("0%");
        compo.GetDynamicText("textNeckStats3")?.SetNewText("0%");
        compo.GetDynamicText("textTorsoStats3")?.SetNewText("0%");
        compo.GetDynamicText("textArmsStats3")?.SetNewText("0%");
        compo.GetDynamicText("textHandsStats3")?.SetNewText("0%");
        compo.GetDynamicText("textLegsStats3")?.SetNewText("0%");
        compo.GetDynamicText("textFeetStats3")?.SetNewText("0%");

        compo.GetScrollbar("scrollbar").SetHeights(
            height,
            height + 150
        );
        compo.GetScrollbar("scrollbar").SetScrollbarPosition(0);

        SetStatsValues(compo, Api.World.Player, _currentAttackTier);
        _recalcualteArmorStats = () => SetStatsValues(compo, Api.World.Player, _currentAttackTier);

        _childBounds = childBounds;
    }
    private static void AddSlotHere(GuiComposer compo, ArmorInventory inv, ArmorLayers layers, DamageZone zone, ref ElementBounds bounds, double gap)
    {
        int slotIndex = ArmorInventory.IndexFromArmorType(layers, zone);
        bool available = inv.IsSlotAvailable(slotIndex) || !inv[slotIndex].Empty;
        compo.AddItemSlotGrid(inv, SendInvPacket, 1, [slotIndex], bounds);
    }
    private static void AddSlot(GuiComposer compo, ArmorInventory inv, ArmorLayers layers, DamageZone zone, ref ElementBounds bounds, double gap)
    {
        int slotIndex = ArmorInventory.IndexFromArmorType(layers, zone);
        bool available = inv.IsSlotAvailable(slotIndex) || !inv[slotIndex].Empty;
        compo.AddItemSlotGrid(inv, SendInvPacket, 1, [slotIndex], BelowCopySet(ref bounds, fixedDeltaY: gap));
    }

    private static void SendInvPacket(object packet)
    {
        Api.Network.SendPacketClient(packet);
    }
    private static ElementBounds BelowCopySet(ref ElementBounds bounds, double fixedDeltaX = 0.0, double fixedDeltaY = 0.0, double fixedDeltaWidth = 0.0, double fixedDeltaHeight = 0.0)
    {
        return bounds = bounds.BelowCopy(fixedDeltaX, fixedDeltaY, fixedDeltaWidth, fixedDeltaHeight);
    }
    private static void OnNewScrollbarValue(float value)
    {
        if (_childBounds != null)
        {
            _childBounds.fixedY = 2 - value;
            _childBounds.CalcWorldBounds();
        }
    }
    
    private static void OnRenderGUI()
    {
        int currentItem = Api?.World.Player.InventoryManager.MouseItemSlot?.Itemstack?.Item?.ItemId ?? 0;

        if (_anySlotsHighlighted && currentItem == 0 && _lastItemId == 0)
        {
            InventoryCharacter? inventory2 = GeneralUtils.GetCharacterInventory(Api?.World.Player);
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

        InventoryCharacter? inventory = GeneralUtils.GetCharacterInventory(Api?.World.Player);
        if (inventory == null) return;

        ItemSlot mouseSlot = Api.World.Player.InventoryManager.MouseItemSlot;

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
            else if (slot.HexBackgroundColor == "#5fbed4")
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
    private static string GetCalcTypeText()
    {
        return Lang.Get($"combatoverhaul:armor-inventory-calctype-{_calculationType}");
    }
    private static string GetCalcTypeHoverText()
    {
        StringBuilder builder = new();

        foreach (DamageReceivedCalculationType value in Enum.GetValues<DamageReceivedCalculationType>().Where(value => value != DamageReceivedCalculationType.TypesNumber))
        {
            builder.AppendLine(Lang.Get($"combatoverhaul:armor-inventory-calctype-hint-{value}"));
            builder.AppendLine();
        }

        return builder.ToString();
    }
    private static void SetStatsValues(GuiComposer composer, IPlayer player, int damageTier)
    {
        bool invert = _calculationType == DamageReceivedCalculationType.OnlyArmor;

        foreach ((DamageZone zone, string textId) in _zonesStatsTextIds)
        {
            float piercing = PlayerDamageModelBehavior.GetDamageReductionFactor(player, zone, EnumDamageType.PiercingAttack, damageTier, _calculationType);
            float blunt = PlayerDamageModelBehavior.GetDamageReductionFactor(player, zone, EnumDamageType.BluntAttack, damageTier, _calculationType);
            float slash = PlayerDamageModelBehavior.GetDamageReductionFactor(player, zone, EnumDamageType.SlashingAttack, damageTier, _calculationType);

            if (invert)
            {
                piercing = 1 - piercing;
                blunt = 1 - blunt;
                slash = 1 - slash;
            }

            switch (_calculationType)
            {
                case DamageReceivedCalculationType.OnlyArmor:
                case DamageReceivedCalculationType.WithBodyParts:
                    composer.GetDynamicText(textId + "1")?.SetNewText($"{piercing:P0}");
                    composer.GetDynamicText(textId + "2")?.SetNewText($"{slash:P0}");
                    composer.GetDynamicText(textId + "3")?.SetNewText($"{blunt:P0}");
                    break;
                case DamageReceivedCalculationType.Average:
                    composer.GetDynamicText(textId + "1")?.SetNewText($"{piercing:P1}");
                    composer.GetDynamicText(textId + "2")?.SetNewText($"{slash:P1}");
                    composer.GetDynamicText(textId + "3")?.SetNewText($"{blunt:P1}");
                    break;
                case DamageReceivedCalculationType.HitChance:
                    composer.GetDynamicText(textId + "1")?.SetNewText($"");
                    composer.GetDynamicText(textId + "2")?.SetNewText($"{slash:P1}");
                    composer.GetDynamicText(textId + "3")?.SetNewText($"");
                    break;
                default:
                    break;
            }
        }
    }
    private static bool ToggleBodyPartsMultipliers(GuiComposer composer)
    {
        _calculationType = (DamageReceivedCalculationType)((int)(_calculationType + 1) % (int)DamageReceivedCalculationType.TypesNumber);
        composer.GetDynamicText("textButton")?.SetNewText(GetCalcTypeText());
        SetStatsValues(composer, Api.World.Player, _currentAttackTier);
        return false;
    }

    private static void CloseIconPressed(GuiDialogInventory __instance) => GuiDialogInventory_CloseIconPressed?.Invoke(__instance, []);
    private static void OnNewScrollbarvalue(GuiDialogInventory __instance, float data) => GuiDialogInventory_OnNewScrollbarvalue?.Invoke(__instance, [data]);
    private static void SendInvPacket(GuiDialogInventory __instance, object data) => GuiDialogInventory_SendInvPacket?.Invoke(__instance, [data]);
}
