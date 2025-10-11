using ConfigLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Armor;

public class ItemSlotBagContentWithWildcardMatch : ItemSlotBagContent, IHasSlotBackpackCategory
{
    public ItemStack SourceBag { get; set; }
    public SlotConfig Config { get; set; } = new([], []);
    public string BackpackCategoryCode => Config.BackpackCategoryCode;
    public float OrderPriority => Config.OrderPriority;

    public ItemSlotBagContentWithWildcardMatch(InventoryBase inventory, int BagIndex, int SlotIndex, EnumItemStorageFlags storageType, ItemStack sourceBag, string? color = null) : base(inventory, BagIndex, SlotIndex, storageType)
    {
        HexBackgroundColor = color;
        SourceBag = sourceBag;
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        if (!CanHold(sourceSlot))
        {
            return false;
        }

        return base.CanTakeFrom(sourceSlot, priority);
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (base.CanHold(sourceSlot) && sourceSlot?.Itemstack?.Collectible?.Code != null)
        {
            bool matchWithoutDomain = WildcardUtil.Match(Config.CanHoldWildcards, sourceSlot.Itemstack.Collectible.Code.Path);
            bool matchWithDomain = WildcardUtil.Match(Config.CanHoldWildcards, sourceSlot.Itemstack.Collectible.Code.ToString());

            bool matchWithTags = false;
            if (sourceSlot.Itemstack?.Item != null && Config.CanHoldItemTags.Length != 0)
            {
                matchWithTags = ItemTagRule.ContainsAllFromAtLeastOne(sourceSlot.Itemstack.Item.Tags, Config.CanHoldItemTags);
            }
            if (sourceSlot.Itemstack?.Block != null && Config.CanHoldBlockTags.Length != 0 && !matchWithTags)
            {
                matchWithTags = BlockTagRule.ContainsAllFromAtLeastOne(sourceSlot.Itemstack.Block.Tags, Config.CanHoldBlockTags);
            }

            return matchWithoutDomain || matchWithDomain || matchWithTags;
        }

        return false;
    }
}

public class ItemSlotToolHolder : ItemSlotBagContentWithWildcardMatch
{
    public string ToolBagId { get; set; }
    public bool MainHand { get; set; } = true;

    public ItemSlotToolHolder(InventoryBase inventory, int BagIndex, int SlotIndex, EnumItemStorageFlags storageType, ItemStack sourceBag, string? color = null) : base(inventory, BagIndex, SlotIndex, storageType, sourceBag, color)
    {
        ToolBagId = sourceBag.Item?.Code?.ToString() ?? "";
    }
}

public class ItemSlotTakeOutOnly : ItemSlotBagContent, IHasSlotBackpackCategory
{
    public string ToolBagId { get; set; }
    public bool CanHoldNow { get; set; } = false;
    public bool MainHand { get; set; } = true;
    public string BackpackCategoryCode { get; set; } = "takeout";
    public float OrderPriority { get; set; } = 0.1f;

    public ItemSlotTakeOutOnly(InventoryBase inventory, int BagIndex, int SlotIndex, EnumItemStorageFlags storageType, ItemStack sourceBag, string? color = null) : base(inventory, BagIndex, SlotIndex, storageType)
    {
        HexBackgroundColor = color;
        inventory.SlotModified += index => EnqueueTryEmpty(inventory, index);

        if (inventory is InventoryBasePlayer playerInventory)
        {
            InventoryBasePlayer? hotbar = playerInventory.Player?.InventoryManager?.GetHotbarInventory() as InventoryBasePlayer;

            if (hotbar != null)
            {
                hotbar.SlotModified += index => EnqueueTryEmpty(hotbar, index);
            }
        }

        ToolBagId = sourceBag.Item?.Code?.ToString() ?? "";
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => CanHoldNow;

    public override bool CanHold(ItemSlot sourceSlot) => CanHoldNow;

    protected long Callback = 0;

    protected virtual void EnqueueTryEmpty(InventoryBase inventory, int index)
    {
        if (Empty) return;

        ICoreServerAPI? api = (inventory as InventoryBasePlayer)?.Player?.Entity?.Api as ICoreServerAPI;

        if (api == null) return;

        if (Callback == 0)
        {
            Callback = api.World.RegisterCallback(_ => TryEmpty(inventory, index), 1000);
        }
    }

    protected virtual void TryEmpty(InventoryBase inventory, int index)
    {
        Callback = 0;
        if (CanHoldNow) return;
        if ((inventory as InventoryBasePlayer)?.Player?.Entity?.Api?.Side != EnumAppSide.Server) return;
        if (CanHoldNow || Empty || SlotIndex == index && Inventory.InventoryID == inventory.InventoryID) return;

        DummySlot dummySlot = new(itemstack);
        ItemSlot? targetSlot = (inventory as InventoryBasePlayer)?.Player?.InventoryManager?.GetHotbarInventory()?.GetBestSuitedSlot(dummySlot)?.slot;
        targetSlot ??= (inventory as InventoryBasePlayer)?.Player?.InventoryManager?.GetHotbarInventory().FirstOrDefault(slot => slot?.CanTakeFrom(dummySlot) == true && slot is not ItemSlotTakeOutOnly, null);
        targetSlot ??= inventory.FirstOrDefault(slot => slot?.CanTakeFrom(dummySlot) == true && slot is not ItemSlotTakeOutOnly, null);

        if (targetSlot == null) return;

        if (dummySlot.TryPutInto(inventory.Api.World, targetSlot, dummySlot.Itemstack.StackSize) > 0)
        {
            targetSlot.MarkDirty();
            itemstack = dummySlot.Itemstack?.StackSize == 0 ? null : dummySlot.Itemstack;
            MarkDirty();
        }
    }
}