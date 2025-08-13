using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.Inputs;

public interface IOnInInventory
{
    void OnInInventory(EntityPlayer player, ItemSlot slot);
}

public class InInventoryPlayerBehavior : EntityBehavior
{
    public InInventoryPlayerBehavior(Entity entity) : base(entity)
    {
        _player = entity as EntityPlayer ?? throw new Exception("This behavior should be attached only to player");
        _listenerId = entity.Api.World.RegisterGameTickListener(Update, _updateTimeMs, _updateTimeMs + entity.Api.World.Rand.Next(_updateTimeMs));
    }

    public override string PropertyName() => "CombatOverhaul:InInventory";

    private readonly EntityPlayer _player;
    internal static readonly List<long> _reportedEntities = [];
    private const int _updateTimeMs = 1000;
    private readonly long _listenerId = 0;

    private void Update(float dt)
    {
        if (_player?.ShouldDespawn == true)
        {
            entity.Api.World.UnregisterGameTickListener(_listenerId);
            return;
        }

        try
        {
            if (_player?.World != null)
            {
                _player.WalkInventory(ProcessSlot);
            }
        }
        catch (Exception exception)
        {
        }
    }

    private bool ProcessSlot(ItemSlot slot)
    {
        if (slot?.Empty != false) return true;

        if (slot.Itemstack?.Collectible?.GetCollectibleInterface<IOnInInventory>() is IOnInInventory collectible)
        {
            collectible.OnInInventory(_player, slot);
        }

        return true;
    }
}
