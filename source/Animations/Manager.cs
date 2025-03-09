using CombatOverhaul.Colliders;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Animations;

public sealed class AnimationsManager
{
    public Dictionary<string, Animation> Animations { get; private set; } = new();

    public AnimationsManager(ICoreClientAPI api, ParticleEffectsManager particleEffectsManager)
    {
        _instance = this;

        _api = api;
        _colliders.Clear();
    }
    public void Load()
    {
        List<IAsset> animations = _api.Assets.GetManyInCategory("config", "animations");

        Dictionary<string, Animation> animationsByCode = new();
        foreach (Dictionary<string, Animation> assetAnimations in animations.Select(FromAsset))
        {
            foreach ((string code, Animation animation) in assetAnimations)
            {
                animationsByCode.Add(code, animation);
            }
        }

        Animations = animationsByCode;
    }

    [Obsolete("Use one from DebugWindowManager")]
    public static void RegisterTransformByCode(ModelTransform transform, string code) => DebugWindowManager.RegisterTransformByCode(transform, code);
    [Obsolete("Use one from DebugWindowManager")]
    public void RegisterTransform(ModelTransform transform, string code) => DebugWindowManager._instance.RegisterTransform(transform, code);
    [Obsolete("Use one from DebugWindowManager")]
    public static void RegisterCollider(string item, string type, MeleeDamageType collider) => DebugWindowManager.RegisterCollider(item, type, collider);
    [Obsolete("Use one from DebugWindowManager")]
    public static void RegisterCollider(string item, string type, Action<LineSegmentCollider> setter, System.Func<LineSegmentCollider> getter) => DebugWindowManager.RegisterCollider(item, type, setter, getter);

    private readonly ICoreClientAPI _api;
    private static Dictionary<string, Dictionary<string, (Action<LineSegmentCollider> setter, System.Func<LineSegmentCollider> getter)>> _colliders = new();
    internal static AnimationsManager _instance;

    private Dictionary<string, Animation> FromAsset(IAsset asset)
    {
        Dictionary<string, Animation> result = new();

        string domain = asset.Location.Domain;

        JsonObject json;

        try
        {
            json = JsonObject.FromJson(asset.ToText());
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(_api, this, $"Error on parsing animations file '{asset.Location}'.\nException: {exception}");
            return result;
        }

        foreach (KeyValuePair<string, JToken?> entry in json.Token as JObject)
        {
            string code = entry.Key;

            try
            {
                JsonObject animationJson = new(entry.Value);

                Animation animation = animationJson.AsObject<AnimationJson>().ToAnimation();

                result.Add($"{domain}:{code}", animation);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(_api, this, $"Error on parsing animation '{code}' from '{asset.Location}'.\nException: {exception}");
            }
        }

        return result;
    }
}