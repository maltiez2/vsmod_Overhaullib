using CombatOverhaul.Utils;
using ConfigLib;
using ProtoBuf;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;


public class ItemSlotBagContentWithWildcardMatch : ItemSlotBagContent
{
    public ItemStack SourceBag { get; set; }
    public SlotConfig Config { get; set; } = new([], []);

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

public class ItemSlotTakeOutOnly : ItemSlotBagContent
{
    public string ToolBagId { get; set; }
    public bool CanHoldNow { get; set; } = false;
    public bool MainHand { get; set; } = true;

    public ItemSlotTakeOutOnly(InventoryBase inventory, int BagIndex, int SlotIndex, EnumItemStorageFlags storageType, ItemStack sourceBag, string? color = null) : base(inventory, BagIndex, SlotIndex, storageType)
    {
        HexBackgroundColor = color;
        inventory.SlotModified += index => TryEmpty(inventory, index);

        if (inventory is InventoryBasePlayer playerInventory)
        {
            InventoryBasePlayer? hotbar = playerInventory.Player?.InventoryManager?.GetHotbarInventory() as InventoryBasePlayer;

            if (hotbar != null)
            {
                hotbar.SlotModified += index => TryEmptyIntoHotbar(hotbar, index);
            }
        }

        ToolBagId = sourceBag.Item?.Code?.ToString() ?? "";
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => CanHoldNow;

    public override bool CanHold(ItemSlot sourceSlot) => CanHoldNow;

    protected virtual void TryEmptyIntoHotbar(InventoryBasePlayer inventory, int index)
    {
        if (inventory.Player.Entity?.Api?.Side != EnumAppSide.Server) return;
        if (CanHoldNow || Empty || SlotIndex == index && Inventory.InventoryID == inventory.InventoryID) return;

        DummySlot dummySlot = new(itemstack);
        ItemSlot? targetSlot = inventory.GetBestSuitedSlot(dummySlot)?.slot ?? inventory.FirstOrDefault(slot => slot?.CanTakeFrom(dummySlot) == true, null);

        if (targetSlot == null) return;

        if (dummySlot.TryPutInto(inventory.Api.World, targetSlot, dummySlot.Itemstack.StackSize) > 0)
        {
            targetSlot.MarkDirty();
            itemstack = dummySlot.Itemstack?.StackSize == 0 ? null : dummySlot.Itemstack;
            MarkDirty();
        }
    }

    protected virtual void TryEmpty(InventoryBase inventory, int index)
    {
        if ((inventory as InventoryBasePlayer)?.Player?.Entity?.Api?.Side != EnumAppSide.Server) return;
        if (CanHoldNow || Empty || SlotIndex == index && Inventory.InventoryID == inventory.InventoryID) return;

        DummySlot dummySlot = new(itemstack);
        ItemSlot? targetSlot = (inventory as InventoryBasePlayer)?.Player?.InventoryManager?.GetHotbarInventory()?.GetBestSuitedSlot(dummySlot)?.slot;
        targetSlot ??= (inventory as InventoryBasePlayer)?.Player?.InventoryManager?.GetHotbarInventory().FirstOrDefault(slot => slot?.CanTakeFrom(dummySlot) == true && slot is not ItemSlotTakeOutOnly, null);
        targetSlot ??= inventory.FirstOrDefault(slot => slot?.CanTakeFrom(dummySlot) == true && slot is not ItemSlotTakeOutOnly, null);

        if (targetSlot == null) return;

        if (TryPutInto(inventory.Api.World, targetSlot, dummySlot.Itemstack.StackSize) > 0)
        {
            targetSlot.MarkDirty();
            itemstack = dummySlot.Itemstack?.StackSize == 0 ? null : dummySlot.Itemstack;
            MarkDirty();
        }
    }
}

public class SlotConfigJson
{
    public string[] CanHoldItemTags { get; set; } = [];
    public string[] CanHoldBlockTags { get; set; } = [];
    public string[][] CanHoldItemTagsCondition { get; set; } = [];
    public string[][] CanHoldBlockTagsCondition { get; set; } = [];
    public string[] CanHoldWildcards { get; set; } = [];
    public string? SlotColor { get; set; } = null;
    public string? SlotsIcon { get; set; } = null;
    public int SlotsNumber { get; set; } = 0;

