using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace CombatOverhaul.Armor;

public class ClothesSlot : ItemSlotCharacter
{
    public IWorldAccessor? World { get; set; }
    public string? OwnerUUID { get; set; }
    public bool PreviouslyHeldBag { get; set; } = false;
    public int PreviousItemId { get; set; } = 0;
    public int PreviousDurability { get; set; } = 0;
    public string? PreviousColor { get; set; }
    public bool PreviousEmpty { get; set; } = false;

    public ClothesSlot(EnumCharacterDressType type, InventoryBase inventory) : base(type, inventory)
    {
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Itemstack != null) EmptyBag(Itemstack);

        try
        {
            base.ActivateSlot(sourceSlot, ref op);
            OnItemSlotModified(null);
        }
        catch (Exception exception)
        {
            LoggerUtil.Debug(World?.Api, this, $"(ActivateSlot) Exception: {exception}");
        }
    }

    public override ItemStack? TakeOutWhole()
    {
        ItemStack itemStack = base.TakeOutWhole();

        if (itemStack != null) EmptyBag(itemStack);

        return itemStack;
    }

    public override ItemStack? TakeOut(int quantity)
    {
        ItemStack stack = base.TakeOut(quantity);

        EmptyBag(stack);

        return stack;
    }

    protected override void FlipWith(ItemSlot itemSlot)
    {
        base.FlipWith(itemSlot);

        ItemStack stack = itemSlot.Itemstack;

        if (stack != null) EmptyBag(stack);
    }

    protected void EmptyBag(ItemStack stack)
    {
        IHeldBag? bag = stack?.Item?.GetCollectibleInterface<IHeldBag>();

        try
        {
            if (bag != null && World != null && World.PlayerByUid(OwnerUUID)?.Entity != null)
            {
                ItemStack?[] bagContent = bag.GetContents(stack, World);
                if (bagContent != null)
                {
                    foreach (ItemStack? bagContentStack in bagContent)
                    {
                        if (bagContentStack != null) World.SpawnItemEntity(bagContentStack, World.PlayerByUid(OwnerUUID)?.Entity?.SidedPos.AsBlockPos);
                    }
                }

                bag.Clear(stack);
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(World?.Api, this, $"Error on emptying bag '{stack?.Collectible?.Code}': \n{exception}");
        }
    }

    protected void ModifyBackpackSlot()
    {
        InventoryPlayerBackPacks? backpack = GetBackpackInventory();
        if (backpack != null)
        {
            backpack[0].MarkDirty();
        }
    }

    protected InventoryPlayerBackPacks? GetBackpackInventory()
    {
        return World?.PlayerByUid(OwnerUUID)?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }
}

public class GearSlot : ClothesSlot
{
    public string SlotType { get; set; }
    public bool Enabled { get; set; } = true;
    public ItemSlot? ParentSlot { get; set; }
    public SlotConfig? Config { get; set; }

    public GearSlot(string slotType, InventoryBase inventory) : base(EnumCharacterDressType.Unknown, inventory)
    {
        SlotType = slotType;
        if (slotType.StartsWith("add"))
        {
            Enabled = false;
        }
    }

    public virtual void SetParentSlot(InventoryBase inventory)
    {
        switch (SlotType)
        {
            case "addBeltLeft":
            case "addBeltRight":
            case "addBeltBack":
            case "addBeltFront":
                ParentSlot = inventory.OfType<GearSlot>().FirstOrDefault(slot => slot.SlotType == "waistgear");
                break;
            case "addBackpack1":
            case "addBackpack2":
            case "addBackpack3":
            case "addBackpack4":
                ParentSlot = inventory.OfType<GearSlot>().FirstOrDefault(slot => slot.SlotType == "backgear");
                break;
        }
    }

    public virtual IEnumerable<ItemSlot> GetChildSlots(InventoryBase inventory)
    {
        string[] childSlotCodes = SlotType switch
        {
            "waistgear" => ["addBeltLeft", "addBeltRight", "addBeltBack", "addBeltFront"],
            "backgear" => ["addBackpack1", "addBackpack2", "addBackpack3", "addBackpack4"],
            _ => []
        };

        return inventory.OfType<GearSlot>().Where(slot => childSlotCodes.Contains(slot.SlotType));
    }

