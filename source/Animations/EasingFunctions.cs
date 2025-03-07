using Vintagestory.API.MathTools;

namespace CombatOverhaul.Animations;

/// <summary>
/// Default option usually is <see cref="Linear"/><br/><br/>
/// Animations themselves is smoothed by Animator itself, but animation speed is not, that can result in more harsh and robotic animations.<br/>
/// Animation speed can modified and smoothed by applying <see cref="EasingFunctions.EasingFunctionDelegate"/> to animation progress.<br/>
/// In the future automatic smoothing for animation speed will be added.<br/><br/>
/// </summary>
/// <remarks>
/// There are two groups of <see cref="EasingFunctionType"/>: smoothing and not smoothing.<br/>
/// First group ensures that animation speed at the start and the end of animation is zero.<br/><br/>
/// It consists of:
/// <list type="bullet">
///     <item><see cref="SinQuadratic"/></item>
///     <item><see cref="SinQuartic"/></item>
///     <item><see cref="CosShifted"/></item>
///     <item><see cref="Bounce"/></item>
/// </list>
/// Also <see cref="Sin"/> has similar property: speed at the end of animation is zero, but not in the start.<br/>
/// All other modifiers do not smooth animation speed. But they still useful when this smoothing is not required.<br/><br/>
/// 
/// <b>Example of modifiers use cases for attack animations:</b><br/><br/>
/// For hit animations try using:
/// <list type="bullet">
///     <item><see cref="Quadratic"/></item>
///     <item><see cref="Cubic"/></item>
///     <item><see cref="Quintic"/></item>
/// </list>
/// For ease out after hit:
/// <list type="bullet">
///     <item><see cref="Sqrt"/></item>
///     <item><see cref="SqrtSqrt"/></item>
///     <item><see cref="Sin"/></item>
/// </list>
/// For full attack animation that has hit key-frame in the middle try using: <see cref="CosShifted"/><br/><br/>
/// For moves between stances:
/// <list type="bullet">
///     <item><see cref="SinQuadratic"/></item>
///     <item><see cref="SinQuartic"/></item>
///     <item><see cref="Bounce"/></item>
/// </list>
/// </remarks>
public enum EasingFunctionType
{
    /// <summary>
    /// Animation speed stays the same through whole animation.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+x+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+x+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    Linear,
    /// <summary>
    /// Starts slower and speeds up with time.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+x*x+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+x*x+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    Quadratic,
    /// <summary>
    /// More dramatic version of <see cref="Quadratic"/>
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+x*x*x+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+x*x*x+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    Cubic,
    /// <summary>
    /// Even more dramatic version of <see cref="Quadratic"/>
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+x*x*x*x*x+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+x*x*x*x*x+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    Quintic,
    /// <summary>
    /// Starts much faster and slows down with time.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+sqrt+x+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+sqrt+x+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    Sqrt,
    /// <summary>
    /// More dramatic version of <see cref="Sqrt"/>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+sqrt+sqrt+x+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+sqrt+sqrt+x+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    SqrtSqrt,
    /// <summary>
    /// Starts faster and slows down to zero.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+Sin+x*pi*0.5+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+Sin+x*pi*0.5+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    Sin,
    /// <summary>
    /// Starts slower, speeds up and then slows down to zero.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+Sin+x*x*pi*0.5+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+Sin+x*x*pi*0.5+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    SinQuadratic,
    /// <summary>
    /// More dramatic version of <see cref="SinQuadratic"/>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+Sin+x*x*x*x*pi*0.5+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+Sin+x*x*x*x*pi*0.5+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    SinQuartic,
    /// <summary>
    /// Starts slow, ends slow, symmetrical.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+0.5-0.5*Cos+x*pi+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+0.5-0.5*Cos+x*pi+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    CosShifted,
    /// <summary>
    /// Starts slow, ends slow, has a bump at the end that overshoots animation a bit and returns back.<br/>
    /// Version of <see cref="SinQuadratic"/> but with bounce.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+0.5-0.5*Cos+x*pi+%2B+0.35*Sin^2+x*pi+from+0+to+1">Progress curve</seealso>.
    ///  <seealso href="https://www.wolframalpha.com/input?i=plot+derivative+0.5-0.5*Cos+x*pi+%2B+0.35*Sin^2+x*pi+from+0+to+1">Speed curve</seealso>.
    /// </summary>
    Bounce,

    // Standard easing functions from https://easings.net/
    EaseInSine,
    EaseOutSine,
    EaseInOutSine,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutQuad,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic,
    EaseInQuart,
    EaseOutQuart,
    EaseInOutQuart,
    EaseInQuint,
    EaseOutQuint,
    EaseInOutQuint,
    EaseInExpo,
    EaseOutExpo,
    EaseInOutExpo,
    EaseInCirc,
    EaseOutCirc,
    EaseInOutCirc,
    EaseInBack,
    EaseOutBack,
    EaseInOutBack,
    EaseInElastic,
    EaseOutElastic,
    EaseInOutElastic,
    EaseInBounce,
    EaseOutBounce,
    EaseInOutBounce
}

