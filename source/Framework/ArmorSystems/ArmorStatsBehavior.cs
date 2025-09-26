using CombatOverhaul.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

public interface IAffectsPlayerStats
{
    public Dictionary<string, float> PlayerStats(ItemSlot slot, EntityPlayer player);

    public bool StatsChanged { get; set; }
}

public sealed class WearableStatsBehavior : EntityBehavior, IDisposable
{
    public WearableStatsBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityPlayer ?? throw new InvalidDataException("This is player behavior");

        _player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().OnDispose += Dispose;

        if (_player.Api.Side == EnumAppSide.Client)
        {
            if (_existingBehaviors.TryGetValue(_player.PlayerUID, out WearableStatsBehavior? previousBehavior))
            {
                previousBehavior.PartialDispose();
            }

            _existingBehaviors[_player.PlayerUID] = this;
        }
    }

    public override string PropertyName() => "CombatOverhaul:WearableStats";
    public Dictionary<string, float> Stats { get; } = new();

    public override void OnGameTick(float deltaTime)
    {
        if (_initialized) return;

        InventoryBase? inventory = GetGearInventory(_player);

        if (inventory == null) return;

        if (inventory is ArmorInventory armorInventory)
        {
            armorInventory.OnSlotModified += UpdateStatsValuesConditional;
        }
        else
        {
            inventory.SlotModified += UpdateStatsValues;
        }

        UpdateStatsValues(0);

        _initialized = true;
    }

    private readonly EntityPlayer _player;
    private const string _statsCategory = "CombatOverhaul:Armor";
    private bool _initialized = false;
    private static readonly Dictionary<string, WearableStatsBehavior> _existingBehaviors = [];

    private static InventoryBase? GetGearInventory(Entity entity)
    {
        return entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
    }

    private void UpdateStatsValues(int slotId)
    {
        UpdateStatsValuesConditional(true, true, true);
    }

    private void UpdateStatsValuesConditional(bool itemChanged, bool durabilityChanged, bool isArmorSlot)
    {
        InventoryBase? inventory = GetGearInventory(_player);

        if (inventory == null) return;

        

        bool anyStatsChangedItem = itemChanged;
        bool anyStatsChangedBehavior = itemChanged;

        if (!itemChanged)
        {
            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
            {
                if (slot?.Itemstack?.Item is not IAffectsPlayerStats item) continue;

                if (item.StatsChanged)
                {
                    anyStatsChangedItem = true;
                    item.StatsChanged = false;
                }
            }

            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
            {
                IAffectsPlayerStats? behavior = slot.Itemstack.Item.CollectibleBehaviors.Where(behavior => behavior is IAffectsPlayerStats).FirstOrDefault(defaultValue: null) as IAffectsPlayerStats;

                if (behavior == null) continue;

                if (behavior.StatsChanged)
                {
                    anyStatsChangedBehavior = true;
                    behavior.StatsChanged = false;
                }
            }
        }

        if (!anyStatsChangedItem && !anyStatsChangedBehavior) return;

        

        foreach ((string stat, _) in Stats)
        {
            _player.Stats.Remove(stat, _statsCategory);
        }

        Stats.Clear();

        if (anyStatsChangedItem)
        {
            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
            {
                if (slot?.Itemstack?.Item is not IAffectsPlayerStats item) continue;

                foreach ((string stat, float value) in item.PlayerStats(slot, _player))
                {
                    AddStatValue(stat, value);
                }
            }
        }

        if (anyStatsChangedBehavior)
        {
            foreach (ItemSlot slot in inventory
                .Where(slot => slot?.Itemstack?.Item != null)
                .Where(slot => slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) > 0))
            {
                IAffectsPlayerStats? behavior = slot.Itemstack.Item.CollectibleBehaviors.Where(behavior => behavior is IAffectsPlayerStats).FirstOrDefault(defaultValue: null) as IAffectsPlayerStats;

                if (behavior == null) continue;

                foreach ((string stat, float value) in behavior.PlayerStats(slot, _player))
                {
                    AddStatValue(stat, value);
                }
            }
        }

        foreach ((string stat, float value) in Stats)
        {
            _player.Stats.Set(stat, _statsCategory, value, false);
        }

        _player.walkSpeed = _player.Stats.GetBlended("walkspeed");

        
    }
    private void AddStatValue(string stat, float value)
    {
        if (stat == "walkspeed" && value < 0)
        {
            value *= _player.Stats.GetBlended("armorWalkSpeedAffectedness");
        }

        if (stat == "manipulationSpeed" && value < 0)
        {
            value *= _player.Stats.GetBlended("armorManipulationSpeedAffectedness");
        }

        if (!Stats.ContainsKey(stat))
        {
            Stats[stat] = value;
        }
        else
        {
            Stats[stat] += value;
        }
    }

    private void PartialDispose()
    {
        InventoryBase? inventory = GetGearInventory(_player);
        if (inventory != null)
        {
            inventory.SlotModified -= UpdateStatsValues;
        }
    }

    public void Dispose()
    {
        InventoryBase? inventory = GetGearInventory(_player);
        if (inventory != null)
        {
            inventory.SlotModified -= UpdateStatsValues;
        }
        _existingBehaviors.Clear();
    }
}