    public string SlotVariant { get; set; } = "bag_slot";
    public string SlotStateVariant { get; set; } = "bag_slot_state";
    public string EmptyStateCode { get; set; } = "empty";
    public string FullStateCode { get; set; } = "full";
    public string SlotMetalVariant { get; set; } = "bag_slot_metal";
    public string SlotLeatherVariant { get; set; } = "bag_slot_leather";
    public string SlotWoodVariant { get; set; } = "bag_slot_wood";

    public bool SetVariants { get; set; } = false;
    public bool SetMaterialVariants { get; set; } = false;

    public SlotConfig ToConfig()
    {
        if (CanHoldItemTagsCondition.Length == 0 && CanHoldItemTags.Length != 0)
        {
            CanHoldItemTagsCondition = [CanHoldItemTags];
        }

        if (CanHoldBlockTagsCondition.Length == 0 && CanHoldBlockTags.Length != 0)
        {
            CanHoldBlockTagsCondition = [CanHoldBlockTags];
        }

        return new SlotConfig(CanHoldItemTagsCondition, CanHoldBlockTagsCondition)
        {
            CanHoldWildcards = CanHoldWildcards,
            SlotColor = SlotColor,
            SlotsIcon = SlotsIcon,
            SlotsNumber = SlotsNumber,
            SlotVariant = SlotVariant,
            SlotStateVariant = SlotStateVariant,
            EmptyStateCode = EmptyStateCode,
            FullStateCode = FullStateCode,
            SlotMetalVariant = SlotMetalVariant,
            SlotLeatherVariant = SlotLeatherVariant,
            SlotWoodVariant = SlotWoodVariant,
            SetVariants = SetVariants,
            SetMaterialVariants = SetMaterialVariants
        };
    }
}

public class SlotConfig
{
    public ItemTagRule[] CanHoldItemTags { get; set; } = [];
    public BlockTagRule[] CanHoldBlockTags { get; set; } = [];
    public string[] CanHoldWildcards { get; set; } = [];
    public string? SlotColor { get; set; } = null;
    public string? SlotsIcon { get; set; } = null;
    public int SlotsNumber { get; set; } = 0;

    public string SlotVariant { get; set; } = "bag_slot";
    public string SlotStateVariant { get; set; } = "bag_slot_state";
    public string EmptyStateCode { get; set; } = "empty";
    public string FullStateCode { get; set; } = "full";
    public string SlotMetalVariant { get; set; } = "bag_slot_metal";
    public string SlotLeatherVariant { get; set; } = "bag_slot_leather";
    public string SlotWoodVariant { get; set; } = "bag_slot_wood";

    public bool SetVariants { get; set; } = false;
    public bool SetMaterialVariants { get; set; } = false;

    protected string[][] CanHoldItemTagsNames { get; set; }
    protected string[][] CanHoldBlockTagsNames { get; set; }
    protected bool Resolved { get; set; } = false;

    public SlotConfig(string[][] itemTags, string[][] blockTags)
    {
        CanHoldItemTagsNames = itemTags;
        CanHoldBlockTagsNames = blockTags;
    }

    public void Resolve(ICoreAPI api)
    {
        if (Resolved) return;
        Resolved = true;

        CanHoldItemTags = CanHoldItemTagsNames
            .Select(tags => new ItemTagRule(api, tags))
            .Where(tags => tags != ItemTagRule.Empty)
            .ToArray();
        CanHoldBlockTags = CanHoldBlockTagsNames
            .Select(tags => new BlockTagRule(api, tags))
            .Where(tags => tags != BlockTagRule.Empty)
            .ToArray();

        CanHoldItemTagsNames = [];
        CanHoldBlockTagsNames = [];
    }
}

public class GearEquipableBag : CollectibleBehavior, IHeldBag, IAttachedInteractions
{
    public SlotConfig DefaultSlotConfig { get; protected set; } = new([], []);
    public SlotConfig[] SlotConfigs { get; protected set; } = [];
    public int SlotsNumber { get; protected set; } = 0;

