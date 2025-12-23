using Vintagestory.API.Client;

namespace CombatOverhaul.Armor;

public class ToolSelectionGuiDialog : GuiDialog
{
    public ToolSelectionGuiDialog(ICoreClientAPI api) : base(api)
    {
        Api = api;
    }

    public override string ToggleKeyCombinationCode => "";

    public override void OnGuiOpened()
    {
        ComposeDialog();
    }

    protected readonly ICoreClientAPI Api;

    protected virtual void ComposeDialog()
    {
        ClearComposers();

        SingleComposer = Api.Gui
            .CreateCompo("combatoverhaul:toolselection", ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(ElementStdBounds.DialogBackground().WithFixedPadding(GuiStyle.ElementToDialogPadding / 2), false)
            .BeginChildElements();
    }
}
