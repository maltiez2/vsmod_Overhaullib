using ProtoBuf;
using System.Security.Cryptography.X509Certificates;
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
    public ItemTagRule CanHoldItemTags { get; set; } = ItemTagRule.Empty;
    public BlockTagRule CanHoldBlockTags { get; set; } = BlockTagRule.Empty;
    public string[] CanHoldWildcard { get; set; } = [];

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
            bool matchWithoutDomain = WildcardUtil.Match(CanHoldWildcard, sourceSlot.Itemstack.Collectible.Code.Path);
            bool matchWithDomain = WildcardUtil.Match(CanHoldWildcard, sourceSlot.Itemstack.Collectible.Code.ToString());

            bool matchWithTags = false;
            if (sourceSlot.Itemstack?.Item != null && CanHoldItemTags != ItemTagRule.Empty)
            {
                matchWithTags = CanHoldItemTags.Intersects(sourceSlot.Itemstack.Item.Tags);
            }
            if (sourceSlot.Itemstack?.Block != null && CanHoldBlockTags != BlockTagRule.Empty)
            {
                matchWithTags = CanHoldBlockTags.Intersects(sourceSlot.Itemstack.Block.Tags);
            }

            return matchWithoutDomain || matchWithDomain || matchWithTags;
        }

        return false;
    }
}

public class ItemSlotToolHolder : ItemSlotBagContentWithWildcardMatch
{
    public int ToolBagId { get; set; }

    public ItemSlotToolHolder(InventoryBase inventory, int BagIndex, int SlotIndex, EnumItemStorageFlags storageType, ItemStack sourceBag, string? color = null) : base(inventory, BagIndex, SlotIndex, storageType, sourceBag, color)
    {
        ToolBagId = sourceBag.Item?.Code?.GetHashCode() ?? 0;
    }
}

public class ItemSlotTakeOutOnly : ItemSlotBagContent
{
    public int ToolBagId { get; set; }
    public bool CanHoldNow { get; set; } = false;

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

        ToolBagId = sourceBag.Item?.Code?.GetHashCode() ?? 0;
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => CanHoldNow;

    public override bool CanHold(ItemSlot sourceSlot) => CanHoldNow;

    protected virtual void TryEmptyIntoHotbar(InventoryBasePlayer inventory, int index)
    {
        if (CanHoldNow || Empty || SlotIndex == index && Inventory.InventoryID == inventory.InventoryID) return;

        DummySlot dummySlot = new(itemstack);
        ItemSlot? targetSlot = inventory.GetBestSuitedSlot(dummySlot)?.slot ?? inventory.FirstOrDefault(slot => slot?.CanHold(dummySlot) == true, null);

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
        if (CanHoldNow || Empty || SlotIndex == index && Inventory.InventoryID == inventory.InventoryID) return;

        DummySlot dummySlot = new(itemstack);
        ItemSlot? targetSlot =
            (inventory as InventoryBasePlayer)?.Player?.InventoryManager?.GetHotbarInventory()?.GetBestSuitedSlot(dummySlot)?.slot
            ?? (inventory as InventoryBasePlayer)?.Player?.InventoryManager?.GetHotbarInventory().FirstOrDefault(slot => slot?.CanHold(dummySlot) == true, null)
            ?? inventory.GetBestSuitedSlot(dummySlot)?.slot
            ?? inventory.FirstOrDefault(slot => slot?.CanHold(dummySlot) == true, null);

        if (targetSlot == null) return;

        if (TryPutInto(inventory.Api.World, dummySlot, dummySlot.Itemstack.StackSize) > 0)
        {
            targetSlot.MarkDirty();
            itemstack = dummySlot.Itemstack?.StackSize == 0 ? null : dummySlot.Itemstack;
            MarkDirty();
        }
    }
}

public class GearEquipableBag : CollectibleBehavior, IHeldBag, IAttachedInteractions
{
    public ItemTagRule CanHoldItemTags { get; set; } = ItemTagRule.Empty;
    public BlockTagRule CanHoldBlockTags { get; set; } = BlockTagRule.Empty;
    public string[] CanHoldWildcard { get; protected set; } = ["*"];
    public string? SlotColor { get; protected set; } = null;
    public int SlotsNumber { get; protected set; } = 0;

    protected ICoreAPI? Api;
    protected string[] ItemTags = [];
    protected string[] BlockTags = [];