    protected ICoreAPI? Api;

    public GearEquipableBag(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Api = api;

        DefaultSlotConfig.Resolve(api);
        SlotConfigs.Foreach(config => config.Resolve(api));
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        SlotConfigJson? defaultSlotConfigJson = properties.AsObject<SlotConfigJson>();
        SlotConfigJson[]? slotConfigsJson = properties["slots"]?.AsObject<SlotConfigJson[]>();

        if (defaultSlotConfigJson != null)
        {
            DefaultSlotConfig = defaultSlotConfigJson.ToConfig();
        }

        if (slotConfigsJson != null)
        {
            SlotConfigs = slotConfigsJson.Select(config => config.ToConfig()).ToArray();
        }

        SlotsNumber = (DefaultSlotConfig?.SlotsNumber ?? 0) + (SlotConfigs?.Select(config => config.SlotsNumber).Sum() ?? 0);
    }

    public void Clear(ItemStack backPackStack)
    {
        ITreeAttribute? stackBackPackTree = backPackStack.Attributes.GetTreeAttribute("backpack");

        if (stackBackPackTree == null) return;

        TreeAttribute slots = new();

        for (int slotIndex = 0; slotIndex < SlotsNumber; slotIndex++)
        {
            slots["slot-" + slotIndex] = new ItemstackAttribute(null);
        }

        stackBackPackTree["slots"] = slots;
    }

    public ItemStack?[] GetContents(ItemStack bagstack, IWorldAccessor world)
    {
        ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backPackTree == null) return Array.Empty<ItemStack?>();

        List<ItemStack?> contents = new();
        ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

        foreach ((_, IAttribute attribute) in slotsTree.SortedCopy())
        {
            ItemStack? contentStack = (ItemStack?)attribute?.GetValue();

            if (contentStack != null)
            {
                contentStack.ResolveBlockOrItem(world);
            }

            contents.Add(contentStack);
        }

        return contents.ToArray();
    }

    public virtual bool IsEmpty(ItemStack bagstack)
    {
        ITreeAttribute backPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (backPackTree == null) return true;
        ITreeAttribute slotsTree = backPackTree.GetTreeAttribute("slots");

        foreach (KeyValuePair<string, IAttribute> val in slotsTree)
        {
            IItemStack stack = (IItemStack)val.Value?.GetValue();
            if (stack != null && stack.StackSize > 0) return false;
        }

        return true;
    }

    public virtual int GetQuantitySlots(ItemStack bagstack) => SlotsNumber;

    public void Store(ItemStack bagstack, ItemSlotBagContent slot)
    {
        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

        slotsTree["slot-" + slot.SlotIndex] = new ItemstackAttribute(slot.Itemstack);
    }

    public virtual string GetSlotBgColor(ItemStack bagstack)
    {
        return bagstack.ItemAttributes["backpack"]["slotBgColor"].AsString(null);
    }

    protected const int DefaultFlags = (int)(EnumItemStorageFlags.General | EnumItemStorageFlags.Agriculture | EnumItemStorageFlags.Alchemy | EnumItemStorageFlags.Jewellery | EnumItemStorageFlags.Metallurgy | EnumItemStorageFlags.Outfit);

    public virtual EnumItemStorageFlags GetStorageFlags(ItemStack bagstack)
    {
        return (EnumItemStorageFlags)DefaultFlags;
    }

