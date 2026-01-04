using CombatOverhaul.Armor;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Vanity;

public class VanityInventory : ArmorInventory
{
    public override bool ModifyColors { get; set; } = false;

    public VanityInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        RecolorSlots();
        Synchronize();
    }

    public override void OnItemSlotModified(ItemSlot slot)
    {
        base.OnItemSlotModified(slot);

        Synchronize();

        Player?.Entity?.MarkShapeModified();
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        base.FromTreeAttributes(tree);

        foreach (ItemSlot slot in this)
        {
            slot.HexBackgroundColor = null;
        }

        if (tree.HasAttribute("hiddenSlots"))
        {
            byte[] hiddenSlots = tree.GetBytes("hiddenSlots");

            foreach (byte slotId in hiddenSlots)
            {
                if (this[slotId] is ISlotContentCanHide slot)
                {
                    slot.Hide = true;
                    this[slotId].HexBackgroundColor = ISlotContentCanHide.HiddenSlotColor;
                }
            }
        }

        RecolorSlots();
        Synchronize();
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        byte[] hiddenSlots = this
            .Where(slot => (slot as ISlotContentCanHide)?.Hide == true)
            .Select(slot => (byte)GetSlotId(slot))
            .ToArray();

        tree.SetBytes("hiddenSlots", hiddenSlots);
        Synchronize();
    }

    public void RecolorSlots()
    {
        foreach (ISlotContentCanHide slot in this.OfType<ISlotContentCanHide>())
        {
            if (slot.Hide)
            {
                (slot as ItemSlot).HexBackgroundColor = ISlotContentCanHide.HiddenSlotColor;
            }
            else
            {
                (slot as ItemSlot).HexBackgroundColor = ISlotContentCanHide.ActiveSlotColor;
            }
        }
    }

    public void Synchronize()
    {
        if (Player == null) return;
        
        byte[] hiddenSlots = this
            .Where(slot => (slot as ISlotContentCanHide)?.Hide == true)
            .Select(slot => (byte)GetSlotId(slot))
            .ToArray();

        Api.ModLoader.GetModSystem<CombatOverhaulSystem>()?.ServerVanitySystem?.SendUpdate(Player, hiddenSlots);
    }

    protected override ItemSlot NewSlot(int slotId)
    {
        if (slotId < _clothesSlotsCount)
        {
            ClothesVanitySlot slot = new((EnumCharacterDressType)slotId, this);
            _clothesSlotsIcons.TryGetValue((EnumCharacterDressType)slotId, out slot.BackgroundIcon);
            return slot;
        }
        else if (slotId < _vanillaSlots)
        {
            if (_disableVanillaArmorSlots)
            {
                ArmorVanitySlot slot = new(this, ArmorType.Empty);
                slot.DrawUnavailable = true;
                return slot;
            }
            else
            {
                ClothesVanitySlot slot = new((EnumCharacterDressType)slotId, this);
                _clothesSlotsIcons.TryGetValue((EnumCharacterDressType)slotId, out slot.BackgroundIcon);
                return slot;
            }
        }
        else if (slotId < _armorSlotsLastIndex)
        {
            ArmorType armorType = ArmorTypeFromIndex(slotId);
            ArmorVanitySlot slot = new(this, armorType);
            _slotsByType[armorType] = slot;
            _armorSlotsIcons.TryGetValue(armorType, out slot.BackgroundIcon);
            return slot;
        }
        else if (slotId < _gearSlotsLastIndex)
        {
            string slotType = GearSlotTypes[slotId - _armorSlotsLastIndex];
            GearVanitySlot slot = new(slotType, this);
            _gearSlots[slotType] = slot;
            _gearSlotsIcons.TryGetValue(slotType, out slot.BackgroundIcon);
            return slot;
        }
        else
        {
            return new ItemSlot(this);
        }
    }
}
