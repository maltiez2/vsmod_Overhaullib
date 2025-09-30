using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatOverhaul;

public interface IFueledItem
{
    double GetFuelHours(IPlayer player, ItemSlot slot);
    void AddFuelHours(IPlayer player, ItemSlot slot, double hours);
    bool ConsumeFuelWhenSleeping(IPlayer player, ItemSlot slot);
}

public interface ITogglableItem
{
    string HotKeyCode { get; }

    bool TurnedOn(IPlayer player, ItemSlot slot);
    void TurnOn(IPlayer player, ItemSlot slot);
    void TurnOff(IPlayer player, ItemSlot slot);
    void Toggle(IPlayer player, ItemSlot slot);
}

public sealed class FueledItemSystem : ModSystem, IRenderer
{
    public double RenderOrder => 0;
    public int RenderRange => 1;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "nightvisionCO");
        api.Event.LevelFinalize += OnLevelFinalize;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        _serverApi = api;
        api.Event.RegisterGameTickListener(OnServerTick, 1000, 200);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_playerInventoryBehavior?.Inventory == null || _clientApi == null) return;

        ItemSlot? slot = _playerInventoryBehavior.Inventory.FirstOrDefault(slot => slot.Itemstack?.Collectible is ItemNightvisiondevice);

        double fuelLeft = (slot?.Itemstack?.Collectible as ItemNightvisiondevice)?.GetFuelHours(slot.Itemstack) ?? 0;

        if (fuelLeft > 0)
        {
            _clientApi.Render.ShaderUniforms.NightVisionStrength = (float)GameMath.Clamp(fuelLeft * 20, 0, 0.8);
        }
        else
        {
            _clientApi.Render.ShaderUniforms.NightVisionStrength = 0;
        }
    }

    private double _lastCheckTotalHours;
    private ICoreClientAPI? _clientApi;
    private ICoreServerAPI? _serverApi;
    private EntityBehaviorPlayerInventory? _playerInventoryBehavior;
    private const double _updatePeriodHours = 0.1;

    private void OnServerTick(float dt)
    {
        if (_serverApi == null) return;

        double totalHours = _serverApi.World.Calendar.TotalHours;
        double hoursPassed = totalHours - _lastCheckTotalHours;

        if (hoursPassed < _updatePeriodHours) return;

        foreach (IPlayer? player in _serverApi.World.AllOnlinePlayers)
        {
            IInventory? inventory = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inventory == null) continue;

            ItemSlot? slot = inventory.FirstOrDefault(slot => slot.Itemstack?.Collectible is ItemNightvisiondevice);

            if (slot?.Itemstack?.Collectible is ItemNightvisiondevice device)
            {
                device.AddFuelHours(slot.Itemstack, -hoursPassed);
                slot.MarkDirty();
            }
        }

        foreach (IPlayer? player in _serverApi.World.AllOnlinePlayers)
        {
            IInventory? inventory = player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inventory == null) continue;

            foreach (ItemSlot slot in inventory)
            {
                IFueledItem? item = slot.Itemstack?.Collectible?.GetCollectibleInterface<IFueledItem>();
                if (item == null) continue;

                if (IsSleeping(player.Entity) && !item.ConsumeFuelWhenSleeping(player, slot)) continue;

                item.AddFuelHours(player, slot, -hoursPassed);
                slot.MarkDirty();
            }
        }

        _lastCheckTotalHours = totalHours;
    }

    private bool IsSleeping(EntityPlayer ep) => ep.GetBehavior<EntityBehaviorTiredness>()?.IsSleeping == true;

    private void OnLevelFinalize()
    {
        _playerInventoryBehavior = _clientApi?.World?.Player?.Entity?.GetBehavior<EntityBehaviorPlayerInventory>();
    }
}