    public virtual List<ItemSlotBagContent?> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
    {
        List<ItemSlotBagContent?> bagContents = new();

        EnumItemStorageFlags flags = (EnumItemStorageFlags)DefaultFlags;

        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (stackBackPackTree == null)
        {
            stackBackPackTree = new TreeAttribute();
            ITreeAttribute slotsTree = new TreeAttribute();

            int slotIndex = 0;

            for (; slotIndex < DefaultSlotConfig.SlotsNumber; slotIndex++)
            {
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, DefaultSlotConfig.SlotColor)
                {
                    Config = DefaultSlotConfig
                };
                bagContents.Add(slot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (DefaultSlotConfig.SlotsIcon != null)
                {
                    slot.BackgroundIcon = DefaultSlotConfig.SlotsIcon;
                }
            }

            foreach (SlotConfig config in SlotConfigs)
            {
                int lastIndex = slotIndex + config.SlotsNumber;

                for (; slotIndex < lastIndex; slotIndex++)
                {
                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                    {
                        Config = config
                    };
                    bagContents.Add(slot);
                    slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                    if (config.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = config.SlotsIcon;
                    }
                }
            }

            stackBackPackTree["slots"] = slotsTree;
            bagstack.Attributes["backpack"] = stackBackPackTree;
        }
        else
        {
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            foreach (KeyValuePair<string, IAttribute> val in slotsTree)
            {
                int slotIndex = int.Parse(val.Key.Split("-")[1]);

                SlotConfig config = GetSlotConfig(slotIndex);

                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                {
                    Config = config
                };

                if (config.SlotsIcon != null)
                {
                    slot.BackgroundIcon = config.SlotsIcon;
                }

                if (val.Value?.GetValue() != null)
                {
                    ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                    slot.Itemstack = attr.value;
                    slot.Itemstack.ResolveBlockOrItem(world);
                }

                while (bagContents.Count <= slotIndex) bagContents.Add(null);
                bagContents[slotIndex] = slot;
            }
        }

        return bagContents;
    }


    public void OnAttached(ItemSlot itemslot, int slotIndex, Entity toEntity, EntityAgent byEntity)
    {

    }

    public void OnDetached(ItemSlot itemslot, int slotIndex, Entity fromEntity, EntityAgent byEntity)
    {
        getOrCreateContainerWorkspace(slotIndex, fromEntity, null).Close((byEntity as EntityPlayer).Player);
    }


    public AttachedContainerWorkspace getOrCreateContainerWorkspace(int slotIndex, Entity onEntity, Action onRequireSave)
    {
        return ObjectCacheUtil.GetOrCreate(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id, () => new AttachedContainerWorkspace(onEntity, onRequireSave));
    }

    public AttachedContainerWorkspace getContainerWorkspace(int slotIndex, Entity onEntity)
    {
        return ObjectCacheUtil.TryGet<AttachedContainerWorkspace>(onEntity.Api, "att-cont-workspace-" + slotIndex + "-" + onEntity.EntityId + "-" + collObj.Id);
    }


    public virtual void OnInteract(ItemSlot bagSlot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave)
    {
        EntityControls controls = byEntity.MountedOn?.Controls ?? byEntity.Controls;
        if (!controls.Sprint)
        {
            handled = EnumHandling.PreventDefault;
            getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave).OnInteract(bagSlot, slotIndex, onEntity, byEntity, hitPosition);
        }
    }

    public void OnReceivedClientPacket(ItemSlot bagSlot, int slotIndex, Entity onEntity, IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled, Action onRequireSave)
    {
        int targetSlotIndex = packetid >> 11;

        if (slotIndex != targetSlotIndex) return;

        int first10Bits = (1 << 11) - 1;
        packetid = packetid & first10Bits;

        getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave).OnReceivedClientPacket(player, packetid, data, bagSlot, slotIndex, ref handled);
    }

    public bool OnTryAttach(ItemSlot itemslot, int slotIndex, Entity toEntity)
    {
        return true;
    }

    public bool OnTryDetach(ItemSlot itemslot, int slotIndex, Entity fromEntity)
    {
        return IsEmpty(itemslot.Itemstack);
    }

    public void OnEntityDespawn(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityDespawnData despawn)
    {
        getContainerWorkspace(slotIndex, onEntity)?.OnDespawn(despawn);
    }

    public void OnEntityDeath(ItemSlot itemslot, int slotIndex, Entity onEntity, DamageSource damageSourceForDeath)
    {
        ItemStack?[] contents = GetContents(itemslot.Itemstack, onEntity.World);
        foreach (ItemStack? stack in contents)
        {
            if (stack == null) continue;
            onEntity.World.SpawnItemEntity(stack, onEntity.Pos.XYZ);
        }
    }

    protected virtual SlotConfig GetSlotConfig(int index)
    {
        if (index < DefaultSlotConfig.SlotsNumber)
        {
            return DefaultSlotConfig;
        }

        int previousIndex = DefaultSlotConfig.SlotsNumber;
        for (int configIndex = 0; configIndex < SlotConfigs.Length; configIndex++)
        {
            previousIndex += SlotConfigs[configIndex].SlotsNumber;

            if (index < previousIndex)
            {
                return SlotConfigs[configIndex];
            }
        }

        return DefaultSlotConfig;
    }
}

