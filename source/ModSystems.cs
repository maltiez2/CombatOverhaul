using CombatOverhaul.Armor;
using CombatOverhaul.Colliders;
using CombatOverhaul.Integration;
using CombatOverhaul.PlayerAnimations;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul;


public sealed class CombatOverhaulSystem : ModSystem
{
    public const string ArmorInventoryId = "CombatOverhaul:ArmorInventory";

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CombatOverhaul:FirstPersonAnimations", typeof(FirstPersonAnimationsBehavior));
        api.RegisterEntityBehaviorClass("CombatOverhaul:EntityColliders", typeof(CollidersEntityBehavior));
        (api as ClientCoreAPI)?.ClassRegistryNative.RegisterInventoryClass(GlobalConstants.characterInvClassName, typeof(ArmorInventory));

        new Harmony("CombatOverhaulAuto").PatchAll();
    }
    public override void StartServerSide(ICoreServerAPI api)
    {

    }

    public override void Dispose()
    {
        new Harmony("CombatOverhaulAuto").UnpatchAll();
    }
}


public sealed class CombatOverhaulAnimationsSystem : ModSystem
{
    public AnimationsManager? PlayerAnimationsManager { get; private set; }

    internal IShaderProgram? AnimatedItemShaderProgram => _shaderProgram;
    internal IShaderProgram? AnimatedItemShaderProgramFirstPerson => _shaderProgramFirstPerson;

    public override void Start(ICoreAPI api)
    {
        _api = api;

        AnimatorPatch.Patch("CombatOverhaul");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += LoadAnimatedItemShaders;
        LoadAnimatedItemShaders();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {

    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi)
        {
            PlayerAnimationsManager = new(clientApi);
        }
    }

    public override void Dispose()
    {
        AnimatorPatch.Unpatch("CombatOverhaul");

        if (_api is ICoreClientAPI clientApi)
        {
            clientApi.Event.ReloadShader -= LoadAnimatedItemShaders;
        }
    }


    private ShaderProgram? _shaderProgram;
    private ShaderProgram? _shaderProgramFirstPerson;
    private ICoreAPI? _api;

    private bool LoadAnimatedItemShaders()
    {
        if (_api is not ICoreClientAPI clientApi) return false;

        _shaderProgram = clientApi.Shader.NewShaderProgram() as ShaderProgram;
        _shaderProgramFirstPerson = clientApi.Shader.NewShaderProgram() as ShaderProgram;

        if (_shaderProgram == null || _shaderProgramFirstPerson == null) return false;

        _shaderProgram.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandard", AnimatedItemShaderProgram);
        _shaderProgram.Compile();

        _shaderProgramFirstPerson.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandardfirstperson", AnimatedItemShaderProgramFirstPerson);
        _shaderProgramFirstPerson.Compile();

        return true;
    }
}


