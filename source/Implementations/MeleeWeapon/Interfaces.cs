using Vintagestory.API.Common;

namespace CombatOverhaul.Implementations;

public interface IHasMeleeWeaponActions
{
    bool CanAttack(EntityPlayer player, bool mainHand);
    bool CanBlock(EntityPlayer player, bool mainHand);
    bool CanThrow(EntityPlayer player, bool mainHand);
}

public interface IRestrictAction
{
    bool RestrictRightHandAction();
    bool RestrictLeftHandAction();
}