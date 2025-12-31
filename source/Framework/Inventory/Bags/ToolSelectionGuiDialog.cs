using Cairo;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Armor;

public class ToolSelectionGuiDialog : GuiDialog
{
    public ToolSelectionGuiDialog(ICoreClientAPI api, ToolBagSelectionSystemClient system) : base(api)
    {
        Api = api;
        System = system;
    }

    public override string ToggleKeyCombinationCode => "";

    public override void OnGuiOpened()
    {
        bool successful = false;

        try
        {
            successful = ComposeDialog();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            successful = false;
        }

        if (!successful)
        {
            //TryClose();
        }
    }

    protected readonly ICoreClientAPI Api;
    protected readonly ToolBagSelectionSystemClient System;

    protected virtual bool ComposeDialog()
    {
        ClearComposers();

        IEnumerable<ToolSlotData> slots = System.GetToolSlots();
        List<SkillItem> skillItems = slots.Select(config => GetSkillItem(config.Stack, config.Icon, config.Color)).ToList();

        if (skillItems.Count == 0) return false;

        ElementBounds mainBounds = ElementStdBounds.AutosizedMainDialog.BelowCopy(fixedDeltaY: 100);
        ElementBounds skillGridBounds = ElementBounds.Fixed(20, 20, 100, 100);

        mainBounds = mainBounds.WithChild(skillGridBounds);

        SingleComposer = Api.Gui.CreateCompo("combatoverhaul:toolselection", mainBounds);

        SingleComposer.AddShadedDialogBG(ElementStdBounds.DialogBackground().WithFixedPadding(GuiStyle.ElementToDialogPadding / 2), false);
        SingleComposer.BeginChildElements();

        ComposeSkillItemRow(slots, "all", skillGridBounds);

        SingleComposer.EndChildElements();
        SingleComposer.Compose();

        return true;
    }
    protected void ComposeSkillItemRow(IEnumerable<ToolSlotData> slots, string code, ElementBounds bounds)
    {
        List<SkillItem> skillItems = [];
        List<ToolSlotData> slotsData = [];

        foreach (ToolSlotData data in slots)
        {
            skillItems.Add(GetSkillItem(data.Stack, data.Icon, data.Color));
            slotsData.Add(data);
        }

        SingleComposer.AddSkillItemGrid(skillItems, skillItems.Count, 1, (index) => OnSkillItemPressed(slotsData[index]), bounds, $"skill-item-grid-{code}");
    }
    protected void OnSkillItemPressed(ToolSlotData data)
    {
        System.TriggerSlots([data]);
    }
    protected SkillItem GetSkillItem(ItemStack? stack, string icon, string color)
    {
        if (stack == null)
        {
            return GetSkillItemWithIcon(Api, icon, color);
        }
        
        DummySlot temporarySlot = new(stack)
        {
            BackgroundIcon = icon
        };

        return new SkillItem
        {
            Code = "",
            Name = "",
            Data = temporarySlot,
            RenderHandler = GetItemStackRenderCallback(temporarySlot, Api, ColorUtil.WhiteArgb)
        };
    }
    protected static RenderSkillItemDelegate GetItemStackRenderCallback(ItemSlot slot, ICoreClientAPI clientApi, int color)
    {
        return (AssetLocation code, float dt, double posX, double posY) =>
        {
            double size = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
            double scaledSize = GuiElement.scaled(size - 5);

            clientApi?.Render.RenderItemstackToGui(
                slot,
                posX + (scaledSize / 2),
                posY + (scaledSize / 2),
                100,
                (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize),
                color,
                showStackSize: true);
        };
    }

    public static SkillItem GetSkillItemWithIcon(ICoreClientAPI clientApi, string iconCode, string color)
    {
        SkillItem item = new();
        double[] colorArray = ColorUtil.Hex2Doubles(color);
        item.Texture = clientApi.Gui.Icons.GenTexture(48, 48, delegate (Context ctx, ImageSurface surface)
        {
            clientApi.Gui.Icons.DrawIcon(ctx, iconCode, 5.0, 5.0, 38.0, 38.0, colorArray);
        });
        return item;
    }
}
