using CombatOverhaul.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace CombatOverhaul.Implementations;

public readonly struct ModeConfig
{
    public readonly string Code;
    public readonly string Name;
    public readonly string Icon;

    public ModeConfig(string code, string name, string icon)
    {
        Code = code;
        Name = name;
        Icon = icon;
    }
}

public sealed class ModeSelector
{
    public ModeSelector(ICoreClientAPI api, IEnumerable<ModeConfig> modes)
    {
        _api = api;
        SelectedMode = modes.FirstOrDefault().Code;
        _modes = modes.ToArray();
        _modesSkillItems = GenerateToolModes();
    }

    public string SelectedMode { get; private set; }

    public int GetToolMode()
    {
        return GetSelectedModeIndex();
    }
    public SkillItem[] GetToolModes()
    {
        return _modesSkillItems;
    }
    public void SetToolMode(int toolMode)
    {
        if (_modes.Length <= toolMode || toolMode < 0)
        {
            return;
        }

        SelectedMode = _modes[toolMode].Code;
    }

    private readonly ICoreClientAPI _api;
    private readonly ModeConfig[] _modes;
    private readonly SkillItem[] _modesSkillItems;

    private int GetSelectedModeIndex()
    {
        for (int index = 0; index < _modes.Length; index++)
        {
            if (_modes[index].Code == SelectedMode)
            {
                return index;
            }
        }

        return 0;
    }
    private SkillItem[] GenerateToolModes()
    {
        SkillItem[] modes = _modes.Select(mode =>
        {
            SkillItem item = ToolModesUtils.GetSkillItemWithIcon(_api, mode.Icon, "#FFFFFF");
            item.Code = mode.Code;
            item.Name = Lang.Get(mode.Name);
            return item;
        }).ToArray();

        return modes;
    }
}