/// <summary>
/// Stores all the <see cref="EasingFunctions.EasingFunctionDelegate"/> available to animations.
/// Custom modifiers can be registered.
/// </summary>
static public class EasingFunctions // @TODO add clean up on mod system dispose
{
    public delegate float EasingFunctionDelegate(float progress);

    private readonly static Dictionary<EasingFunctionType, EasingFunctionDelegate> _modifiers = new()
    {
        { EasingFunctionType.Linear,       (float progress) => progress },
        { EasingFunctionType.Quadratic,    (float progress) => progress * progress },
        { EasingFunctionType.Cubic,        (float progress) => progress * progress * progress },
        { EasingFunctionType.Quintic,      (float progress) => progress * progress * progress * progress * progress },
        { EasingFunctionType.Sqrt,         (float progress) => GameMath.Sqrt(progress) },
        { EasingFunctionType.Sin,          (float progress) => GameMath.Sin(progress / 2 * GameMath.PI) },
        { EasingFunctionType.SinQuadratic, (float progress) => GameMath.Sin(progress * progress / 2 * GameMath.PI) },
        { EasingFunctionType.SinQuartic,   (float progress) => GameMath.Sin(progress * progress * progress * progress / 2 * GameMath.PI) },
        { EasingFunctionType.CosShifted,   (float progress) => 0.5f - GameMath.Cos(progress * GameMath.PI) / 2 },
        { EasingFunctionType.SqrtSqrt,     (float progress) => GameMath.Sqrt(GameMath.Sqrt(progress)) },
        { EasingFunctionType.Bounce,       (float progress) => 0.5f - GameMath.Cos(progress * GameMath.PI) / 2 + MathF.Pow(GameMath.Sin(progress * GameMath.PI), 2) * 0.35f },
    };

    public static EasingFunctionDelegate Get(EasingFunctionType id) => id <= EasingFunctionType.Bounce ? _modifiers[id] : (float x) => StandardEasingFunctions.Calculate(id, x);
    public static EasingFunctionDelegate Get(int id) => Get((EasingFunctionType)id);
    public static EasingFunctionDelegate Get(string name) => Get((EasingFunctionType)Enum.Parse(typeof(EasingFunctionType), name));
    /// <summary>
    /// Registers <see cref="EasingFunctionDelegate"/> by given id.<br/>
    /// It is better to use <see cref="Register(string,EasingFunctionDelegate)"/> to avoid conflicts.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="modifier"></param>
    /// <returns><c>false</c> if <paramref name="id"/> already registered</returns>
    public static bool Register(int id, EasingFunctionDelegate modifier) => _modifiers.TryAdd((EasingFunctionType)id, modifier);
    /// <summary>
    /// Registers <see cref="EasingFunctionDelegate"/> by given name. Name should be unique across mods.
    /// </summary>
    /// <param name="name">Unique name of modifier</param>
    /// <param name="modifier"></param>
    /// <returns><c>false</c> if <paramref name="name"/> already registered, or it has hash conflict with another registered <paramref name="name"/></returns>
    public static bool Register(string name, EasingFunctionDelegate modifier) => _modifiers.TryAdd((EasingFunctionType)ToCrc32(name), modifier);

    internal static uint ToCrc32(string value) => GameMath.Crc32(value.ToLowerInvariant()) & int.MaxValue;
}

/// <summary>
/// https://easings.net/
/// </summary>
static public class StandardEasingFunctions
{
    public const float BackC1 = 1.70158f;
    public const float BackC2 = BackC1 * 1.525f;
    public const float BackC3 = BackC1 + 1;
    public const float ElasticC1 = 2f * MathF.PI / 3f;
    public const float ElasticC2 = 2f * MathF.PI / 4.5f;
    public const float BounceN1 = 7.5625f;
    public const float BounceD1 = 2.75f;

