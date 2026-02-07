using CombatOverhaul.Utils;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Armor;

public readonly struct ToolSlotData
{
    public readonly SlotConfig Config;
    public readonly ItemStack? Stack;
    public readonly string ToolBagId;
    public readonly int ToolBagIndex;
    public readonly bool MainHand;
    public readonly string Icon;
    public readonly string Color;
    public readonly int SlotIndex;

    public ToolSlotData(SlotConfig config, ItemStack? stack, string toolBagId, int toolBagIndex, bool mainHand, string icon, string color, int slotIndex)
    {
        Config = config;
        Stack = stack;
        ToolBagId = toolBagId;
        ToolBagIndex = toolBagIndex;
        MainHand = mainHand;
        Icon = icon;
        Color = color;
        SlotIndex = slotIndex;
    }
}

public sealed class ToolBagSelectionSystemClient
{
    public ToolBagSelectionSystemClient(ICoreClientAPI api, ToolBagSystemClient toolBagSystem)
    {
        _api = api;
        _toolBagSystem = toolBagSystem;
        _dialog = new(api, this);
        try
        {
            _api.Input.RegisterHotKeyFirst(HotkeyCode, Lang.Get(HotkeyLangCode), GlKeys.F, HotkeyType.CharacterControls);
        }
        catch
        {
            LoggerUtil.Notify(_api, this, $"Hotkey '{HotkeyCode}' was already registered.");
        }

        _api.Input.SetHotKeyHandler(HotkeyCode, OnHotkeyPress);


        GuiDialogToolMode? toolModeDialog = _api.Gui.LoadedGuis.OfType<GuiDialogToolMode>().FirstOrDefault();
        if (toolModeDialog != null)
        {
            toolModeDialog.OnClosed += () => _dialog?.TryClose();
        }
    }

    public const string HotkeyCode = "combatoverhaul:tool-selection";
    public const string HotkeyLangCode = "combatoverhaul:tool-selection-hotkey";

    public IEnumerable<ToolSlotData> GetToolSlots()
    {
        IInventory? inventory = GetBackpackInventory(_api.World.Player);

        if (inventory == null) return [];

        return inventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.Config.HandleHotkey || slot.Config.DisplayInToolDialog)
            .Select(slot => new ToolSlotData(slot.Config, slot.Itemstack, slot.ToolBagId, slot.ToolBagIndex, slot.MainHand, slot.BackgroundIcon, slot.HexBackgroundColor ?? "", slot.SlotIndex));
    }

    public IEnumerable<ToolSlotData> GetSlotsForToolDialog()
    {
        IInventory? inventory = GetBackpackInventory(_api.World.Player);

        if (inventory == null) return [];

        return inventory
            .OfType<ItemSlotBagContentWithWildcardMatch>()
            .Where(slot => slot.Config.DisplayInToolDialog || slot.Config.HandleHotkey)
            .Select(slot => new ToolSlotData(slot.Config, slot.Itemstack, slot.ToolBagId, slot.ToolBagIndex, slot.MainHand, slot.BackgroundIcon, slot.HexBackgroundColor ?? "", slot.SlotIndex));
    }

    public void TriggerSlots(IEnumerable<ToolSlotData> slots)
    {
        Debug.WriteLine(slots.FirstOrDefault().ToolBagId);

        foreach (ToolSlotData slotData in slots)
        {
            _toolBagSystem.Send(slotData.ToolBagId, slotData.ToolBagIndex, slotData.MainHand, slotData.SlotIndex);
        }

        GuiDialogToolMode? toolModeDialog = _api.Gui.LoadedGuis.OfType<GuiDialogToolMode>().FirstOrDefault();

        toolModeDialog?.TryClose();
        _dialog.TryClose();
    }

    private readonly ICoreClientAPI _api;
    private readonly ToolSelectionGuiDialog _dialog;
    private readonly ToolBagSystemClient _toolBagSystem;

    private bool OnHotkeyPress(KeyCombination combination)
    {
        if (!GetSlotsForToolDialog().Any())
        {
            return false;
        }

        _dialog.TryOpen(withFocus: true);

        return false;
    }

    private static IInventory? GetBackpackInventory(IPlayer player)
    {
        return player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }
}