public class ToolBag : GearEquipableBag
{
    public SlotConfig? MainHandSlotConfig { get; protected set; } = null;
    public SlotConfig? OffHandSlotConfig { get; protected set; } = null;

    public string? TakeOutSlotColor { get; protected set; } = null;
    public string HotkeyCode { get; protected set; } = "";
    public string HotkeyName { get; protected set; } = "";
    public GlKeys HotKeyKey { get; protected set; } = GlKeys.R;
    public int RegularSlotsNumber { get; protected set; } = 0;
    public int ToolSlotNumber { get; protected set; } = 0;
    public string? TakeOutSlotIcon { get; protected set; } = null;

    public ToolBag(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        TakeOutSlotColor = properties["takeOutColor"].AsString(null);
        HotkeyCode = properties["hotkeyCode"].AsString("");
        HotkeyName = properties["hotkeyName"].AsString("");
        HotKeyKey = Enum.Parse<GlKeys>(properties["hotkeyKey"].AsString("R"));
        TakeOutSlotIcon = properties["takeOutSlotIcon"].AsString();

        SlotConfigJson? mainHandSlotConfigJson = properties["toolSlot"]?.AsObject<SlotConfigJson>();
        SlotConfigJson? offHandSlotConfigJson = properties["offhandToolSlot"]?.AsObject<SlotConfigJson>();

        if (mainHandSlotConfigJson != null)
        {
            MainHandSlotConfig = mainHandSlotConfigJson.ToConfig();
        }

        if (offHandSlotConfigJson != null)
        {
            OffHandSlotConfig = offHandSlotConfigJson.ToConfig();
        }

        RegularSlotsNumber = SlotsNumber;

        if (mainHandSlotConfigJson != null) ToolSlotNumber += 1;
        if (offHandSlotConfigJson != null) ToolSlotNumber += 1;

        SlotsNumber += ToolSlotNumber * 2;
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Api = api;

        MainHandSlotConfig?.Resolve(api);
        OffHandSlotConfig?.Resolve(api);

        if (api is not ICoreClientAPI clientApi) return;

        ClientApi = clientApi;

        if (!clientApi.Input.HotKeys.TryGetValue(HotkeyCode, out HotKey? hotkey))
        {
            clientApi.Input.RegisterHotKey(HotkeyCode, HotkeyName, HotKeyKey);
            hotkey = clientApi.Input.HotKeys[HotkeyCode];
        }

        PreviousHotkeyHandler = hotkey.Handler;

        clientApi.Input.SetHotKeyHandler(HotkeyCode, OnHotkeyPressed);
    }

