using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using OpenTK.Mathematics;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleeAttackPacket
{
    public MeleeDamagePacket[] MeleeAttackDamagePackets { get; set; }
}

public abstract class MeleeSystem
{
    public const string NetworkChannelId = "CombatOverhaul:damage-packets";
}

public readonly struct AttackId
{
    public readonly int ItemId;
    public readonly int Id;

    public AttackId(int itemId, int id)
    {
        ItemId = itemId;
        Id = id;
    }
}

public sealed class MeleeSystemClient : MeleeSystem
{
    public MeleeSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>();
    }

    public void SendPackets(IEnumerable<MeleeDamagePacket> packets)
    {
        _clientChannel.SendPacket(new MeleeAttackPacket
        {
            MeleeAttackDamagePackets = packets.ToArray()
        });
    }

    private readonly IClientNetworkChannel _clientChannel;
}

public sealed class MeleeSystemServer : MeleeSystem
{
    public delegate void MeleeDamageDelegate(Entity target, DamageSource damageSource, ItemSlot? slot, ref float damage);

    public event MeleeDamageDelegate? OnDealMeleeDamage;

    public MeleeSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>()
            .SetMessageHandler<MeleeAttackPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;

    private void HandlePacket(IServerPlayer player, MeleeAttackPacket packet)
    {
        foreach (MeleeDamagePacket damagePacket in packet.MeleeAttackDamagePackets)
        {
            Attack(damagePacket);
        }
    }

    private void Attack(MeleeDamagePacket packet)
    {
        Entity? target = _api.World.GetEntityById(packet.TargetEntityId);

        if (target == null || !target.Alive) return;

        Entity attacker = _api.World.GetEntityById(packet.AttackerEntityId);
        string targetName = target.GetName();

        IServerPlayer? serverPlayer = (attacker as EntityPlayer)?.Player as IServerPlayer;
        if (serverPlayer != null)
        {
            if (target is EntityPlayer && (!_api.Server.Config.AllowPvP || !serverPlayer.HasPrivilege("attackplayers")))
            {
                return;
            }

            if (target is EntityAgent && !serverPlayer.HasPrivilege("attackcreatures"))
            {
                return;
            }
        }

        ItemSlot? slot = (packet.MainHand ? (attacker as EntityAgent)?.RightHandItemSlot : (attacker as EntityAgent)?.LeftHandItemSlot);

        DirectionalTypedDamageSource damageSource = new()
        {
            Source = attacker is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
            SourceEntity = attacker,
            CauseEntity = attacker,
            DamageTypeData = new DamageData(Enum.Parse<EnumDamageType>(packet.DamageType), packet.Strength),
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Collider = packet.Collider,
            KnockbackStrength = packet.Knockback,
            DamageTier = (int)packet.Strength,
            Type = Enum.Parse<EnumDamageType>(packet.DamageType),
            Weapon = packet.MainHand ? serverPlayer?.Entity.RightHandItemSlot.Itemstack : serverPlayer?.Entity.LeftHandItemSlot.Itemstack
        };

        bool damageReceived = DealDamage(target, damageSource, slot, packet.Damage);

        DealDurabilityDamage(slot, packet, attacker);

        PrintLog(attacker, damageReceived, target, packet, targetName);
    }

    private bool DealDamage(Entity target, DamageSource damageSource, ItemSlot? slot, float damage)
    {
        OnDealMeleeDamage?.Invoke(target, damageSource, slot, ref damage);

        return target.ReceiveDamage(damageSource, damage);
    }

    private void DealDurabilityDamage(ItemSlot? slot, MeleeDamagePacket packet, Entity? attacker)
    {
        if (packet.DurabilityDamage <= 0) return;

        if (slot?.Itemstack?.Collectible != null && attacker != null)
        {
            slot.Itemstack.Collectible.DamageItem(attacker.Api.World, attacker, slot, packet.DurabilityDamage);
            slot.MarkDirty();
        }
    }

    private void PrintLog(Entity? attacker, bool damageReceived, Entity target, MeleeDamagePacket packet, string targetName)
    {
        bool printIntoChat = _api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings.PrintMeleeHits;

        if (printIntoChat)
        {
            float damage = damageReceived ? target.WatchedAttributes.GetFloat("onHurt") : 0;

            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-dealt-damage", Lang.Get($"combatoverhaul:entity-damage-zone-{(ColliderTypes)packet.ColliderType}"), targetName, $"{damage:F2}");

            ((attacker as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, damageLogMessage, EnumChatType.Notification);
        }
    }
}