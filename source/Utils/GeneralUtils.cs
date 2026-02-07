using CombatOverhaul.Vanity;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace CombatOverhaul.Utils;

public static class GeneralUtils
{
    public const string ItemStackIdAttribute = "item-stack-uid";

    public static long MarkItemStack(ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack;

        if (stack == null)
        {
            return 0;
        }

        if (stack.Attributes == null)
        {
            stack.Attributes = new TreeAttribute();
        }

        long? id = stack.Attributes.TryGetLong(ItemStackIdAttribute);
        if (id == null)
        {
            long newId = DateTime.UtcNow.Ticks;
            stack.Attributes.SetLong(ItemStackIdAttribute, newId);
            slot.MarkDirty();
            id = newId;
        }

        return id.Value;
    }

    public static long GetItemMark(ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack;

        if (stack == null)
        {
            return 0;
        }

        if (stack.Attributes == null)
        {
            return 0;
        }

        long? id = stack.Attributes.TryGetLong(ItemStackIdAttribute);
        if (id == null)
        {
            return 0;
        }

        return id.Value;
    }

    public static InventoryPlayerBackPacks? GetBackpackInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }

    public static InventoryCharacter? GetCharacterInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName) as InventoryCharacter;
    }

    public static VanityInventory? GetVanityInventory(IPlayer? player)
    {
        return player?.InventoryManager.GetOwnInventory(CombatOverhaulSystem.VanityInventoryCode) as VanityInventory;
    }
}
