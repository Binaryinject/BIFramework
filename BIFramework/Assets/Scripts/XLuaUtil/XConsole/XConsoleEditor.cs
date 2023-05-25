#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;
using System;
using UnityEditor;
using XConsole;


/// <summary>
/// The basic editor logger backend
/// This is seperate from the editor frontend, so we can have multiple frontends active if we wish,
///  and so that we catch errors even without the frontend active.
/// Derived from ScriptableObject so it persists across play sessions.
/// </summary>
[System.Serializable]
public class XConsoleEditor : ScriptableObject, XConsole.ILogger {
    List<LogInfo> LogInfo = new();
    HashSet<string> Channels = new();

    public bool PauseOnError = false;
    public bool ClearOnPlay = true;
    public bool WasPlaying = false;
    public int NoErrors;
    public int NoWarnings;
    public int NoMessages;

    public static XConsoleEditor Create() {
        var editorDebug = FindObjectOfType<XConsoleEditor>();

        if (editorDebug == null) {
            editorDebug = CreateInstance<XConsoleEditor>();
        }

        editorDebug.NoErrors = 0;
        editorDebug.NoWarnings = 0;
        editorDebug.NoMessages = 0;

        return editorDebug;
    }

    public void OnEnable() {
        EditorApplication.playModeStateChanged += OnPlaymodeStateChanged;

        //Make this scriptable object persist between Play sessions
        hideFlags = HideFlags.HideAndDontSave;
    }

    /// <summary>
    /// If we're about to start playing and 'ClearOnPlay' is set, clear the current logs
    /// </summary>
    public void ProcessOnStartClear() {
        if (!WasPlaying && EditorApplication.isPlayingOrWillChangePlaymode) {
            if (ClearOnPlay) {
                Clear();
            }
        }

        WasPlaying = EditorApplication.isPlayingOrWillChangePlaymode;
    }

    void OnPlaymodeStateChanged(PlayModeStateChange obj) {
        ProcessOnStartClear();
    }


    /// <summary>
    /// Interface for deriving new logger backends.
    /// Add a new logger via Logger.AddLogger()
    /// </summary>
    public interface ILoggerWindow {
        /// <summary>
        /// Logging backend entry point. logInfo contains all the information about the logging request.
        /// </summary>
        void OnLogChange(LogInfo logInfo);
    }

    List<ILoggerWindow> Windows = new();

    public void AddWindow(ILoggerWindow window) {
        Windows.Clear();
        Windows.Add(window);
    }

    public void Log(LogInfo logInfo) {
        lock (this) {
            if (!String.IsNullOrEmpty(logInfo.Channel) && !Channels.Contains(logInfo.Channel)) {
                Channels.Add(logInfo.Channel);
            }

            LogInfo.Add(logInfo);
        }

        if (logInfo.Severity == LogSeverity.Error) {
            NoErrors++;
        }
        else if (logInfo.Severity == LogSeverity.Warning) {
            NoWarnings++;
        }
        else {
            NoMessages++;
        }

        foreach (var window in Windows) {
            window.OnLogChange(logInfo);
        }

        if (logInfo.Severity == LogSeverity.Error && PauseOnError) {
            UnityEngine.Debug.Break();
        }
    }

    public void Clear(bool nativeLogsClear = true) {
        lock (this) {
            LogInfo.Clear();
            Channels.Clear();
            NoWarnings = 0;
            NoErrors = 0;
            NoMessages = 0;

            foreach (var window in Windows) {
                window.OnLogChange(null);
            }

            if (nativeLogsClear) NativeLogs.Clear();
            //compile error message
            var entries = NativeLogs.GetStackTraces();
            if (entries.Count > 0) {
                foreach (var entry in entries) {
                    if (entry.Item1 is NativeLogs.LogEntryMode.ScriptCompileError or NativeLogs.LogEntryMode.GraphCompileError) {
                        XConsole.Logger.UnityLogHandler(entry.Item2, entry.Item2, LogType.Exception);
                    }
                    else if (entry.Item1 == NativeLogs.LogEntryMode.ScriptCompileWarning) {
                        XConsole.Logger.UnityLogHandler(entry.Item2, entry.Item2, LogType.Warning);
                    }
                }
            }
        }
    }

    public List<LogInfo> CopyLogInfo() {
        lock (this) {
            return new List<LogInfo>(LogInfo);
        }
    }

    public HashSet<string> CopyChannels() {
        lock (this) {
            return new HashSet<string>(Channels);
        }
    }
}

#endif