    public GearEquipableBag(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Api = api;

        CanHoldItemTags = new(Api, ItemTags);
        CanHoldBlockTags = new(Api, BlockTags);

        ItemTags = [];
        BlockTags = [];
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        CanHoldWildcard = properties["canHoldWildcards"].AsArray().Select(element => element.AsString("*")).ToArray();
        SlotsNumber = properties["slotsNumber"].AsInt(0);
        SlotColor = properties["color"].AsString(null);

        ItemTags = properties["canHoldItemTags"].AsArray<string>([]);
        BlockTags = properties["canHoldBlockTags"].AsArray<string>([]);
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
        int quantitySlots = SlotsNumber;

        ITreeAttribute stackBackPackTree = bagstack.Attributes.GetTreeAttribute("backpack");
        if (stackBackPackTree == null)
        {
            stackBackPackTree = new TreeAttribute();
            ITreeAttribute slotsTree = new TreeAttribute();

            for (int slotIndex = 0; slotIndex < quantitySlots; slotIndex++)
            {
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, SlotColor)
                {
                    CanHoldWildcard = CanHoldWildcard,
                    CanHoldItemTags = CanHoldItemTags,
                    CanHoldBlockTags = CanHoldBlockTags
                };
                bagContents.Add(slot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
            }

            stackBackPackTree["slots"] = slotsTree;
            bagstack.Attributes["backpack"] = stackBackPackTree;
        }
        else
        {
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            foreach (KeyValuePair<string, IAttribute> val in slotsTree)
            {
                int slotIndex = val.Key.Split("-")[1].ToInt();
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, SlotColor)
                {
                    CanHoldWildcard = CanHoldWildcard,
                    CanHoldItemTags = CanHoldItemTags,
                    CanHoldBlockTags = CanHoldBlockTags
                };

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
}

public class ToolBag : GearEquipableBag
{
    public string? TakeOutSlotColor { get; protected set; } = null;
    public string? ToolSlotColor { get; protected set; } = null;
    public string HotkeyCode { get; protected set; } = "";
    public string HotkeyName { get; protected set; } = "";
    public GlKeys HotKeyKey { get; protected set; } = GlKeys.R;
    public int RegularSlotsNumber { get; protected set; } = 0;
    public ItemTagRule ToolItemTags { get; set; } = ItemTagRule.Empty;
    public BlockTagRule ToolBlockTags { get; set; } = BlockTagRule.Empty;
    public string[] ToolWildcard { get; protected set; } = ["*"];
    public bool MainHand { get; protected set; } = true;

    public ToolBag(CollectibleObject collObj) : base(collObj)
    {
    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        TakeOutSlotColor = properties["takeOutColor"].AsString(null);
        ToolSlotColor = properties["toolSlotColor"].AsString(null);
        HotkeyCode = properties["hotkeyCode"].AsString("");
        HotkeyName = properties["hotkeyName"].AsString("");
        HotKeyKey = Enum.Parse<GlKeys>(properties["hotkeyKey"].AsString("R"));
        ToolWildcard = properties["toolWildcards"].AsArray().Select(element => element.AsString("*")).ToArray();
        ToolStringItemTags = properties["toolItemTags"].AsArray<string>([]);
        ToolStringBlockTags = properties["toolBlockTags"].AsArray<string>([]);
        MainHand = properties["putIntoMainHand"].AsBool(true);

        RegularSlotsNumber = SlotsNumber;
        SlotsNumber += 2;
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Api = api;

        if (api is not ICoreClientAPI clientApi) return;

        ClientApi = clientApi;

        if (!clientApi.Input.HotKeys.TryGetValue(HotkeyCode, out HotKey? hotkey))
        {
            clientApi.Input.RegisterHotKey(HotkeyCode, HotkeyName, HotKeyKey);
            hotkey = clientApi.Input.HotKeys[HotkeyCode];
        }

        PreviousHotkeyHandler = hotkey.Handler;

        clientApi.Input.SetHotKeyHandler(HotkeyCode, OnHotkeyPressed);

        ToolItemTags = new(Api, ToolStringItemTags);
        ToolBlockTags = new(Api, ToolStringBlockTags);

        ToolStringItemTags = [];
        ToolStringBlockTags = [];
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

            for (int slotIndex = 0; slotIndex < RegularSlotsNumber; slotIndex++)
            {
                ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, SlotColor)
                {
                    CanHoldWildcard = CanHoldWildcard,
                    CanHoldItemTags = CanHoldItemTags,
                    CanHoldBlockTags = CanHoldBlockTags
                };
                bagContents.Add(slot);
                slotsTree["slot-" + slotIndex] = new ItemstackAttribute(null);
            }

            ItemSlotToolHolder toolSlot = new(parentinv, bagIndex, 0, flags, bagstack, ToolSlotColor)
            {
                CanHoldWildcard = CanHoldWildcard,
                CanHoldItemTags = CanHoldItemTags,
                CanHoldBlockTags = CanHoldBlockTags
            };
            bagContents.Add(toolSlot);
            slotsTree["slot-" + RegularSlotsNumber] = new ItemstackAttribute(null);

            ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, 0, flags, bagstack, TakeOutSlotColor);
            bagContents.Add(takeOutSLot);
            slotsTree["slot-" + (RegularSlotsNumber + 1)] = new ItemstackAttribute(null);


            stackBackPackTree["slots"] = slotsTree;
            bagstack.Attributes["backpack"] = stackBackPackTree;
        }
        else
        {
            ITreeAttribute slotsTree = stackBackPackTree.GetTreeAttribute("slots");

            foreach (KeyValuePair<string, IAttribute> val in slotsTree)
            {
                int slotIndex = val.Key.Split("-")[1].ToInt();

                if (slotIndex < RegularSlotsNumber)
                {
                    ItemSlotBagContentWithWildcardMatch slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, SlotColor)
                    {
                        CanHoldWildcard = CanHoldWildcard,
                        CanHoldItemTags = CanHoldItemTags,
                        CanHoldBlockTags = CanHoldBlockTags
                    };

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        slot.Itemstack = attr.value;
                        slot.Itemstack.ResolveBlockOrItem(world);
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = slot;
                }
                else if (slotIndex == RegularSlotsNumber)
                {
                    ItemSlotToolHolder slot = new(parentinv, bagIndex, slotIndex, flags, bagstack, ToolSlotColor)
                    {
                        CanHoldWildcard = ToolWildcard,
                        CanHoldItemTags = ToolItemTags,
                        CanHoldBlockTags = ToolBlockTags
                    };

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        slot.Itemstack = attr.value;
                        slot.Itemstack.ResolveBlockOrItem(world);
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = slot;
                }
                else if (slotIndex == RegularSlotsNumber + 1)
                {
                    ItemSlotTakeOutOnly takeOutSLot = new(parentinv, bagIndex, slotIndex, flags, bagstack, TakeOutSlotColor);

                    if (val.Value?.GetValue() != null)
                    {
                        ItemstackAttribute attr = (ItemstackAttribute)val.Value;
                        takeOutSLot.Itemstack = attr.value;
                        takeOutSLot.Itemstack.ResolveBlockOrItem(world);
                    }

                    while (bagContents.Count <= slotIndex) bagContents.Add(null);
                    bagContents[slotIndex] = takeOutSLot;
                }
            }
        }

        return bagContents;
    }

    protected ActionConsumable<KeyCombination>? PreviousHotkeyHandler;
    protected ICoreClientAPI? ClientApi;
    protected string[] ToolStringItemTags = [];
    protected string[] ToolStringBlockTags = [];

    protected virtual bool OnHotkeyPressed(KeyCombination keyCombination)
    {
        InventoryPlayerBackPacks? inventory = GetBackpackInventory();

        if (inventory != null)
        {
            int toolBagId = collObj.Code.GetHashCode();

            if (inventory.Any(slot => (slot as ItemSlotToolHolder)?.ToolBagId == toolBagId))
            {
                ToolBagSystemClient? system = ClientApi?.ModLoader?.GetModSystem<CombatOverhaulSystem>()?.ClientToolBagSystem;

                system?.Send(toolBagId, MainHand);
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
    public int ToolBagId { get; set; } = 0;
    public bool MainHand { get; set; } = true;
}

public class ToolBagSystemClient
{
    public ToolBagSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(_networkChannelId)
            .RegisterMessageType<ToolBagPacket>();
    }

    public void Send(int toolBagId, bool mainHand)
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
        InventoryPlayerBackPacks? inventory = GetBackpackInventory(player);

        if (inventory == null) return;

        ItemSlotToolHolder? toolSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotToolHolder)?.ToolBagId == packet.ToolBagId, null) as ItemSlotToolHolder;
        ItemSlotTakeOutOnly? sinkSlot = inventory.FirstOrDefault(slot => (slot as ItemSlotTakeOutOnly)?.ToolBagId == packet.ToolBagId, null) as ItemSlotTakeOutOnly;
        ItemSlot? activeSlot = packet.MainHand ? player.Entity.ActiveHandItemSlot : player.Entity.LeftHandItemSlot;

        if (toolSlot == null || sinkSlot == null || activeSlot == null) return;

        if (toolSlot.Empty && !activeSlot.Empty && toolSlot.CanHold(activeSlot))
        {
            PutBack(activeSlot, toolSlot, player);
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

    private void Flip(ItemSlot activeSlot, ItemSlotToolHolder toolSlot)
    {
        toolSlot.TryFlipWith(activeSlot);

        activeSlot.MarkDirty();
        toolSlot.MarkDirty();
    }

    private void TakeOut(ItemSlot activeSlot, ItemSlotToolHolder toolSlot, ItemSlotTakeOutOnly sinkSlot, IServerPlayer player)
    {
        if (!sinkSlot.Empty && !activeSlot.Empty) return;

        DummySlot dummySlot = new();

        activeSlot.TryPutInto(player.Entity.World, dummySlot, activeSlot.Itemstack?.StackSize ?? 1);
        toolSlot.TryPutInto(player.Entity.World, activeSlot, toolSlot.Itemstack?.StackSize ?? 1);

        sinkSlot.CanHoldNow = true;
        dummySlot.TryPutInto(player.Entity.World, sinkSlot, dummySlot.Itemstack?.StackSize ?? 1);
        sinkSlot.CanHoldNow = false;

        activeSlot.MarkDirty();
        toolSlot.MarkDirty();
        sinkSlot.MarkDirty();
    }

    private void PutBack(ItemSlot activeSlot, ItemSlotToolHolder toolSlot, IServerPlayer player)
    {
        activeSlot.TryPutInto(player.Entity.World, toolSlot, activeSlot.Itemstack?.StackSize ?? 1);

        activeSlot.MarkDirty();
        toolSlot.MarkDirty();
    }

    private static InventoryPlayerBackPacks? GetBackpackInventory(IPlayer player)
    {
        return player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }
}