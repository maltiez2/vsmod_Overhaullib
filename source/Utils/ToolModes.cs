using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static OpenTK.Graphics.OpenGL.GL;

namespace CombatOverhaul.Utils;

public static class ToolModesUtils
{
    public static SkillItem[] GetModesFromSlots(ICoreClientAPI api, IEnumerable<ItemSlot> slots, System.Func<ItemSlot, string> descriptionGetter)
    {
        List<SkillItem> modes = new();
        int index = 0;
        foreach (ItemSlot slot in slots)
        {
            if (slot.Itemstack == null) continue;
            modes.Add(new SkillItem
            {
                Code = new AssetLocation($"{slot.Itemstack.Collectible.Code}-{index++}"),
                Name = descriptionGetter(slot),
                Data = slot
            });

            modes[^1].RenderHandler = GetItemStackRenderCallback(slot, api, ColorUtil.WhiteArgb);
        }

        return modes.ToArray();
    }
    public static RenderSkillItemDelegate GetItemStackRenderCallback(ItemSlot slot, ICoreClientAPI clientApi, int color)
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
    public static SkillItem GetSkillItemWithItemStack(ICoreClientAPI clientApi, ItemStack? stack, string icon, string color)
    {
        if (stack == null)
        {
            return GetSkillItemWithIcon(clientApi, icon, color);
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
            RenderHandler = GetItemStackRenderCallback(temporarySlot, clientApi, ColorUtil.WhiteArgb)
        };
    }
}
