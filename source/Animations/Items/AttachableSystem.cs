using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CombatOverhaul.Animations;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AttachableAttachPacket
{
    public long EntityId { get; set; }
    public string AttachmentCode { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public float[] Transform { get; set; } = Array.Empty<float>();

    public AttachableAttachPacket()
    {

    }

    public AttachableAttachPacket(long entityId, string attachmentCode, ItemStack stack, ModelTransform transform)
    {
        EntityId = entityId;
        AttachmentCode = attachmentCode;
        ItemCode = stack?.Collectible?.Code?.ToString() ?? "";
        Transform = new float[12]
        {
            transform.Translation.X,
            transform.Translation.Y,
            transform.Translation.Z,
            transform.Rotation.X,
            transform.Rotation.Y,
            transform.Rotation.Z,
            transform.Origin.X,
            transform.Origin.Y,
            transform.Origin.Z,
            transform.ScaleXYZ.X,
            transform.ScaleXYZ.Y,
            transform.ScaleXYZ.Z
        };
    }

    public ItemStack GetItemStack(IWorldAccessor world)
    {
        JsonItemStack jsonStack = new()
        {
            Code = new(ItemCode),
            Type = EnumItemClass.Item
        };
        jsonStack.Resolve(world, "attach packet");

        return jsonStack.ResolvedItemstack;
    }

    public ModelTransform GetModelTransform()
    {
        return new ModelTransform()
        {
            Translation = new(Transform[0], Transform[1], Transform[2]),
            Rotation = new(Transform[3], Transform[4], Transform[5]),
            Origin = new(Transform[6], Transform[7], Transform[8]),
            ScaleXYZ = new(Transform[9], Transform[10], Transform[11])
        };
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AttachableClearPacket
{
    public long EntityId { get; set; }

    public AttachableClearPacket()
    {

    }

    public AttachableClearPacket(long entityId)
    {
        EntityId = entityId;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class AttachableSwitchModelsPacket
{
    public long EntityId { get; set; }
    public bool SwitchModels { get; set; }

    public AttachableSwitchModelsPacket()
    {

    }
    public AttachableSwitchModelsPacket(long entityId, bool switchModels)
    {
        EntityId = entityId;
        SwitchModels = switchModels;
    }
}


public sealed class AttachableSystemClient
{
    public AttachableSystemClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel("CO-attachablesystem")
            .RegisterMessageType<AttachableAttachPacket>()
            .RegisterMessageType<AttachableClearPacket>()
            .RegisterMessageType<AttachableSwitchModelsPacket>()
            .SetMessageHandler<AttachableAttachPacket>(HandlePacket)
            .SetMessageHandler<AttachableClearPacket>(HandlePacket)
            .SetMessageHandler<AttachableSwitchModelsPacket>(HandlePacket);
    }

    public void SendAttachPacket(long entityId, string attachmentCode, ItemStack stack, ModelTransform transform)
    {
        _clientChannel.SendPacket(new AttachableAttachPacket(entityId, attachmentCode, stack, transform));
    }
    public void SendClearPacket(long entityId)
    {
        _clientChannel.SendPacket(new AttachableClearPacket(entityId));
    }

    public void SendSwitchModelPacket(long entityId, bool switchModel)
    {
        _clientChannel.SendPacket(new AttachableSwitchModelsPacket(entityId, switchModel));
    }

    private readonly IClientNetworkChannel _clientChannel;
    private readonly ICoreClientAPI _api;

    private void HandlePacket(AttachableAttachPacket packet)
    {
        EntityPlayer? player = _api.World.GetEntityById(packet.EntityId) as EntityPlayer;
        if (player == null) return;

        AnimatableAttachable? attachableBehavior = player.ActiveHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior<AnimatableAttachable>(true);

        attachableBehavior?.SetAttachment(packet.EntityId, packet.AttachmentCode, packet.GetItemStack(player.Api.World), packet.GetModelTransform());
    }
    private void HandlePacket(AttachableClearPacket packet)
    {
        EntityPlayer? player = _api.World.GetEntityById(packet.EntityId) as EntityPlayer;
        if (player == null) return;

        AnimatableAttachable? attachableBehavior = player.ActiveHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior<AnimatableAttachable>(true);

        attachableBehavior?.ClearAttachments(packet.EntityId);
    }
    private void HandlePacket(AttachableSwitchModelsPacket packet)
    {
        EntityPlayer? player = _api.World.GetEntityById(packet.EntityId) as EntityPlayer;
        if (player == null) return;

        AnimatableAttachable? attachableBehavior = player.ActiveHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior<AnimatableAttachable>(true);

        attachableBehavior?.SetSwitchModels(packet.EntityId, packet.SwitchModels);
    }
}

public sealed class AttachableSystemServer
{
    public AttachableSystemServer(ICoreServerAPI api)
    {
        _serverChannel = api.Network.RegisterChannel("CO-attachablesystem")
            .RegisterMessageType<AttachableAttachPacket>()
            .RegisterMessageType<AttachableClearPacket>()
            .RegisterMessageType<AttachableSwitchModelsPacket>()
            .SetMessageHandler<AttachableAttachPacket>(HandlePacket)
            .SetMessageHandler<AttachableSwitchModelsPacket>(HandlePacket)
            .SetMessageHandler<AttachableClearPacket>(HandlePacket);
    }

    private readonly IServerNetworkChannel _serverChannel;

    private void HandlePacket(IServerPlayer player, AttachableAttachPacket packet)
    {
        _serverChannel.BroadcastPacket(packet, player);
    }
    private void HandlePacket(IServerPlayer player, AttachableClearPacket packet)
    {
        _serverChannel.BroadcastPacket(packet, player);
    }
    private void HandlePacket(IServerPlayer player, AttachableSwitchModelsPacket packet)
    {
        _serverChannel.BroadcastPacket(packet, player);
    }
}