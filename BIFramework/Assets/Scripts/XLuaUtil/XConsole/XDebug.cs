#if !ENABLE_XLOGGING && (DEVELOPMENT_BUILD || DEBUG || UNITY_EDITOR)
#define ENABLE_XLOGGING
#endif

using XConsole;
using XLua;

//Helper functions to make logging easier
[LuaCallCSharp]
public static class XDebug
{
    [StackTraceIgnore]
    public static void Log(UnityEngine.Object context, string message, params object[] par)
    {
#if ENABLE_XLOGGING
        XConsole.Logger.Log("", context, LogSeverity.Message, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void Log(string message, params object[] par)
    {
#if ENABLE_XLOGGING
        XConsole.Logger.Log("", null, LogSeverity.Message, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogChannel(UnityEngine.Object context, string channel, string message, params object[] par)
    {
#if ENABLE_XLOGGING
        XConsole.Logger.Log(channel, context, LogSeverity.Message, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogChannel(string channel, string message, params object[] par)
    {
#if ENABLE_XLOGGING
        XConsole.Logger.Log(channel, null, LogSeverity.Message, message, par);
#endif
    }


    [StackTraceIgnore]
    public static void LogWarning(UnityEngine.Object context, object message, params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_WARNINGS)
        XConsole.Logger.Log("", context, LogSeverity.Warning, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogWarning(object message, params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_WARNINGS)
        XConsole.Logger.Log("", null, LogSeverity.Warning, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogWarningChannel(UnityEngine.Object context, string channel, string message,
        params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_WARNINGS)
        XConsole.Logger.Log(channel, context, LogSeverity.Warning, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogWarningChannel(string channel, string message, params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_WARNINGS)
        XConsole.Logger.Log(channel, null, LogSeverity.Warning, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogError(UnityEngine.Object context, object message, params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_ERRORS)
        XConsole.Logger.Log("", context, LogSeverity.Error, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogError(object message, params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_ERRORS)
        XConsole.Logger.Log("", null, LogSeverity.Error, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogErrorChannel(UnityEngine.Object context, string channel, string message, params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_ERRORS)
        XConsole.Logger.Log(channel, context, LogSeverity.Error, message, par);
#endif
    }

    [StackTraceIgnore]
    public static void LogErrorChannel(string channel, string message, params object[] par)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_ERRORS)
        XConsole.Logger.Log(channel, null, LogSeverity.Error, message, par);
#endif
    }


    //Logs that will not be caught by XConsole
    //Useful for debugging XConsole
    [LogUnityOnly]
    public static void UnityLog(object message)
    {
#if ENABLE_XLOGGING
        UnityEngine.Debug.Log(message);
#endif
    }

    [LogUnityOnly]
    public static void UnityLogWarning(object message)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_WARNINGS)
        UnityEngine.Debug.LogWarning(message);
#endif
    }

    [LogUnityOnly]
    public static void UnityLogError(object message)
    {
#if (ENABLE_XLOGGING || ENABLE_XLOGGING_ERRORS)
        UnityEngine.Debug.LogError(message);
#endif
    }
}