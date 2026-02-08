using CombatOverhaul.Integration;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CombatOverhaul;

internal static class HarmonyPatchesManager
{
    public static void Patch(ICoreAPI api)
    {
        _api = api;

        PatchUniversalSide(api);

        if (api is ICoreClientAPI clientApi)
        {
            PatchClientSide(clientApi);
        }
    }
    public static void Unpatch()
    {
        UnpatchUniversalSide();
        UnpatchClientSide();
        _api = null;
    }


    private const string _harmonyId = "OverhaulLib:";
    private const string _harmonyIdAiming = _harmonyId + "Aiming";
    private const string _harmonyIdMouseWheel = _harmonyId + "MouseWheel";
    private const string _harmonyIdGuiDialog = _harmonyId + "GuiDialog";
    private const string _harmonyIdTranspilers = _harmonyId + "Transpilers";
    private const string _harmonyIdInventory = _harmonyId + "Inventory";
    private const string _harmonyIdAnimation = _harmonyId + "Animation";
    private const string _harmonyIdGeneral = _harmonyId + "General";

    private static ICoreAPI? _api;
    private static bool _patchedUniversalSide = false;
    private static bool _patchedClientSide = false;


    private static void PatchClientSide(ICoreClientAPI api)
    {
        if (_patchedClientSide)
        {
            return;
        }
        _patchedClientSide = true;

        AimingPatches.Patch(_harmonyIdAiming);
        MouseWheelPatch.Patch(_harmonyIdMouseWheel, api);
        GuiDialogPatches.Patch(_harmonyIdGuiDialog, api);
    }
    private static void UnpatchClientSide()
    {
        if (!_patchedClientSide)
        {
            return;
        }
        _patchedClientSide = false;


        AimingPatches.Unpatch(_harmonyIdAiming);
        MouseWheelPatch.Unpatch(_harmonyIdMouseWheel);
        GuiDialogPatches.Unpatch(_harmonyIdGuiDialog);
    }

    private static void PatchUniversalSide(ICoreAPI api)
    {
        if (_patchedUniversalSide)
        {
            return;
        }
        _patchedUniversalSide = true;

        new Harmony(_harmonyIdTranspilers).PatchAll();

        InventorySafeguardsPatches.Patch(_harmonyIdInventory);
        HarmonyPatches.Patch(_harmonyIdGeneral, api);
        AnimationPatches.Patch(_harmonyIdAnimation, api);
    }
    private static void UnpatchUniversalSide()
    {
        if (!_patchedUniversalSide)
        {
            return;
        }
        _patchedUniversalSide = false;

        new Harmony(_harmonyIdTranspilers).UnpatchAll();

        InventorySafeguardsPatches.Unpatch(_harmonyIdInventory);
        if (_api != null)
        {
            HarmonyPatches.Unpatch(_harmonyIdGeneral, _api);
            AnimationPatches.Unpatch(_harmonyIdAnimation, _api);
        }
    }
}
