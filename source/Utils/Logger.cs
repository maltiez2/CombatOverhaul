using System.Runtime.CompilerServices;
using Vintagestory.API.Common;

namespace CombatOverhaul.Utils;

public static class LoggerUtil
{
    private const string _prefix = "[Combat Overhaul]";

    public static void Notify(ICoreAPI? api, object caller, string format) => api?.Logger?.Notification(Format(caller, format));
    public static void Notify(ICoreAPI? api, Type type, string format) => api?.Logger?.Notification(Format(type, format));

    public static void Warn(ICoreAPI? api, object caller, string format) => api?.Logger?.Warning(Format(caller, format));
    public static void Warn(ICoreAPI? api, Type type, string format) => api?.Logger?.Warning(Format(type, format));

    public static void Error(ICoreAPI? api, object caller, string format) => api?.Logger?.Error(Format(caller, format));
    public static void Error(ICoreAPI? api, Type type, string format) => api?.Logger?.Error(Format(type, format));

    public static void Debug(ICoreAPI? api, object caller, string format) => api?.Logger?.Debug(Format(caller, format));
    public static void Debug(ICoreAPI? api, Type type, string format) => api?.Logger?.Debug(Format(type, format));

    public static void Verbose(ICoreAPI? api, object caller, string format) => api?.Logger?.VerboseDebug(Format(caller, format));
    public static void Verbose(ICoreAPI? api, Type type, string format) => api?.Logger?.VerboseDebug(Format(type, format));

    public static void Audit(ICoreAPI? api, object caller, string format) => api?.Logger?.Audit(Format(caller, format));
    public static void Audit(ICoreAPI? api, Type type, string format) => api?.Logger?.Audit(Format(type, format));

    public static void Dev(ICoreAPI? api, object caller, string format)
    {
#if DEBUG
        api?.Logger?.Notification(Format(caller, format));
#endif
    }
    public static void Dev(ICoreAPI? api, Type type, string format)
    {
#if DEBUG
        api?.Logger?.Notification(Format(type, format));
#endif
    }
    public static void Dev(ICoreAPI? api, string format)
    {
#if DEBUG
        api?.Logger?.Notification(Format(format));
#endif
    }

    public static string Format(object caller, string format)
    {
        DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new(4, 3);
        defaultInterpolatedStringHandler.AppendFormatted(_prefix);
        defaultInterpolatedStringHandler.AppendLiteral(" [");
        defaultInterpolatedStringHandler.AppendFormatted(GetCallerTypeName(caller));
        defaultInterpolatedStringHandler.AppendLiteral("] ");
        defaultInterpolatedStringHandler.AppendFormatted(format);
        return defaultInterpolatedStringHandler.ToStringAndClear().Replace("{", "{{").Replace("}", "}}");
    }
    public static string Format(Type type, string format)
    {
        DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new(4, 3);
        defaultInterpolatedStringHandler.AppendFormatted(_prefix);
        defaultInterpolatedStringHandler.AppendLiteral(" [");
        defaultInterpolatedStringHandler.AppendFormatted(GetTypeName(type));
        defaultInterpolatedStringHandler.AppendLiteral("] ");
        defaultInterpolatedStringHandler.AppendFormatted(format);
        return defaultInterpolatedStringHandler.ToStringAndClear().Replace("{", "{{").Replace("}", "}}");
    }
    public static string Format(string format)
    {
        DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new(4, 3);
        defaultInterpolatedStringHandler.AppendFormatted(_prefix);
        defaultInterpolatedStringHandler.AppendFormatted(format);
        return defaultInterpolatedStringHandler.ToStringAndClear().Replace("{", "{{").Replace("}", "}}");
    }

    public static string GetCallerTypeName(object caller)
    {
        Type type = caller.GetType();
        if (type.IsGenericType)
        {
            string obj = type.Name.Split(new char[1] { '`' }, StringSplitOptions.RemoveEmptyEntries)[0];
            string text = type.GetGenericArguments().Select(new System.Func<Type, string>(GetTypeName)).Aggregate((string first, string second) => first + "," + second);
            return obj + "<" + text + ">";
        }

        return type.Name;
    }

    public static string GetTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            string obj = type.Name.Split(new char[1] { '`' }, StringSplitOptions.RemoveEmptyEntries)[0];
            string text = type.GetGenericArguments().Select(new System.Func<Type, string>(GetTypeName)).Aggregate((string first, string second) => first + "," + second);
            return obj + "<" + text + ">";
        }

        return type.Name;
    }
}