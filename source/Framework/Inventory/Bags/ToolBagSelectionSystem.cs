using Vintagestory.API.Client;

namespace CombatOverhaul.Armor;

public sealed class ToolBagSelectionSystemClient
{
    public ToolBagSelectionSystemClient(ICoreClientAPI api, ToolBagSystemClient toolBagSystem)
    {
        _api = api;
        _dialog = new(api);
        _toolBagSystem = toolBagSystem;
    }

    private readonly ICoreClientAPI _api;
    private readonly ToolSelectionGuiDialog _dialog;
    private readonly ToolBagSystemClient _toolBagSystem;
}
