using Vintagestory.API.Common;

namespace CombatOverhaul.Animations;

public interface IHasIdleAnimations
{
    AnimationRequestByCode IdleAnimation { get; }
    AnimationRequestByCode ReadyAnimation { get; }
}

public interface IHasDynamicIdleAnimations
{
    AnimationRequestByCode? GetIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetReadyAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
}

public interface IHasMoveAnimations : IHasIdleAnimations
{
    AnimationRequestByCode WalkAnimation { get; }
    AnimationRequestByCode RunAnimation { get; }
    AnimationRequestByCode SwimAnimation { get; }
    AnimationRequestByCode SwimIdleAnimation { get; }
}

public interface IHasDynamicMoveAnimations : IHasDynamicIdleAnimations
{
    AnimationRequestByCode? GetWalkAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetRunAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetSwimAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
    AnimationRequestByCode? GetSwimIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand);
}