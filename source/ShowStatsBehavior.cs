using System.Text;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.source;

public class ShowStatsBehavior : EntityBehavior
{
    public List<string[]> Entries { get; set; } = [];
    
    public ShowStatsBehavior(Entity entity) : base(entity)
    {
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        Entries = attributes["entries"].AsObject<List<string[]>>([]);
    }

    public override string PropertyName() => "ShowStatsBehavior";

    public override void GetInfoText(StringBuilder infotext)
    {
        foreach (string[] entry in Entries)
        {
            if (entry.Length == 0)
            {
                infotext.AppendLine();
                continue;
            }

            if (entry.Length == 1)
            {
                infotext.AppendLine(Lang.Get(entry[0]));
                continue;
            }

            infotext.AppendLine(Lang.Get(entry[0], entry[1 ..].Select(parameter => Lang.Get(parameter)).ToArray()));
        }
    }
}
