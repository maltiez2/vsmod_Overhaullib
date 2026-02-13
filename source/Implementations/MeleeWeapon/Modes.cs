using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace CombatOverhaul.Implementations;

public class MeleeWeaponClientModesCollection
{
    public string CurrentModeCode => CurrentModeValue;
    public MeleeWeaponClient CurrentMode => Clients[CurrentModeValue];
    public Dictionary<string, MeleeWeaponClient> Clients { get; protected set; }
    public Dictionary<string, MeleeWeaponModeStats> Modes { get; protected set; } = [];

    public MeleeWeaponClientModesCollection(ICoreClientAPI api, Item item)
    {
        Api = api;

        if (item.Attributes.KeyExists("Modes"))
        {
            Modes = item.Attributes.AsObject<MeleeWeaponModeCollectionStats>().Modes;
        }
        else
        {
            Modes.Add("default", item.Attributes.AsObject<MeleeWeaponModeStats>());
        }

        CurrentModeValue = Modes.First().Key;

        Clients = Modes.ToDictionary(entry => entry.Key, entry => new MeleeWeaponClient(api, item, entry.Value));

        Clients.Where(entry => entry.Key != CurrentModeCode).Foreach(entry => entry.Value.Active = false);

        if (Modes.Count > 1)
        {
            ModeSelector = new(api, Modes.Select(entry => new ModeConfig(entry.Key, entry.Value.Name, entry.Value.Icon)));
        }
    }

    public virtual void ChangeMode(string mode, EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        int fakeState = 0;
        CurrentMode.OnDeselected(player, mainHand, ref fakeState);
        CurrentMode.Active = false;
        CurrentModeValue = mode;
        CurrentMode.Active = true;
        CurrentMode.OnSelected(slot, player, mainHand, ref fakeState);
        CurrentMode.PlayReadyAnimation(mainHand);
    }

    public virtual int GetToolMode(EntityPlayer player, ItemSlot slot)
    {
        return ModeSelector?.GetToolMode() ?? 0;
    }
    public virtual SkillItem[]? GetToolModes(EntityPlayer player, ItemSlot slot)
    {
        return ModeSelector?.GetToolModes();
    }
    public virtual void SetToolMode(EntityPlayer player, ItemSlot slot, int toolMode)
    {
        if (ModeSelector == null) return;

        ModeSelector.SetToolMode(toolMode);

        ChangeMode(ModeSelector.SelectedMode, player, slot, IsMainHand(player, slot));
    }

    protected string CurrentModeValue;
    protected ModeSelector? ModeSelector;
    protected ICoreClientAPI Api;

    protected bool IsMainHand(EntityPlayer player, ItemSlot slot) => player.RightHandItemSlot == slot;
}