    public override List<ItemSlotBagContent?> GetOrCreateSlots(ItemStack bagstack, InventoryBase parentinv, int bagIndex, IWorldAccessor world)
    {
        List<ItemSlotBagContent?> bagContents = new();

        EnumItemStorageFlags flags = (EnumItemStorageFlags)DefaultFlags;

        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (stackBackPackTree == null)
        {
            stackBackPackTree = new TreeAttribute();
            ITreeAttribute slotsTree = new TreeAttribute();

            int slotIndex = 0;

            if (MainHandSlotConfig != null)
            {
                ItemSlotToolHolder toolSlot = new(parentinv, bagIndex, slotIndex, flags, bagstack, MainHandSlotConfig.SlotColor)
                {
                    Config = MainHandSlotConfig
                };
                toolSlot.MainHand = true;
                bagContents.Add(toolSlot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (MainHandSlotConfig.SlotsIcon != null)
                {
                    toolSlot.BackgroundIcon = MainHandSlotConfig.SlotsIcon;
                }
                slotIndex += 1;
            }

            if (OffHandSlotConfig != null)
            {
                ItemSlotToolHolder toolSlot = new(parentinv, bagIndex, slotIndex, flags, bagstack, OffHandSlotConfig.SlotColor)
                {
                    Config = OffHandSlotConfig
                };
                toolSlot.MainHand = false;
                bagContents.Add(toolSlot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (OffHandSlotConfig.SlotsIcon != null)
                {
                    toolSlot.BackgroundIcon = OffHandSlotConfig.SlotsIcon;
                }
                slotIndex += 1;
            }

            for (; slotIndex < DefaultSlotConfig.SlotsNumber + ToolSlotNumber; slotIndex++)
            {
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, DefaultSlotConfig.SlotColor)
                {
                    Config = DefaultSlotConfig
                };
                bagContents.Add(slot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (DefaultSlotConfig.SlotsIcon != null)
                {
                    slot.BackgroundIcon = DefaultSlotConfig.SlotsIcon;
                }
            }

            foreach (SlotConfig config in SlotConfigs)
            {
                int lastIndex = slotIndex + config.SlotsNumber;

                for (; slotIndex < lastIndex; slotIndex++)
                {
                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                    {
                        Config = config
                    };
                    bagContents.Add(slot);
                    slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                    if (config.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = config.SlotsIcon;
                    }
                }
            }

            if (MainHandSlotConfig != null)
            {
                ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                takeOutSLot.MainHand = true;
                bagContents.Add(takeOutSLot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (TakeOutSlotIcon != null)
                {
                    takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                }
                slotIndex += 1;
            }

            if (OffHandSlotConfig != null)
            {
                ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                takeOutSLot.MainHand = false;
                bagContents.Add(takeOutSLot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
                if (TakeOutSlotIcon != null)
                {
                    takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                }
            }

            stackBackPackTree["slots"] = slotsTree;
            bagstack.Attributes["backpack"] = stackBackPackTree;
        }
        else
        {
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            foreach (KeyValuePair<string, IAttribute> val in slotsTree)
            {
                int slotIndex = int.Parse(val.Key.Split("-")[1]);

                if (slotIndex == 0)
                {
                    if (MainHandSlotConfig != null)
                    {
                        ItemSlotToolHolder slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, MainHandSlotConfig.SlotColor)
                        {
                            Config = MainHandSlotConfig
                        };
                        slot.MainHand = true;

                        if (val.Value?.GetValue() != null)
                        {
                            ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                            slot.Itemstack = attr.value;
                            slot.Itemstack.ResolveBlockOrItem(world);
                        }

                        if (MainHandSlotConfig.SlotsIcon != null)
                        {
                            slot.BackgroundIcon = MainHandSlotConfig.SlotsIcon;
                        }

                        while (bagContents.Count <= slotIndex) bagContents.Add(null);
                        bagContents[slotIndex] = slot;
                    }
                    else if (OffHandSlotConfig != null)
                    {
                        ItemSlotToolHolder slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, OffHandSlotConfig.SlotColor)
                        {
                            Config = OffHandSlotConfig
                        };
                        slot.MainHand = false;

                        if (val.Value?.GetValue() != null)
                        {
                            ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                            slot.Itemstack = attr.value;
                            slot.Itemstack.ResolveBlockOrItem(world);
                        }

                        if (OffHandSlotConfig.SlotsIcon != null)
                        {
                            slot.BackgroundIcon = OffHandSlotConfig.SlotsIcon;
                        }

                        while (bagContents.Count <= slotIndex) bagContents.Add(null);
                        bagContents[slotIndex] = slot;
                    }
                }
                else if (slotIndex == 1 && ToolSlotNumber == 2 && OffHandSlotConfig != null)
                {
                    ItemSlotToolHolder slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, OffHandSlotConfig.SlotColor)
                    {
                        Config = OffHandSlotConfig
                    };
                    slot.MainHand = false;

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        slot.Itemstack = attr.value;
                        slot.Itemstack.ResolveBlockOrItem(world);
                    }

                    if (OffHandSlotConfig.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = OffHandSlotConfig.SlotsIcon;
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = slot;
                }
                else if (slotIndex == RegularSlotsNumber + ToolSlotNumber + (ToolSlotNumber == 2 ? 1 : 0) - (ToolSlotNumber == 2 ? 1 : 0) && MainHandSlotConfig != null)
                {
                    ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                    takeOutSLot.MainHand = true;

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        takeOutSLot.Itemstack = attr.value;
                        takeOutSLot.Itemstack.ResolveBlockOrItem(world);
                    }

                    if (TakeOutSlotIcon != null)
                    {
                        takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = takeOutSLot;
                }
                else if (slotIndex == RegularSlotsNumber + ToolSlotNumber + (ToolSlotNumber == 2 ? 1 : 0) + (ToolSlotNumber == 2 ? 1 : 0) - 1 && OffHandSlotConfig != null)
                {
                    ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);
                    takeOutSLot.MainHand = false;

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        takeOutSLot.Itemstack = attr.value;
                        takeOutSLot.Itemstack.ResolveBlockOrItem(world);
                    }

                    if (TakeOutSlotIcon != null)
                    {
                        takeOutSLot.BackgroundIcon = TakeOutSlotIcon;
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = takeOutSLot;
                }
                else
                {
                    SlotConfig config = GetSlotConfig(slotIndex - ToolSlotNumber);

                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, config.SlotColor)
                    {
                        Config = config
                    };

                    if (config.SlotsIcon != null)
                    {
                        slot.BackgroundIcon = config.SlotsIcon;
                    }

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        slot.Itemstack = attr.value;
                        slot.Itemstack.ResolveBlockOrItem(world);
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = slot;
                }
            }
        }