    public virtual void CheckParentSlot()
    {
        if (ParentSlot == null) return;

        IEnableAdditionalSlots? parent = ParentSlot.Itemstack?.Collectible?.GetCollectibleInterface<IEnableAdditionalSlots>();

        if (ParentSlot.Itemstack == null || parent == null)
        {
            HexBackgroundColor = "#999999";
            PreviousColor = HexBackgroundColor;
            BackgroundIcon = null;
            Config = null;
            Enabled = false;
            return;
        }

        BackgroundIcon = parent.GetIcon(ParentSlot.Itemstack, inventory, SlotType);
        Enabled = parent.GetIfEnabled(ParentSlot.Itemstack, inventory, SlotType);
        Config = parent.GetConfig(ParentSlot.Itemstack, inventory, SlotType);
        HexBackgroundColor = Enabled ? null : "#999999";
        PreviousColor = HexBackgroundColor;
    }

    public override bool CanTake()
    {
        if (GetChildSlots(inventory).Any(slot => !slot.Empty))
        {
            ((inventory as InventoryBasePlayer)?.Player?.Entity?.Api as ICoreClientAPI)?.TriggerIngameError(this, "canttakeout", "Cannot take out item. Some other items attached to it.");
            return false;
        }
        
        return base.CanTake();
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
        return Enabled && IsGearType(sourceSlot?.Itemstack, SlotType) && CanHoldConfig(sourceSlot);
    }

    public virtual bool CanHoldConfig(ItemSlot? sourceSlot)
    {
        if (Config == null || sourceSlot == null) return true;
        if (!base.CanHold(sourceSlot) || sourceSlot.Itemstack?.Collectible?.Code == null) return false;

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

    public static bool IsGearType(IItemStack? itemStack, string gearType)
    {
        return IsGearType(itemStack?.Collectible, gearType);
    }

    public static bool IsGearType(CollectibleObject? collectible, string gearType)
    {
        if (collectible?.Attributes == null) return false;

        string? stackDressType = collectible.Attributes["clothescategory"].AsString() ?? collectible.Attributes["attachableToEntity"]["categoryCode"].AsString();
        string[]? stackDressTypes = collectible.Attributes["clothescategories"].AsObject<string[]>() ?? collectible.Attributes["attachableToEntity"]["categoryCodes"].AsObject<string[]>();

        bool singleType = stackDressType != null && string.Equals(stackDressType, gearType, StringComparison.InvariantCultureIgnoreCase);
        bool multipleTypes = stackDressTypes != null && stackDressTypes.Contains(value => string.Equals(value, gearType, StringComparison.InvariantCultureIgnoreCase));

        return singleType || multipleTypes;
    }
}

public class ArmorSlot : ItemSlot
{
    public ArmorType ArmorType { get; }
    public ArmorType StoredArmoredType => GetStoredArmorType();
    public DamageZone DamageZone => ArmorType.Slots;
    public ArmorLayers Layer => ArmorType.Layers;
    public DamageResistData Resists => GetResists();
    public override int MaxSlotStackSize => 1;
    public bool Available => _inventory.IsSlotAvailable(ArmorType);
    public List<int> SlotsWithSameItem { get; } = new();
    public IWorldAccessor? World { get; set; }
    public string? OwnerUUID { get; set; }
    public bool PreviouslyHeldBag { get; set; } = false;
    public int PreviousItemId { get; set; } = 0;
    public int PreviousDurability { get; set; } = 0;

    public ArmorSlot(InventoryBase inventory, ArmorType armorType) : base(inventory)
    {
        ArmorType = armorType;
        _inventory = inventory as ArmorInventory ?? throw new Exception();
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (DrawUnavailable || !base.CanHold(sourceSlot) || !IsArmor(sourceSlot.Itemstack.Collectible, out IArmor? armor)) return false;

        if (armor == null || !_inventory.CanHoldArmorPiece(armor)) return false;

        return armor.ArmorType.Intersect(ArmorType);
    }
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Itemstack != null) EmptyBag(Itemstack);

