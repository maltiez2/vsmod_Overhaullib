using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CombatOverhaul.DamageSystems;

public sealed class DamageBlockStats
{
    public readonly PlayerBodyPart ZoneType;
    public readonly DirectionConstrain Directions;
    public readonly Action<float, int, int> Callback;
    public readonly string? Sound;
    public readonly Dictionary<EnumDamageType, int>? BlockTier;
    public readonly bool CanBlockProjectiles;
    public readonly TimeSpan StaggerTime;
    public readonly int StaggerTier;

    public DamageBlockStats(PlayerBodyPart type, DirectionConstrain directions, Action<float, int, int> callback, string? sound, Dictionary<EnumDamageType, int>? blockTier, bool canBlockProjectiles, TimeSpan staggerTime, int staggerTier)
    {
        ZoneType = type;
        Directions = directions;
        Callback = callback;
        Sound = sound;
        BlockTier = blockTier;
        CanBlockProjectiles = canBlockProjectiles;
        StaggerTime = staggerTime;
        StaggerTier = staggerTier;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageBlockPacket
{
    public int Zones { get; set; }
    public float[] Directions { get; set; } = Array.Empty<float>();
    public bool MainHand { get; set; }
    public string? Sound { get; set; } = null;
    public Dictionary<EnumDamageType, int>? BlockTier { get; set; }
    public bool CanBlockProjectiles { get; set; }
    public int StaggerTimeMs { get; set; }
    public int StaggerTier { get; set; }
    public ulong Id { get; set; }

    public DamageBlockStats ToBlockStats(Action<float, int, int> callback)
    {
        return new((PlayerBodyPart)Zones, DirectionConstrain.FromArray(Directions), callback, Sound, BlockTier, CanBlockProjectiles, TimeSpan.FromMilliseconds(StaggerTimeMs), StaggerTier);
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageBlockCallbackPacket
{
    public ulong Id { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class DamageStopBlockPacket
{
    public bool MainHand { get; set; }
}

public sealed class DamageBlockJson
{
    public string[] Zones { get; set; } = Array.Empty<string>();
    public float[] Directions { get; set; } = Array.Empty<float>();
    public string? Sound { get; set; } = null;
    public Dictionary<string, int>? BlockTier { get; set; }
    public bool CanBlockProjectiles { get; set; } = true;
    public int StaggerTimeMs { get; set; } = 0;
    public int StaggerTier { get; set; } = 1;

    public DamageBlockPacket ToPacket()
    {
        return new()
        {
            Zones = (int)Zones.Select(Enum.Parse<PlayerBodyPart>).Aggregate((first, second) => first | second),
            Directions = Directions,
            Sound = Sound,
            BlockTier = BlockTier?.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value),
            CanBlockProjectiles = CanBlockProjectiles,
            StaggerTimeMs = StaggerTimeMs,
            StaggerTier = StaggerTier
        };
    }

    public DamageBlockJson Clone()
    {
        return new()
        {
            Zones = Zones,
            Directions = Directions,
            Sound = Sound,
            BlockTier = BlockTier?.ToDictionary(entry => entry.Key, entry => entry.Value),
            CanBlockProjectiles = CanBlockProjectiles,
            StaggerTimeMs = StaggerTimeMs,
            StaggerTier = StaggerTier
        };
    }
}

public sealed class MeleeBlockSystemClient : MeleeSystem
{
    public MeleeBlockSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<DamageBlockPacket>()
            .RegisterMessageType<DamageStopBlockPacket>()
            .RegisterMessageType<DamageBlockCallbackPacket>()
            .SetMessageHandler<DamageBlockCallbackPacket>(HandleCallback);
    }

    public void StartBlock(DamageBlockJson block, bool mainHand)
    {
        DamageBlockPacket packet = block.ToPacket();
        packet.Id = 0;
        packet.MainHand = mainHand;
        _clientChannel.SendPacket(packet);
    }
    public void StartBlock(DamageBlockJson block, bool mainHand, Action callback)
    {
        DamageBlockPacket packet = block.ToPacket();
        packet.Id = _nextId++;
        packet.MainHand = mainHand;

        PushCallback(packet.Id, callback);

        _clientChannel.SendPacket(packet);
    }
    public void StopBlock(bool mainHand)
    {
        _clientChannel.SendPacket(new DamageStopBlockPacket() { MainHand = mainHand });
    }
    public void HandleCallback(DamageBlockCallbackPacket packet)
    {
        if (_callbacks.TryGetValue(packet.Id, out Action? callback))
        {
            callback();
        }
    }

    private readonly IClientNetworkChannel _clientChannel;
    private readonly Queue<ulong> _ids = [];
    private readonly Dictionary<ulong, Action> _callbacks = [];
    private const int _queueSize = 10;
    private ulong _nextId = 1;

    private void PushCallback(ulong id, Action callback)
    {
        _ids.Enqueue(id);
        _callbacks[id] = callback;

        if (_ids.Count > _queueSize)
        {
            ulong idToRemove = _ids.Dequeue();
            _callbacks.Remove(idToRemove);
        }
    }
}

public interface IHasServerBlockCallback
{
    public void BlockCallback(IServerPlayer player, ItemSlot slot, bool mainHand, float damageBlocked, int attackTier, int blockTier);
}

public sealed class MeleeBlockSystemServer : MeleeSystem
{
    public MeleeBlockSystemServer(ICoreServerAPI api)
    {
        _api = api;
        _serverChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<DamageBlockPacket>()
            .RegisterMessageType<DamageStopBlockPacket>()
            .RegisterMessageType<DamageBlockCallbackPacket>()
            .SetMessageHandler<DamageBlockPacket>(HandlePacket)
            .SetMessageHandler<DamageStopBlockPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;
    private readonly IServerNetworkChannel _serverChannel;
    private bool _lastBlockMainHand = false;

    private void HandlePacket(IServerPlayer player, DamageBlockPacket packet)
    {
        PlayerDamageModelBehavior behavior = player.Entity.GetBehavior<PlayerDamageModelBehavior>();
        if (behavior != null)
        {
            _lastBlockMainHand = packet.MainHand;
            behavior.CurrentDamageBlock = packet.ToBlockStats((damageBlocked, attackTier, blockTier) => BlockCallback(player, packet.MainHand, damageBlocked, attackTier, blockTier, packet.Id));
        }
    }

    private void HandlePacket(IServerPlayer player, DamageStopBlockPacket packet)
    {
        PlayerDamageModelBehavior behavior = player.Entity.GetBehavior<PlayerDamageModelBehavior>();
        if (behavior != null && _lastBlockMainHand == packet.MainHand)
        {
            behavior.CurrentDamageBlock = null;
        }
    }

    private void BlockCallback(IServerPlayer player, bool mainHand, float damageBlocked, int attackTier, int blockTier, ulong id)
    {
        ItemSlot slot = mainHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;

        IHasServerBlockCallback? item = slot.Itemstack?.Collectible?.GetCollectibleInterface<IHasServerBlockCallback>();

        if (item == null) return;

        item.BlockCallback(player, slot, mainHand, damageBlocked, attackTier, blockTier);

        if (id != 0)
        {
            _serverChannel?.SendPacket(new DamageBlockCallbackPacket() { Id = id }, player);
        }
    }
}