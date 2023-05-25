using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace XConsole {
    public static class NativeLogs {
        public enum LogEntryMode {
            Error = 1,
            Assert,
            Log = 4,
            Fatal = 16,
            DontPreprocessCondition = 32,
            AssetImportError = 64,
            AssetImportWarning = 128,
            ScriptingError = 256,
            ScriptingWarning = 512,
            ScriptingLog = 1024,
            ScriptCompileError = 2048,
            ScriptCompileWarning = 4096,
            StickyError = 8192,
            MayIgnoreLineNumber = 16384,
            ReportBug = 32768,
            DisplayPreviousErrorInStatusBar = 65536,
            ScriptingException = 131072,
            DontExtractStacktrace = 262144,
            ShouldClearOnPlay = 524288,
            GraphCompileError = 1048576,
            ScriptingAssertion = 2097152
        }

        public static (int, string) GetStackText(int row) {
            var LogEntriesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries") ??
                                 typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.LogEntries");
            var startGettingEntriesMethod = LogEntriesType.GetMethod("StartGettingEntries",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var endGettingEntriesMethod = LogEntriesType.GetMethod("EndGettingEntries",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            startGettingEntriesMethod.Invoke(null, new object[0]);
            var GetEntryInternalMethod = LogEntriesType.GetMethod("GetEntryInternal",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var logEntryType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntry") ??
                               typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.LogEntry");


            var logEntry = Activator.CreateInstance(logEntryType);
            //Get detail debug info.
            GetEntryInternalMethod.Invoke(null, new[] {row, logEntry});
            //More info please search "UnityEditorInternal.LogEntry" class of ILSPY.
            var fieldInfo = logEntryType.GetField("message",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var stack = fieldInfo.GetValue(logEntry).ToString();
            fieldInfo = logEntryType.GetField("mode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var mode = (int) fieldInfo.GetValue(logEntry);
            endGettingEntriesMethod.Invoke(null, new object[0]);
            return (mode, stack);
        }

        public static void Clear() {
            var debugType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries") ??
                            typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.LogEntries");
            var methodInfo = debugType.GetMethod("Clear",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            methodInfo.Invoke(null, new object[0]);
        }

        public static int GetCount() {
            var debugType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries") ??
                            typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.LogEntries");
            var methodInfo = debugType.GetMethod("GetCount",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return (int) methodInfo.Invoke(null, new object[0]);
        }

        public static List<Tuple<LogEntryMode, string>> GetStackTraces() {
            var count = GetCount();
            var stackTraces = new List<Tuple<LogEntryMode, string>>();
            for (int i = 0; i < count; i++) {
                var (mode, item2) = GetStackText(i);
                if ((mode & (int) LogEntryMode.ScriptCompileError) != 0) {
                    stackTraces.Add(new Tuple<LogEntryMode, string>(LogEntryMode.ScriptCompileError, item2));
                } else if ((mode & (int) LogEntryMode.GraphCompileError) != 0) {
                    stackTraces.Add(new Tuple<LogEntryMode, string>(LogEntryMode.GraphCompileError, item2));
                } else if ((mode & (int) LogEntryMode.ScriptCompileWarning) != 0) {
                    stackTraces.Add(new Tuple<LogEntryMode, string>(LogEntryMode.ScriptCompileWarning, item2));
                }
            }

            return stackTraces;
        }
    }
}
#endif