using CombatOverhaul.Utils;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Common;

namespace CombatOverhaul.Inputs;

public interface IOnInInventory
{
    void OnInInventory(EntityPlayer player, ItemSlot slot);
}

public sealed class InInventoryPlayerBehavior : EntityBehavior
{
    public InInventoryPlayerBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityPlayer ?? throw new Exception("This behavior should be attached only to player");
        _process = _player.Api.Side == EnumAppSide.Server || _player.PlayerUID == (_player.Api as ICoreClientAPI)?.Settings.String["playeruid"];
        _timeSinceUpdate = 0 - entity.Api.World.Rand.NextSingle() * _updatePeriodSec;
        _timeSinceReport = entity.Api.World.Rand.NextSingle() * _reportPeriodSec;
    }

    public override string PropertyName() => "CombatOverhaul:InInventory";

    private readonly EntityPlayer _player;
    internal static readonly List<long> _reportedEntities = [];
    private const float _updatePeriodSec = 1;
    private const float _reportPeriodSec = 10 * 60;
    private readonly bool _process;
    private float _timeSinceUpdate = 0;
    private float _timeSinceReport = 0;

    public override void OnGameTick(float deltaTime)
    {
        if (!_process) return;

        _timeSinceUpdate += deltaTime;
        _timeSinceReport += deltaTime;

        if (_timeSinceUpdate < _updatePeriodSec) return;

        _timeSinceUpdate = 0;

        Update();
    }

    private void Update()
    {
        if (_player?.Player?.InventoryManager?.Inventories == null) return;

        foreach ((_, IInventory? inventory) in _player.Player.InventoryManager.Inventories)
        {
            if (inventory == null || inventory is InventoryPlayerCreative) continue;

            foreach (ItemSlot? slot in inventory)
            {
                try
                {
                    ProcessSlot(slot);
                }
                catch (Exception exception)
                {
                    if (_timeSinceReport > _reportPeriodSec)
                    {
                        _timeSinceReport = 0;
                        LoggerUtil.Error(_player.Api, this, $"Error for inventory: '{inventory.ClassName}', item: '{slot?.Itemstack?.Collectible?.Code}':\n{exception}");
                    }
                    
                    Debug.WriteLine(exception);
                }
            }
        }
    }

    private void ProcessSlot(ItemSlot? slot)
    {
        if (slot == null || slot.Empty) return;

        CollectibleObject? collectible = slot.Itemstack?.Collectible;

        if (collectible == null) return;

        if (collectible is IOnInInventory inInventory)
        {
            inInventory.OnInInventory(_player, slot);
            return;
        }

        foreach (CollectibleBehavior? behavior in collectible.CollectibleBehaviors)
        {
            if (behavior is IOnInInventory inInventory2)
            {
                inInventory2.OnInInventory(_player, slot);
            }
        }
    }
}