    public static float Calculate(EasingFunctionType function, float x)
    {
        return function switch
        {
            EasingFunctionType.Linear => x,
            EasingFunctionType.EaseInSine => 1 - MathF.Cos((x * MathF.PI) / 2),
            EasingFunctionType.EaseOutSine => MathF.Sin((x * MathF.PI) / 2),
            EasingFunctionType.EaseInOutSine => -(MathF.Cos(MathF.PI * x) - 1) / 2,
            EasingFunctionType.EaseInQuad => x * x,
            EasingFunctionType.EaseOutQuad => 1 - (1 - x) * (1 - x),
            EasingFunctionType.EaseInOutQuad => x < 0.5 ? 2 * x * x : 1 - MathF.Pow(-2 * x + 2, 2) / 2,
            EasingFunctionType.EaseInCubic => x * x * x,
            EasingFunctionType.EaseOutCubic => 1 - MathF.Pow(1 - x, 3),
            EasingFunctionType.EaseInOutCubic => x < 0.5 ? 4 * x * x * x : 1 - MathF.Pow(-2 * x + 2, 3) / 2,
            EasingFunctionType.EaseInQuart => x * x * x * x,
            EasingFunctionType.EaseOutQuart => 1 - MathF.Pow(1 - x, 4),
            EasingFunctionType.EaseInOutQuart => x < 0.5 ? 8 * x * x * x * x : 1 - MathF.Pow(-2 * x + 2, 4) / 2,
            EasingFunctionType.EaseInQuint => x * x * x * x * x,
            EasingFunctionType.EaseOutQuint => 1 - MathF.Pow(1 - x, 5),
            EasingFunctionType.EaseInOutQuint => x < 0.5 ? 16 * x * x * x * x * x : 1 - MathF.Pow(-2 * x + 2, 5) / 2,
            EasingFunctionType.EaseInExpo => x == 0 ? 0 : MathF.Pow(2, 10 * x - 10),
            EasingFunctionType.EaseOutExpo => x == 1 ? 1 : 1 - MathF.Pow(2, -10 * x),
            EasingFunctionType.EaseInOutExpo => x == 0 ? 0 : x == 1 ? 1 : x < 0.5 ? MathF.Pow(2, 20 * x - 10) / 2 : (2 - MathF.Pow(2, -20 * x + 10)) / 2,
            EasingFunctionType.EaseInCirc => 1 - MathF.Sqrt(1 - MathF.Pow(x, 2)),
            EasingFunctionType.EaseOutCirc => MathF.Sqrt(1 - MathF.Pow(x - 1, 2)),
            EasingFunctionType.EaseInOutCirc => x < 0.5 ? (1 - MathF.Sqrt(1 - MathF.Pow(2 * x, 2))) / 2 : (MathF.Sqrt(1 - MathF.Pow(-2 * x + 2, 2)) + 1) / 2,
            EasingFunctionType.EaseInBack => BackC3 * x * x * x - BackC1 * x * x,
            EasingFunctionType.EaseOutBack => 1 + BackC3 * MathF.Pow(x - 1, 3) + BackC1 * MathF.Pow(x - 1, 2),
            EasingFunctionType.EaseInOutBack => x < 0.5 ? (MathF.Pow(2 * x, 2) * ((BackC2 + 1) * 2 * x - BackC2)) / 2 : (MathF.Pow(2 * x - 2, 2) * ((BackC2 + 1) * (x * 2 - 2) + BackC2) + 2) / 2,
            EasingFunctionType.EaseInElastic => x == 0 ? 0 : x == 1 ? 1 : -MathF.Pow(2, 10 * x - 10) * MathF.Sin((x * 10 - 10.75f) * ElasticC1),
            EasingFunctionType.EaseOutElastic => x == 0 ? 0 : x == 1 ? 1 : MathF.Pow(2, -10 * x) * MathF.Sin((x * 10 - 0.75f) * ElasticC1) + 1,
            EasingFunctionType.EaseInOutElastic => x == 0 ? 0 : x == 1 ? 1 : x < 0.5 ? -(MathF.Pow(2, 20 * x - 10) * MathF.Sin((20 * x - 11.125f) * ElasticC2)) / 2 : (MathF.Pow(2, -20 * x + 10) * MathF.Sin((20 * x - 11.125f) * ElasticC2)) / 2 + 1,
            EasingFunctionType.EaseInBounce => 1 - Bounce(1 - x),
            EasingFunctionType.EaseOutBounce => Bounce(x),
            EasingFunctionType.EaseInOutBounce => x < 0.5 ? (1 - Bounce(1 - 2 * x)) / 2 : (1 + Bounce(2 * x - 1)) / 2,
            _ => x
        };
    }

    public static float Bounce(float x)
    {
        if (x < 1 / BounceD1)
        {
            return BounceN1 * x * x;
        }
        else if (x < 2 / BounceD1)
        {
            return BounceN1 * ((x - 1.5f) / BounceD1) * (x - 1.5f) + 0.75f;
        }
        else if (x < 2.5 / BounceD1)
        {
            return BounceN1 * ((x - 2.25f) / BounceD1) * (x - 2.25f) + 0.9375f;
        }
        else
        {
            return BounceN1 * ((x - 2.625f) / BounceD1) * (x - 2.625f) + 0.984375f;
        }
    }
}
