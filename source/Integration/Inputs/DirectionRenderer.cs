using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace CombatOverhaul.Inputs;

public sealed class DirectionCursorRenderer : IRenderer
{
    public bool Show { get; set; } = true;
    public int CurrentDirection
    {
        get => _currentDirection;
        set
        {
            _currentDirection = Math.Clamp(value, 0, NumDirections - 1);
        }
    }
    public float CursorScale { get; set; } = 1.0f;
    public float Alpha { get; set; } = 1.0f;

    public const int NumDirections = 8;
    public const float ScaleMultiplier = 0.5f;

    public double RenderOrder => 0.98;
    public int RenderRange => 9999;


    public DirectionCursorRenderer(ICoreClientAPI api)
    {
        _clientApi = api;

        for (int index = 0; index < NumDirections; index++)
        {
            LoadedTexture cursorTexture = new(api);

            api.Render.GetOrLoadTexture(new AssetLocation("combatoverhaul", $"gui/direction-cursor-{index}.png"), ref cursorTexture);

            _directionCursorTextures.Add(cursorTexture);
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!Show) return;

        if (!_clientApi.Input.MouseGrabbed) return;


        LoadedTexture texture = _directionCursorTextures[_currentDirection];

        float reticleScale = CursorScale * RuntimeEnv.GUIScale * ScaleMultiplier;

        _clientApi.Render.Render2DTexture(texture.TextureId,
            (_clientApi.Render.FrameWidth / 2) - (texture.Width * reticleScale / 2),
            (_clientApi.Render.FrameHeight / 2) - (texture.Height * reticleScale / 2),
            texture.Width * reticleScale, texture.Height * reticleScale, 10000f, new(1, 1, 1, Alpha));
    }

    public void Dispose()
    {
        foreach (LoadedTexture texture in _directionCursorTextures)
        {
            texture.Dispose();
        }
    }

    private readonly List<LoadedTexture> _directionCursorTextures = new();
    private readonly ICoreClientAPI _clientApi;
    private int _currentDirection = 0;
}