        try
        {
            base.ActivateSlot(sourceSlot, ref op);
            OnItemSlotModified(null);
        }
        catch (Exception exception)
        {
            LoggerUtil.Debug(World?.Api, this, $"(ActivateSlot) Exception: {exception}");
        }
    }
    public override ItemStack? TakeOutWhole()
    {
        ItemStack itemStack = base.TakeOutWhole();

        if (itemStack != null) EmptyBag(itemStack);

        return itemStack;
    }
    public override ItemStack? TakeOut(int quantity)
    {
        ItemStack stack = base.TakeOut(quantity);

        EmptyBag(stack);

        return stack;
    }

    protected override void FlipWith(ItemSlot itemSlot)
    {
        base.FlipWith(itemSlot);

        ItemStack stack = itemSlot.Itemstack;

        if (stack != null) EmptyBag(stack);
    }
    protected void EmptyBag(ItemStack stack)
    {
        IHeldBag? bag = stack?.Item?.GetCollectibleInterface<IHeldBag>();

        try
        {
            if (bag != null && World != null && World.PlayerByUid(OwnerUUID)?.Entity != null)
            {
                ItemStack?[] bagContent = bag.GetContents(stack, World);
                if (bagContent != null)
                {
                    foreach (ItemStack? bagContentStack in bagContent)
                    {
                        if (bagContentStack != null) World.SpawnItemEntity(bagContentStack, World.PlayerByUid(OwnerUUID)?.Entity?.SidedPos.AsBlockPos);
                    }
                }

                bag.Clear(stack);
            }
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(World?.Api, this, $"Error on emptying bag '{stack?.Collectible?.Code}': \n{exception}");
        }
    }
    protected void ModifyBackpackSlot()
    {
        InventoryPlayerBackPacks? backpack = GetBackpackInventory();
        if (backpack != null)
        {
            backpack[0].MarkDirty();
        }
    }
    protected InventoryPlayerBackPacks? GetBackpackInventory()
    {
        return World?.PlayerByUid(OwnerUUID)?.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryPlayerBackPacks;
    }

    private readonly ArmorInventory _inventory;
    private ArmorType GetStoredArmorType()
    {
        if (Itemstack?.Item != null && IsArmor(Itemstack.Collectible, out IArmor? armor) && armor != null)
        {
            return armor.ArmorType;
        }
        else
        {
            return ArmorType.Empty;
        }
    }
    private DamageResistData GetResists()
    {
        if (Itemstack?.Item != null && IsModularArmor(Itemstack.Collectible, out IModularArmor? modularArmor) && modularArmor != null)
        {
            return modularArmor.GetResists(this, ArmorType);
        }
        else if (Itemstack?.Item != null && IsArmor(Itemstack.Collectible, out IArmor? armor) && armor != null)
        {
            return armor.Resists;
        }
        else
        {
            return DamageResistData.Empty;
        }
    }
    private static bool IsArmor(CollectibleObject item, out IArmor? armor)
    {
        if (item is IArmor armorItem)
        {
            armor = armorItem;
            return true;
        }

        CollectibleBehavior? behavior = item.CollectibleBehaviors.FirstOrDefault(x => x is IArmor);

        if (behavior is not IArmor armorBehavior)
        {
            armor = null;
            return false;
        }

        armor = armorBehavior;
        return true;
    }
    private static bool IsModularArmor(CollectibleObject item, out IModularArmor? armor)
    {
        if (item is IModularArmor armorItem)
        {
            armor = armorItem;
            return true;
        }

        CollectibleBehavior? behavior = item.CollectibleBehaviors.FirstOrDefault(x => x is IModularArmor);

        if (behavior is not IModularArmor armorBehavior)
        {
            armor = null;
            return false;
        }

        armor = armorBehavior;
        return true;
    }
}