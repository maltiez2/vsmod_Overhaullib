using CombatOverhaul.Armor;
using Vintagestory.API.Common.Entities;

namespace CombatOverhaul.Vanity;

public class VanityBehavior : EntityBehavior
{
    public VanityBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "VanityBehavior";

    public ArmorInventory? VanityInventory { get; set; }
}
