using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Compatibility;

public class PlayerModelConfig
{
    public string Domain { get; set; } = "";

    public CollidersConfig? Colliders { get; set; } = null;

    public PlayerDamageModelConfig? DamageModel { get; set; } = null;
}

public class PlayerModelConfigJson
{
    public PlayerModelConfig? CombatOverhaulConfig { get; set; } = null;
}

public sealed class PlayerModelLibCompatibilitySystem : ModSystem
{
    public Dictionary<string, PlayerModelConfig> CustomModelConfigs { get; private set; } = [];

    public override double ExecuteOrder() => 0.209;

    public const string PlayerModelLibId = "playermodellib";

    public override bool ShouldLoad(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled(PlayerModelLibId))
        {
            return false;
        }

        return base.ShouldLoad(api);
    }

    public override void Start(ICoreAPI api)
    {
        /*CustomModelsSystem system = api.ModLoader.GetModSystem<CustomModelsSystem>() ?? throw new InvalidOperationException($"Failed to find 'CustomModelsSystem' system, make sure that '{PlayerModelLibId}' mod is installed");

        system.OnCustomModelsLoaded += () => LoadModelsData(api);*/
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        LoadModelsData(api);
    }

    private const string _playerModelLibConfigs = "customplayermodels";

    private void LoadModelsData(ICoreAPI api)
    {
        List<IAsset> modelsConfigs = api.Assets.GetManyInCategory("config", _playerModelLibConfigs);

        foreach (Dictionary<string, PlayerModelConfig> customModelConfigs in modelsConfigs.Select(asset => FromAsset(asset, api)))
        {
            foreach ((string code, PlayerModelConfig modelConfig) in customModelConfigs)
            {
                CustomModelConfigs.Add(code, modelConfig);
            }
        }
    }
    private Dictionary<string, PlayerModelConfig> FromAsset(IAsset asset, ICoreAPI api)
    {
        Dictionary<string, PlayerModelConfig> result = [];
        string domain = asset.Location.Domain;
        JObject? json;

        try
        {
            string text = asset.ToText();
            json = JsonObject.FromJson(text).Token as JObject;

            if (json == null)
            {
                Utils.LoggerUtil.Error(api, this, $"Error when trying to load model config '{asset.Location}'.");
                return result;
            }
        }
        catch (Exception exception)
        {
            Utils.LoggerUtil.Error(api, this, $"Exception when trying to load model config '{asset.Location}':\n{exception}");
            return result;
        }

        foreach ((string code, JToken? token) in json)
        {
            try
            {
                JsonObject configJson = new(token);
                PlayerModelConfigJson config = configJson.AsObject<PlayerModelConfigJson>();
                if (config.CombatOverhaulConfig != null)
                {
                    config.CombatOverhaulConfig.Domain = domain;
                    result.Add($"{domain}:{code}", config.CombatOverhaulConfig);
                }
            }
            catch (Exception exception)
            {
                Utils.LoggerUtil.Error(api, this, $"Exception when trying to load model config '{asset.Location}' for model '{code}':\n{exception}");
            }
        }

        return result;
    }
}
