using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using YamlDotNet.Serialization;

namespace CombatOverhaul.Implementations;

public sealed class AmmoSelector
{
    public AmmoSelector(ICoreClientAPI api, string ammoWildcard)
    {
        _api = api;
        _ammoWildcard = ammoWildcard;
        SelectedAmmo = ammoWildcard;
    }

    public string SelectedAmmo { get; private set; }

    public int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        if (_ammoSlots.Count == 0) UpdateAmmoSlots(byPlayer);

        return GetSelectedModeIndex();
    }
    public SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        UpdateAmmoSlots(forPlayer);


        return GetOrGenerateModes();
    }
    public void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if (toolMode == 0 || _ammoSlots.Count < toolMode)
        {
            SelectedAmmo = _ammoWildcard;
            return;
        }

        SelectedAmmo = _ammoSlots[toolMode - 1].Itemstack.Collectible.Code.ToString();
    }

    private readonly ICoreClientAPI _api;
    private readonly string _ammoWildcard;
    private readonly List<ItemSlot> _ammoSlots = new();
    private readonly TimeSpan _generationTimeout = TimeSpan.FromSeconds(1);
    private TimeSpan _lastGenerationTime = TimeSpan.Zero;
    private SkillItem[] _modesCache = Array.Empty<SkillItem>();

    private int GetSelectedModeIndex()
    {
        if (SelectedAmmo == _ammoWildcard) return 0;

        for (int index = 0; index < _ammoSlots.Count; index++)
        {
            if (WildcardUtil.Match(_ammoWildcard, _ammoSlots[index].Itemstack.Item.Code.ToString()))
            {
                return index + 1;
            }
        }

        return 0;
    }
    private void UpdateAmmoSlots(IPlayer player)
    {
        _ammoSlots.Clear();

        player.Entity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_ammoWildcard, slot.Itemstack.Item.Code.ToString()))
            {
                AddAmmoStackToList(slot.Itemstack.Clone());
            }

            return true;
        });
    }
    private void AddAmmoStackToList(ItemStack stack)
    {
        foreach (ItemSlot slot in _ammoSlots.Where(slot => slot.Itemstack.Collectible.Code.ToString() == stack.Collectible.Code.ToString()))
        {
            slot.Itemstack.StackSize += stack.StackSize;
            return;
        }

        _ammoSlots.Add(new DummySlot(stack));
    }
    private SkillItem[] GetOrGenerateModes()
    {
        TimeSpan currentTime = TimeSpan.FromMilliseconds(_api.World.ElapsedMilliseconds);
        if (currentTime - _lastGenerationTime < _generationTimeout)
        {
            return _modesCache;
        }

        _lastGenerationTime = currentTime;
        _modesCache = GenerateToolModes();
        return _modesCache;
    }
    private SkillItem[] GenerateToolModes()
    {
        SkillItem[] modes = ToolModesUtils.GetModesFromSlots(_api, _ammoSlots, slot => slot.Itemstack.Collectible.GetHeldItemName(slot.Itemstack));

        SkillItem mode = new()
        {
            Code = new("none-0"),
            Name = Lang.Get("combatoverhaul:toolmode-noselection")
        };

        return modes.Prepend(mode).ToArray();
    }
}