        return bagContents;
    }

    protected ActionConsumable<KeyCombination>? PreviousHotkeyHandler;
    protected ICoreClientAPI? ClientApi;

    protected virtual bool OnHotkeyPressed(KeyCombination keyCombination)
    {
        InventoryPlayerBackPacks? inventory = GetBackpackInventory();

        if (inventory != null)
        {
            string toolBagId = collObj.Code.ToString();

            if (inventory.Any(slot => (slot as ItemSlotToolHolder)?.ToolBagId == toolBagId))
            {
                ToolBagSystemClient? system = ClientApi?.ModLoader?.GetModSystem<CombatOverhaulSystem>()?.ClientToolBagSystem;

                system?.Send(toolBagId, MainHandSlotConfig != null);
            }
        }

        return PreviousHotkeyHandler?.Invoke(keyCombination) ?? true;
    }

    protected InventoryPlayerBackPacks? GetBackpackInventory()
    {
        return ClientApi?.World?.Player?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }
}


[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class ToolBagPacket
{
    public string ToolBagId { get; set; } = "";
    public bool MainHand { get; set; } = true;
}

public class ToolBagSystemClient
{
    public ToolBagSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<ToolBagPacket>();
    }

    public void Send(string toolBagId, bool mainHand)
    {
        _clientChannel.SendPacket(new ToolBagPacket
        {
            ToolBagId = toolBagId,
            MainHand = mainHand
        });
    }

    private const string _networkChannelId = "CombatOverhaul:stats";
    private readonly IClientNetworkChannel _clientChannel;
}

public class ToolBagSystemServer
{
    public ToolBagSystemServer(ICoreServerAPI api)
    {
        api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<ToolBagPacket>()
            .SetMessageHandler<ToolBagPacket>(HandlePacket);
    }

    private const string _networkChannelId = "CombatOverhaul:stats";

