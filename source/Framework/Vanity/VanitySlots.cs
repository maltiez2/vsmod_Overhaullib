using CombatOverhaul.Armor;
using Vintagestory.API.Common;

namespace CombatOverhaul.Vanity;

public interface ISlotContentCanHide
{
    bool Hide { get; set; }

    const string? HiddenSlotColor = "#776666";
    const string? ActiveSlotColor = null;
}

public class ClothesVanitySlot : ClothesSlot, ISlotContentCanHide
{
    public bool Hide { get; set; } = false;
    
    public ClothesVanitySlot(EnumCharacterDressType type, InventoryBase inventory) : base(type, inventory)
    {
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Inventory.Api.Side == EnumAppSide.Client) return;
        
        if (sourceSlot.Itemstack != null)
        {
            if (!CanHold(sourceSlot))
            {
                return;
            }
            
            Itemstack = sourceSlot.Itemstack.Clone();
            Hide = false;
            HexBackgroundColor = ISlotContentCanHide.ActiveSlotColor;
            OnSlotClicked?.Invoke(this, sourceSlot, ref op);
            MarkDirty();
            return;
        }

        if (Itemstack == null)
        {
            Hide = !Hide;
            HexBackgroundColor = Hide ? ISlotContentCanHide.HiddenSlotColor : ISlotContentCanHide.ActiveSlotColor;
            MarkDirty();
        }
        else
        {
            Itemstack = null;
            OnSlotClicked?.Invoke(this, sourceSlot, ref op);
            MarkDirty();
        }
    }
}

public class GearVanitySlot : GearSlot, ISlotContentCanHide
{
    public bool Hide { get; set; } = false;

    public GearVanitySlot(string slotType, InventoryBase inventory) : base(slotType, inventory)
    {
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Inventory.Api.Side == EnumAppSide.Client) return;

        if (sourceSlot.Itemstack != null)
        {
            if (!CanHold(sourceSlot))
            {
                return;
            }

            Itemstack = sourceSlot.Itemstack.Clone();
            Hide = false;
            HexBackgroundColor = ISlotContentCanHide.ActiveSlotColor;
            OnSlotClicked?.Invoke(this, sourceSlot, ref op);
            MarkDirty();
            return;
        }

        if (Itemstack == null)
        {
            Hide = !Hide;
            HexBackgroundColor = Hide ? ISlotContentCanHide.HiddenSlotColor : ISlotContentCanHide.ActiveSlotColor;
            MarkDirty();
        }
        else
        {
            Itemstack = null;
            OnSlotClicked?.Invoke(this, sourceSlot, ref op);
            MarkDirty();
        }
    }
}

public class ArmorVanitySlot : ArmorSlot, ISlotContentCanHide
{
    public bool Hide { get; set; } = false;

    public ArmorVanitySlot(InventoryBase inventory, ArmorType armorType) : base(inventory, armorType)
    {
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Inventory.Api.Side == EnumAppSide.Client) return;

        if (sourceSlot.Itemstack != null)
        {
            if (!CanHold(sourceSlot))
            {
                return;
            }

            Itemstack = sourceSlot.Itemstack.Clone();
            Hide = false;
            HexBackgroundColor = ISlotContentCanHide.ActiveSlotColor;
            OnSlotClicked?.Invoke(this, sourceSlot, ref op);
            MarkDirty();
            return;
        }

        if (Itemstack == null)
        {
            Hide = !Hide;
            HexBackgroundColor = Hide ? ISlotContentCanHide.HiddenSlotColor : ISlotContentCanHide.ActiveSlotColor;
            MarkDirty();
        }
        else
        {
            Itemstack = null;
            OnSlotClicked?.Invoke(this, sourceSlot, ref op);
            MarkDirty();
        }
    }
}