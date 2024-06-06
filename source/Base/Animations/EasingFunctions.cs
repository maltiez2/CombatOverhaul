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
    Bounce
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

    public static EasingFunctionDelegate Get(EasingFunctionType id) => _modifiers[id];
    public static EasingFunctionDelegate Get(int id) => _modifiers[(EasingFunctionType)id];
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