    private void HandlePacket(IServerPlayer player, ToolBagPacket packet)
    {
        IInventory? inventory = GetBackpackInventory(player);

        if (inventory == null) return;

        ItemSlotToolHolder? mainHandToolSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotToolHolder)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotToolHolder)?.MainHand == true, null) as ItemSlotToolHolder;
        ItemSlotToolHolder? offHandToolSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotToolHolder)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotToolHolder)?.MainHand == false, null) as ItemSlotToolHolder;
        ItemSlotTakeOutOnly? mainHandSinkSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotTakeOutOnly)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotTakeOutOnly)?.MainHand == true, null) as ItemSlotTakeOutOnly;
        ItemSlotTakeOutOnly? offHandHandSinkSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotTakeOutOnly)?.ToolBagId == packet.ToolBagId && (slot as ItemSlotTakeOutOnly)?.MainHand == false, null) as ItemSlotTakeOutOnly;
        ItemSlot? mainHandActiveSlot = player.Entity.ActiveHandItemSlot;
        ItemSlot? offHandActiveSlot = player.Entity.LeftHandItemSlot;

        if (mainHandToolSlot != null && mainHandSinkSlot != null && mainHandActiveSlot != null)
        {
            ProcessSlots(mainHandToolSlot, mainHandSinkSlot, mainHandActiveSlot, player);
        }

        if (offHandToolSlot != null && offHandHandSinkSlot != null && offHandActiveSlot != null)
        {
            ProcessSlots(offHandToolSlot, offHandHandSinkSlot, offHandActiveSlot, player);
        }

        try
        {
            mainHandToolSlot?.MarkDirty();
            offHandToolSlot?.MarkDirty();
            mainHandSinkSlot?.MarkDirty();
            offHandHandSinkSlot?.MarkDirty();
            mainHandActiveSlot?.MarkDirty();
            offHandActiveSlot?.MarkDirty();
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(player.Entity.Api, this, $"Error when trying to use tool bag/sheath '{packet.ToolBagId}': {exception}");
        }
    }

    private void ProcessSlots(ItemSlotToolHolder toolSlot, ItemSlotTakeOutOnly sinkSlot, ItemSlot activeSlot, IServerPlayer player)
    {
        try
        {
            if (toolSlot.Empty && !activeSlot.Empty && toolSlot.CanHold(activeSlot))
            {
                Flip(activeSlot, toolSlot);
            }
            else if (!toolSlot.Empty && !activeSlot.Empty && toolSlot.CanHold(activeSlot))
            {
                Flip(activeSlot, toolSlot);
            }
            else if (!toolSlot.Empty)
            {
                TakeOut(activeSlot, toolSlot, sinkSlot, player);
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(player.Entity.Api, this, $"(player: {player.PlayerName}) Exception when trying to interact with sheath/quiver:\n{exception}");
        }
    }

    private void Flip(ItemSlot activeSlot, ItemSlotToolHolder toolSlot)
    {
        bool canTakeActive = toolSlot.CanTakeFrom(activeSlot) || activeSlot.Itemstack == null;
        bool canTakeTool = activeSlot.CanTakeFrom(toolSlot) || toolSlot.Itemstack == null;
        if (!canTakeActive || !canTakeTool) return;

        ItemStack? toolSlotStack = toolSlot.Itemstack;
        ItemStack? activeSlotStack = activeSlot.Itemstack;

        toolSlot.Itemstack = activeSlotStack;
        activeSlot.Itemstack = toolSlotStack;
    }

    private void TakeOut(ItemSlot activeSlot, ItemSlotToolHolder toolSlot, ItemSlotTakeOutOnly sinkSlot, IServerPlayer player)
    {
        if (!sinkSlot.Empty && !activeSlot.Empty) return;

        bool canTakeActive = sinkSlot.CanTakeFrom(activeSlot) || activeSlot.Itemstack == null;
        bool canTakeTool = activeSlot.CanTakeFrom(toolSlot) || toolSlot.Itemstack == null;
        if (!canTakeActive || !canTakeTool) return;

        ItemStack? toolSlotStack = toolSlot.Itemstack;
        ItemStack? activeSlotStack = activeSlot.Itemstack;

        if (activeSlot.Empty)
        {
            activeSlot.Itemstack = toolSlotStack;
            toolSlot.Itemstack = null;
        }

        if (!activeSlot.Empty && sinkSlot.Empty)
        {
            activeSlot.Itemstack = toolSlotStack;
            sinkSlot.Itemstack = activeSlotStack;
            toolSlot.Itemstack = null;
        }
    }

    private void PutBack(ItemSlot activeSlot, ItemSlotToolHolder toolSlot, IServerPlayer player)
    {
        activeSlot.TryPutInto(player.Entity.World, toolSlot, activeSlot.Itemstack?.StackSize ?? 1);
    }

    private static IInventory? GetBackpackInventory(IPlayer player)
    {
        return player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }
}