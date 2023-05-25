using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using XConsole;
using System.Text.RegularExpressions;

/// <summary>
/// The console logging frontend.
/// Pulls data from the XConsoleEditor backend
/// </summary>
public class XConsoleEditorWindow : EditorWindow, XConsoleEditor.ILoggerWindow {
    private static XConsoleEditorWindow _window = null;
    Vector2 LogListScrollPosition;
    Vector2 LogDetailsScrollPosition;

    Texture2D ErrorIcon;
    Texture2D WarningIcon;
    Texture2D MessageIcon;
    Texture2D SmallErrorIcon;
    Texture2D SmallWarningIcon;
    Texture2D SmallMessageIcon;

    Texture2D SmallErrorInactiveIcon;
    Texture2D SmallWarningInactiveIcon;
    Texture2D SmallMessageInactiveIcon;

    //bool ShowFilter = false;
    bool ShowChannels = false;
    bool ShowTimes = true;
    bool Collapse = false;
    bool ScrollFollowMessages = true;
    float CurrentTopPaneHeight = 200;
    bool Resize = false;
    private Rect CursorChangeRectUp;
    Rect CursorChangeRect;
    private Rect CursorChangeRectDown;
    int SelectedRenderLog = -1;
    bool Dirty = false;
    bool MakeDirty = false;
    float DividerHeight = 1;

    double LastMessageClickTime = 0;
    double LastFrameClickTime = 0;

    const double DoubleClickInterval = 0.5f;

    //Serialise the logger field so that Unity doesn't forget about the logger when you hit Play
    [UnityEngine.SerializeField]
    XConsoleEditor EditorLogger;

    List<XConsole.LogInfo> CurrentLogList = new();
    HashSet<string> CurrentChannels = new();

    //Standard unity pro colours
    Color SizerLineColour;

    GUIStyle EntryStyleStateInfo;
    GUIStyle EntryStyleBackEven;
    GUIStyle EntryStyleBackOdd;

    GUIStyle TextFieldRoundEdge;
    GUIStyle TextFieldRoundEdgeCancelButton;
    GUIStyle TextFieldRoundEdgeCancelButtonEmpty;

    string CurrentChannel = "All";
    string FilterRegex = "";
    bool ShowErrors = true;
    bool ShowWarnings = true;
    bool ShowMessages = true;
    int SelectedCallstackFrame = 0;
    bool ShowFrameSource = false;

    class CountedLog {
        public XConsole.LogInfo Log = null;
        public Int32 Count = 1;

        public CountedLog(XConsole.LogInfo log, Int32 count) {
            Log = log;
            Count = count;
        }
    }

    List<CountedLog> RenderLogs = new();
    float LogListMaxWidth = 0;
    float LogListLineHeight = 0;
    float CollapseBadgeMaxWidth = 0;

    [MenuItem("Window/XConsole %/")]
    public static void ShowLogWindow() {
        Init();
    }

    public static void Init() {
        _window = (XConsoleEditorWindow) GetWindow(typeof(XConsoleEditorWindow));
        _window.Focus();
        _window.Show();
    }

    public void OnLogChange(LogInfo logInfo) {
        Dirty = true;
        ScrollFollowMessages = true;
        // Repaint();
    }


    void OnInspectorUpdate() {
        // Debug.Log("Update");
        if (Dirty) {
            Repaint();
        }
    }

    void OnEnable() {
        // Connect to or create the backend
        if (!EditorLogger) {
            EditorLogger = XConsole.Logger.GetLogger<XConsoleEditor>();
            if (!EditorLogger) {
                EditorLogger = XConsoleEditor.Create();
            }
        }

        // XConsole doesn't allow for duplicate loggers, so this is safe
        // And, due to Unity serialisation stuff, necessary to do to it here.
        XConsole.Logger.AddLogger(EditorLogger);
        EditorLogger.AddWindow(this);

// _OR_NEWER only became available from 5.3
#if UNITY_5 || UNITY_5_3_OR_NEWER
        titleContent.text = "XConsole";
#else
        title = "XConsole";

#endif
        ClearSelectedMessage();

        titleContent.image = EditorGUIUtility.FindTexture("d_UnityEditor.ConsoleWindow");
        SmallErrorIcon = EditorGUIUtility.FindTexture("d_console.erroricon.sml");
        SmallWarningIcon = EditorGUIUtility.FindTexture("d_console.warnicon.sml");
        SmallMessageIcon = EditorGUIUtility.FindTexture("d_console.infoicon.sml");

        SmallErrorInactiveIcon = EditorGUIUtility.FindTexture("d_console.erroricon.inactive.sml");
        SmallWarningInactiveIcon = EditorGUIUtility.FindTexture("d_console.warnicon.inactive.sml");
        SmallMessageInactiveIcon = EditorGUIUtility.FindTexture("d_console.infoicon.inactive.sml");

        ErrorIcon = SmallErrorIcon;
        WarningIcon = SmallWarningIcon;
        MessageIcon = SmallMessageIcon;

        Dirty = true;
        Repaint();
    }

    private void OnDisable() {
        ClearSelectedMessage();
        EditorLogger.Clear(false);
    }

    /// <summary>
    /// Converts the entire message log to a multiline string
    /// </summary>
    public string ExtractLogListToString() {
        string result = "";
        foreach (CountedLog log in RenderLogs) {
            XConsole.LogInfo logInfo = log.Log;
            result += logInfo.GetRelativeTimeStampAsString() + ": " + logInfo.Severity + ": " + logInfo.Message + "\n";
        }

        return result;
    }

    /// <summary> 
    /// Converts the currently-displayed stack to a multiline string 
    /// </summary> 
    public string ExtractLogDetailsToString() {
        string result = "";
        if (RenderLogs.Count > 0 && SelectedRenderLog >= 0) {
            var countedLog = RenderLogs[SelectedRenderLog];
            var log = countedLog.Log;

            foreach (var frame in log.Callstack) {
                var methodName = frame.GetFormattedMethodName();
                result += methodName + "\n";
            }
        }

        return result;
    }

    /// <summary> 
    /// Handle "Copy" command; copies log & stacktrace contents to clipboard
    /// </summary> 
    public void HandleCopyToClipboard() {
        const string copyCommandName = "Copy";

        Event e = Event.current;
        if (e.type == EventType.ValidateCommand && e.commandName == copyCommandName) {
            // Respond to "Copy" command

            // Confirm that we will consume the command; this will result in the command being re-issued with type == EventType.ExecuteCommand
            e.Use();
        } else if (e.type == EventType.ExecuteCommand && e.commandName == copyCommandName) {
            // Copy current message log and current stack to the clipboard 

            // Convert all messages to a single long string 
            // It would be preferable to only copy one of the two, but that requires XConsole to have focus handling 
            // between the message log and stack views 
            string result = ExtractLogListToString();

            result += "\n";

            // Convert current callstack to a single long string 
            result += ExtractLogDetailsToString();

            GUIUtility.systemCopyBuffer = result;
        }
    }

    Vector2 DrawPos;

    public void OnGUI() {
        //Set up the basic style, based on the Unity defaults
        //A bit hacky, but means we don't have to ship an editor guistyle and can fit in to pro and free skins
        Color defaultLineColor = GUI.backgroundColor;

        EntryStyleBackEven = new GUIStyle("CN EntryBackEven");
        EntryStyleBackEven.padding = new RectOffset(4, 0, 0, 0);
        EntryStyleBackEven.imagePosition = ImagePosition.ImageLeft;
        EntryStyleBackEven.alignment = TextAnchor.MiddleLeft;

        EntryStyleBackOdd = new GUIStyle("CN EntryBackOdd");
        EntryStyleBackOdd.padding = new RectOffset(4, 0, 0, 0);
        EntryStyleBackOdd.imagePosition = ImagePosition.ImageLeft;
        EntryStyleBackOdd.alignment = TextAnchor.MiddleLeft;
        EntryStyleStateInfo = new GUIStyle("CN StatusInfo");

        TextFieldRoundEdge = new GUIStyle("SearchTextField");
        TextFieldRoundEdgeCancelButton = new GUIStyle("SearchCancelButton");
        TextFieldRoundEdgeCancelButtonEmpty = new GUIStyle("SearchCancelButtonEmpty");

        SizerLineColour = new Color(defaultLineColor.r * 0.5f, defaultLineColor.g * 0.5f, defaultLineColor.b * 0.5f);

        // GUILayout.BeginVertical(GUILayout.Height(topPanelHeaderHeight), GUILayout.MinHeight(topPanelHeaderHeight));
        ResizeTopPane();
        DrawPos = Vector2.zero;
        DrawToolbar();
        //if (ShowFilter) DrawFilter();

        if (ShowChannels) DrawChannels();

        float logPanelHeight = CurrentTopPaneHeight - DrawPos.y;

        if (Dirty) {
            CurrentLogList = EditorLogger.CopyLogInfo();
        }

        DrawLogList(logPanelHeight);

        DrawPos.y += DividerHeight + 4;

        DrawLogDetails();

        HandleCopyToClipboard();

        //If we're dirty, do a repaint
        Dirty = false;
        if (MakeDirty) {
            Dirty = true;
            MakeDirty = false;
            Repaint();
        }
    }

    //Some helper functions to draw buttons that are only as big as their text
    bool ButtonClamped(string text, GUIStyle style, out Vector2 size) {
        var content = new GUIContent(text);
        size = style.CalcSize(content);
        var rect = new Rect(DrawPos, size);
        return GUI.Button(rect, text, style);
    }

    bool ToggleClamped(bool state, string text, GUIStyle style, out Vector2 size) {
        var content = new GUIContent(text);
        return ToggleClamped(state, content, style, out size);
    }

    bool ToggleClamped(bool state, GUIContent content, GUIStyle style, out Vector2 size) {
        size = style.CalcSize(content);
        Rect drawRect = new Rect(DrawPos, size);
        return GUI.Toggle(drawRect, state, content, style);
    }

    string TextClamped(string state, Vector2 newSize, GUIStyle style, out Vector2 size) {
        size = newSize;
        Rect drawRect = new Rect(DrawPos + new Vector2(0, 2), size);
        return GUI.TextField(drawRect, state, -1, style);
    }

    void LabelClamped(string text, GUIStyle style, out Vector2 size) {
        var content = new GUIContent(text);
        size = style.CalcSize(content);

        Rect drawRect = new Rect(DrawPos, size);
        GUI.Label(drawRect, text, style);
    }

    /// <summary>
    /// Draws the thin, Unity-style toolbar showing error counts and toggle buttons
    /// </summary>
    void DrawToolbar() {
        var toolbarStyle = EditorStyles.toolbarButton;

        Vector2 elementSize;
        if (ButtonClamped("Clear", EditorStyles.toolbarButton, out elementSize)) {
            EditorLogger.Clear();
        }

        DrawPos.x += elementSize.x;
        EditorLogger.ClearOnPlay = ToggleClamped(EditorLogger.ClearOnPlay, "Clear On Play", EditorStyles.toolbarButton,
            out elementSize);
        DrawPos.x += elementSize.x;
        EditorLogger.PauseOnError = ToggleClamped(EditorLogger.PauseOnError, "Error Pause", EditorStyles.toolbarButton,
            out elementSize);
        DrawPos.x += elementSize.x;
        var showTimes = ToggleClamped(ShowTimes, "Times", EditorStyles.toolbarButton, out elementSize);
        if (showTimes != ShowTimes) {
            MakeDirty = true;
            ShowTimes = showTimes;
        }

        DrawPos.x += elementSize.x;
        var showChannels = ToggleClamped(ShowChannels, "Channels", EditorStyles.toolbarButton, out elementSize);
        if (showChannels != ShowChannels) {
            MakeDirty = true;
            ShowChannels = showChannels;
        }

        DrawPos.x += elementSize.x;
        var collapse = ToggleClamped(Collapse, "Collapse", EditorStyles.toolbarButton, out elementSize);
        if (collapse != Collapse) {
            MakeDirty = true;
            Collapse = collapse;
            SelectedRenderLog = -1;
        }

        DrawPos.x += elementSize.x;

        // ScrollFollowMessages =
        //     ToggleClamped(ScrollFollowMessages, "Follow", EditorStyles.toolbarButton, out elementSize);
        // DrawPos.x += elementSize.x;


        var filterSpace = 3;
        var filterTextSize = new Vector2(300, 30);
        var messageToggleContent = new GUIContent(EditorLogger.NoMessages.ToString(),
            EditorLogger.NoMessages > 0 ? SmallMessageIcon : SmallMessageInactiveIcon);
        var warningToggleContent = new GUIContent(EditorLogger.NoWarnings.ToString(),
            EditorLogger.NoWarnings > 0 ? SmallWarningIcon : SmallWarningInactiveIcon);
        var errorToggleContent = new GUIContent(EditorLogger.NoErrors.ToString(),
            EditorLogger.NoErrors > 0 ? SmallErrorIcon : SmallErrorInactiveIcon);

        float totalErrorButtonWidth = toolbarStyle.CalcSize(errorToggleContent).x +
                                      toolbarStyle.CalcSize(warningToggleContent).x +
                                      toolbarStyle.CalcSize(messageToggleContent).x + filterTextSize.x + filterSpace;

        var errorIconX = position.width - totalErrorButtonWidth;
        if (errorIconX > DrawPos.x) {
            DrawPos.x = errorIconX;
        }

        var guiStyle = FilterRegex != "" ? TextFieldRoundEdgeCancelButton : TextFieldRoundEdgeCancelButtonEmpty;

        if (GUI.Button(
            new Rect(DrawPos.x + filterTextSize.x - 15, DrawPos.y + 2, guiStyle.fixedWidth, guiStyle.fixedHeight),
            GUIContent.none, guiStyle) && FilterRegex != "") {
            FilterRegex = "";
            GUI.changed = true;
            GUIUtility.keyboardControl = 0;
            ClearSelectedMessage();
            MakeDirty = true;
        }

        var filterText = TextClamped(FilterRegex, filterTextSize, TextFieldRoundEdge, out elementSize);

        if (GUI.Button(
            new Rect(DrawPos.x + filterTextSize.x - 15, DrawPos.y + 2, guiStyle.fixedWidth, guiStyle.fixedHeight),
            GUIContent.none, guiStyle) && FilterRegex != "") {
        }


        DrawPos.x += elementSize.x + filterSpace;

        var showMessages = ToggleClamped(ShowMessages, messageToggleContent, toolbarStyle, out elementSize);
        DrawPos.x += elementSize.x;
        var showWarnings = ToggleClamped(ShowWarnings, warningToggleContent, toolbarStyle, out elementSize);
        DrawPos.x += elementSize.x;
        var showErrors = ToggleClamped(ShowErrors, errorToggleContent, toolbarStyle, out elementSize);
        DrawPos.x += elementSize.x;

        DrawPos.y += elementSize.y;
        DrawPos.x = 0;

        //If the errors/warning to show has changed, clear the selected message
        if (showErrors != ShowErrors || showWarnings != ShowWarnings || showMessages != ShowMessages ||
            FilterRegex != filterText) {
            ClearSelectedMessage();
            MakeDirty = true;
        }

        ShowWarnings = showWarnings;
        ShowMessages = showMessages;
        ShowErrors = showErrors;
        FilterRegex = filterText;
    }

    /// <summary>
    /// Draws the channel selector
    /// </summary>
    void DrawChannels() {
        var channels = GetChannels();
        int currentChannelIndex = 0;
        for (int c1 = 0; c1 < channels.Count; c1++) {
            if (channels[c1] == CurrentChannel) {
                currentChannelIndex = c1;
                break;
            }
        }

        var content = new GUIContent("S");
        var size = GUI.skin.button.CalcSize(content);
        var drawRect = new Rect(DrawPos, new Vector2(position.width, size.y));
        currentChannelIndex = GUI.SelectionGrid(drawRect, currentChannelIndex, channels.ToArray(), channels.Count);
        if (CurrentChannel != channels[currentChannelIndex]) {
            CurrentChannel = channels[currentChannelIndex];
            ClearSelectedMessage();
            MakeDirty = true;
        }

        DrawPos.y += size.y;
    }

    /// <summary>
    /// Based on filter and channel selections, should this log be shown?
    /// </summary>
    bool ShouldShowLog(System.Text.RegularExpressions.Regex regex, LogInfo log) {
        if (log.Channel == CurrentChannel || CurrentChannel == "All" ||
            (CurrentChannel == "No Channel" && String.IsNullOrEmpty(log.Channel))) {
            if (log.Severity == LogSeverity.Message && ShowMessages ||
                log.Severity == LogSeverity.Warning && ShowWarnings ||
                log.Severity == LogSeverity.Error && ShowErrors) {
                if (regex == null || regex.IsMatch(log.Message)) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Converts a given log element into a piece of gui content to be displayed
    /// </summary>
    GUIContent GetLogLineGUIContent(XConsole.LogInfo log, bool showTimes, bool showChannels, bool oneLine = true) {
        var showMessage = log.Message;
        if (oneLine) showMessage = showMessage.Split(Environment.NewLine.ToCharArray())[0];

        //Make all messages single line
        //showMessage = showMessage.Replace(XConsole.Logger.UnityInternalNewLine, " ");

        // Format the message as follows:
        //     [channel] 0.000 : message  <-- Both channel and time shown
        //     0.000 : message            <-- Time shown, channel hidden
        //     [channel] : message        <-- Channel shown, time hidden
        //     message                    <-- Both channel and time hidden
        var showChannel = showChannels && !string.IsNullOrEmpty(log.Channel);
        var channelMessage = showChannel ? $"[{log.Channel}]" : "";
        var channelTimeSeparator = (showChannel && showTimes) ? " " : "";
        var timeMessage = showTimes ? $"{log.GetAbsoluteTimeStampAsString()} " : "";
        var prefixMessageSeparator = showChannel ? " : " : "";
        showMessage =
            $"{channelMessage}{channelTimeSeparator}{timeMessage}{prefixMessageSeparator}{showMessage}{(oneLine ? "" : (showMessage.Last().ToString() != "\n" ? "\n" : ""))}";

        var content = new GUIContent(showMessage, GetIconForLog(log)); 
        return content;
    }

    /// <summary>
    /// Draws the main log panel
    /// </summary>
    public void DrawLogList(float height) {
        var oldColor = GUI.backgroundColor;


        float buttonY = 0;

        System.Text.RegularExpressions.Regex filterRegex = null;

        if (!String.IsNullOrEmpty(FilterRegex)) {
            filterRegex = new Regex(FilterRegex);
        }

        var collapseBadgeStyle = EditorStyles.miniButton;
        var logLineStyle = EntryStyleBackEven;

        // If we've been marked dirty, we need to recalculate the elements to be displayed
        if (Dirty) {
            LogListMaxWidth = 0;
            LogListLineHeight = 21;
            CollapseBadgeMaxWidth = 0;
            RenderLogs.Clear();

            //When collapsed, count up the unique elements and use those to display
            if (Collapse) {
                var collapsedLines = new Dictionary<string, CountedLog>();
                var collapsedLinesList = new List<CountedLog>();

                foreach (var log in CurrentLogList) {
                    if (ShouldShowLog(filterRegex, log)) {
                        var matchString = log.Message + "!$" + log.Severity + "!$" + log.Channel;

                        CountedLog countedLog;
                        if (collapsedLines.TryGetValue(matchString, out countedLog)) {
                            countedLog.Count++;
                        } else {
                            countedLog = new CountedLog(log, 1);
                            collapsedLines.Add(matchString, countedLog);
                            collapsedLinesList.Add(countedLog);
                        }
                    }
                }

                foreach (var countedLog in collapsedLinesList) {
                    var content = GetLogLineGUIContent(countedLog.Log, ShowTimes, ShowChannels);
                    RenderLogs.Add(countedLog);
                    var logLineSize = logLineStyle.CalcSize(content);
                    LogListMaxWidth = Mathf.Max(LogListMaxWidth, logLineSize.x);
                    LogListLineHeight = Mathf.Max(LogListLineHeight, logLineSize.y);

                    var collapseBadgeContent = new GUIContent(countedLog.Count.ToString());
                    var collapseBadgeSize = collapseBadgeStyle.CalcSize(collapseBadgeContent);
                    CollapseBadgeMaxWidth = Mathf.Max(CollapseBadgeMaxWidth, collapseBadgeSize.x);
                }
            }
            //If we're not collapsed, display everything in order
            else {
                foreach (var log in CurrentLogList) {
                    if (ShouldShowLog(filterRegex, log)) {
                        var content = GetLogLineGUIContent(log, ShowTimes, ShowChannels);
                        RenderLogs.Add(new CountedLog(log, 1));
                        var logLineSize = logLineStyle.CalcSize(content);
                        LogListMaxWidth = Mathf.Max(LogListMaxWidth, logLineSize.x);
                        LogListLineHeight = Mathf.Max(LogListLineHeight, logLineSize.y);
                    }
                }
            }

            LogListMaxWidth += CollapseBadgeMaxWidth;
        }

        var scrollRect = new Rect(DrawPos, new Vector2(position.width, height));
        float lineWidth = Mathf.Max(LogListMaxWidth, scrollRect.width);

        CursorChangeRect = new Rect(0, DrawPos.y, position.width, DividerHeight);
        oldColor = GUI.color;
        GUI.color = SizerLineColour;
        GUI.DrawTexture(CursorChangeRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0,
            new Color(0f, 0f, 0f, 0.5f), 0, 0);
        GUI.color = oldColor;

        var contentRect = new Rect(0, 0, lineWidth, RenderLogs.Count * LogListLineHeight + 1);
        Vector2 lastScrollPosition = LogListScrollPosition;

        LogListScrollPosition = GUI.BeginScrollView(scrollRect, LogListScrollPosition, contentRect, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        //If we're following the messages but the user has moved, cancel following
        if (ScrollFollowMessages) {
            if (lastScrollPosition.y - LogListScrollPosition.y > LogListLineHeight) {
                //XDebug.UnityLog($"{lastScrollPosition.y} {LogListScrollPosition.y}");
                ScrollFollowMessages = false;
            }
        }

        float logLineX = CollapseBadgeMaxWidth;

        //Render all the elements
        int firstRenderLogIndex = (int) (LogListScrollPosition.y / LogListLineHeight);
        int lastRenderLogIndex = firstRenderLogIndex + (int) (height / LogListLineHeight);

        firstRenderLogIndex = Mathf.Clamp(firstRenderLogIndex, 0, RenderLogs.Count);
        lastRenderLogIndex = Mathf.Clamp(lastRenderLogIndex, 0, RenderLogs.Count);
        lastRenderLogIndex = lastRenderLogIndex > 0 && lastRenderLogIndex < RenderLogs.Count - 1 ? lastRenderLogIndex + 1 : RenderLogs.Count;
        buttonY = firstRenderLogIndex * LogListLineHeight;

        for (int renderLogIndex = firstRenderLogIndex; renderLogIndex < lastRenderLogIndex; renderLogIndex++) {
            var countedLog = RenderLogs[renderLogIndex];
            var log = countedLog.Log;
            logLineStyle = (renderLogIndex % 2 == 0) ? EntryStyleBackOdd : EntryStyleBackEven;

            logLineStyle.normal.background = renderLogIndex == SelectedRenderLog ? EditorGUIUtility.whiteTexture : null;
            GUI.backgroundColor = renderLogIndex == SelectedRenderLog
                ? new Color(44f / 255f, 93f / 255f, 135f / 255f)
                : Color.white;

            //Make all messages single line
            var content = GetLogLineGUIContent(log, ShowTimes, ShowChannels);
            var drawRect = new Rect(logLineX, buttonY, contentRect.width, LogListLineHeight);
            if (GUI.Button(drawRect, content, logLineStyle)) {
                //Select a message, or jump to source if it's double-clicked
                if (renderLogIndex == SelectedRenderLog) {
                    if (EditorApplication.timeSinceStartup - LastMessageClickTime < DoubleClickInterval) {
                        LastMessageClickTime = 0;
                        // Attempt to display source code associated with messages. Search through all stackframes,
                        //   until we find a stackframe that can be displayed in source code view
                        foreach (var t in log.Callstack) {
                            if (JumpToSource(t))
                                break;
                        }
                    } else {
                        LastMessageClickTime = EditorApplication.timeSinceStartup;
                    }
                } else {
                    GUIUtility.keyboardControl = 0;
                    GUIUtility.hotControl = 0;
                    SelectedRenderLog = renderLogIndex;
                    SelectedCallstackFrame = -1;
                    LastMessageClickTime = EditorApplication.timeSinceStartup;
                }


                //Always select the game object that is the source of this message
                var go = log.Source as GameObject;
                if (go != null) {
                    Selection.activeGameObject = go;
                }
            }

            if (Collapse) {
                var collapseBadgeContent = new GUIContent(countedLog.Count.ToString());
                var collapseBadgeSize = collapseBadgeStyle.CalcSize(collapseBadgeContent);
                var collapseBadgeRect = new Rect(0, buttonY, collapseBadgeSize.x, collapseBadgeSize.y);
                GUI.Button(collapseBadgeRect, collapseBadgeContent, collapseBadgeStyle);
            }

            buttonY += LogListLineHeight;
        }

        //If we're following the log, move to the end
        if (ScrollFollowMessages && RenderLogs.Count > 0) {
            LogListScrollPosition.y = ((RenderLogs.Count + 1) * LogListLineHeight) - scrollRect.height;
        }

        GUI.EndScrollView();
        DrawPos.y += height;
        DrawPos.x = 0;
        GUI.backgroundColor = oldColor;
    }

    /// <summary>
    /// The bottom of the panel - details of the selected log
    /// </summary>
    public void DrawLogDetails() {
        var oldColor = GUI.backgroundColor;

        SelectedRenderLog = Mathf.Clamp(SelectedRenderLog, 0, CurrentLogList.Count);

        if (RenderLogs.Count > 0 && SelectedRenderLog >= 0) {
            var countedLog = RenderLogs[SelectedRenderLog];
            var log = countedLog.Log;
            var logLineStyle = EntryStyleBackOdd;

            var sourceStyle = new GUIStyle(GUI.skin.textArea);
            sourceStyle.richText = true;

            var drawRect = new Rect(DrawPos, new Vector2(position.width - DrawPos.x, position.height - DrawPos.y));

            //Work out the content we need to show, and the sizes
            var detailLines = new List<GUIContent>();
            float contentHeight = 0;
            float contentWidth = 0;
            float lineHeight = 15;


            for (int c1 = 0; c1 < log.Callstack.Count; c1++) {
                var frame = log.Callstack[c1];
                var methodName = frame.GetFormattedMethodNameWithFileName();
                if (!string.IsNullOrEmpty(methodName)) {
                    var content = new GUIContent(methodName);
                    detailLines.Add(content);

                    var contentSize = logLineStyle.CalcSize(content);
                    contentHeight += contentSize.y;
                    lineHeight = Mathf.Max(lineHeight, contentSize.y);
                    contentWidth = Mathf.Max(contentSize.x, contentWidth);
                    if (ShowFrameSource && c1 == SelectedCallstackFrame) {
                        var sourceContent = GetFrameSourceGUIContent(frame);
                        if (sourceContent != null) {
                            var sourceSize = sourceStyle.CalcSize(sourceContent);
                            contentHeight += sourceSize.y;
                            contentWidth = Mathf.Max(sourceSize.x, contentWidth);
                        }
                    }
                }
            }

            var msgContent = GetLogLineGUIContent(log, false, false, false);
            var msgLineSize = logLineStyle.CalcSize(msgContent);
            var msgLineHeight = Mathf.Max(15, msgLineSize.y);

            if (log.Callstack.Count == 1) msgLineHeight = 0;
            
            //Render the content
            var contentRect = new Rect(-2, 0, Mathf.Max(contentWidth, drawRect.width), contentHeight + msgLineHeight);
            
            LogDetailsScrollPosition =
                GUI.BeginScrollView(drawRect, LogDetailsScrollPosition, contentRect, false, false);
            
            logLineStyle.normal.background = null;
            //Render origin message
            EditorGUI.SelectableLabel(new Rect(-2, 0, Mathf.Max(contentWidth, drawRect.width), msgLineHeight),
                msgContent.text, logLineStyle);
            
            float lineY = msgLineHeight;
            for (int c1 = 0; c1 < detailLines.Count; c1++) {
                var lineContent = detailLines[c1];
                if (lineContent != null) {
                    logLineStyle = EntryStyleStateInfo;
                    logLineStyle.normal.background =
                        c1 == SelectedCallstackFrame ? EditorGUIUtility.whiteTexture : null;
                    GUI.backgroundColor = c1 == SelectedCallstackFrame
                        ? new Color(44f / 255f, 93f / 255f, 135f / 255f)
                        : Color.white;

                    var frame = log.Callstack[c1];
                    var lineRect = new Rect(0, lineY, contentRect.width, lineHeight);

                    // Handle clicks on the stack frame
                    if (GUI.Button(lineRect, lineContent, logLineStyle)) {
                        if (c1 == SelectedCallstackFrame) {
                            if (Event.current.button == 1) {
                                ToggleShowSource(frame);
                                Repaint();
                            } else {
                                if (EditorApplication.timeSinceStartup - LastFrameClickTime < DoubleClickInterval) {
                                    LastFrameClickTime = 0;
                                    JumpToSource(frame);
                                } else {
                                    LastFrameClickTime = EditorApplication.timeSinceStartup;
                                }
                            }
                        } else {
                            SelectedCallstackFrame = c1;
                            LastFrameClickTime = EditorApplication.timeSinceStartup;
                        }
                    }

                    lineY += lineHeight;
                    // //Show the source code if needed
                    // if (ShowFrameSource && c1 == SelectedCallstackFrame)
                    // {
                    //     GUI.backgroundColor = Color.white;
                    //
                    //     var sourceContent = GetFrameSourceGUIContent(frame);
                    //     if (sourceContent != null)
                    //     {
                    //         var sourceSize = sourceStyle.CalcSize(sourceContent);
                    //         var sourceRect = new Rect(0, lineY, contentRect.width, sourceSize.y);
                    //
                    //         GUI.Label(sourceRect, sourceContent, sourceStyle);
                    //         lineY += sourceSize.y;
                    //     }
                    // }
                }
            }

            GUI.EndScrollView();
        }

        GUI.backgroundColor = oldColor;
    }

    Texture2D GetIconForLog(LogInfo log) {
        return log.Severity switch {
            LogSeverity.Error => ErrorIcon,
            LogSeverity.Warning => WarningIcon,
            _ => MessageIcon
        };
    }

    void ToggleShowSource(LogStackFrame frame) {
        ShowFrameSource = !ShowFrameSource;
    }

    static bool JumpToSource(LogStackFrame frame) {
        if (frame.FileName != null) {
            var osFileName = XConsole.Logger.ConvertDirectorySeparatorsFromUnityToOS(frame.FileName);
            var filename = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), osFileName);
            if (System.IO.File.Exists(filename)) {
                if (UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(filename, frame.LineNumber))
                    return true;
            }
        }

        return false;
    }

    GUIContent GetFrameSourceGUIContent(LogStackFrame frame) {
        var source = GetSourceForFrame(frame);
        if (!string.IsNullOrEmpty(source)) {
            var content = new GUIContent(source);
            return content;
        }

        return null;
    }


    void DrawFilter() {
        Vector2 size;
        LabelClamped("Filter Regex", GUI.skin.label, out size);
        DrawPos.x += size.x;

        string filterRegex = null;
        bool clearFilter = false;
        if (ButtonClamped("Clear", GUI.skin.button, out size)) {
            clearFilter = true;

            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
        }

        DrawPos.x += size.x;

        var drawRect = new Rect(DrawPos, new Vector2(position.width - DrawPos.x, size.y));
        filterRegex = EditorGUI.TextArea(drawRect, FilterRegex);

        if (clearFilter) {
            filterRegex = null;
        }

        //If the filter has changed, invalidate our currently selected message
        if (filterRegex != FilterRegex) {
            ClearSelectedMessage();
            FilterRegex = filterRegex;
            MakeDirty = true;
        }

        DrawPos.y += size.y;
        DrawPos.x = 0;
    }

    List<string> GetChannels() {
        if (Dirty) {
            CurrentChannels = EditorLogger.CopyChannels();
        }

        var categories = CurrentChannels;

        var channelList = new List<string>();
        channelList.Add("All");
        channelList.Add("No Channel");
        channelList.AddRange(categories);
        return channelList;
    }

    /// <summary>
    ///   Handles the split window stuff, somewhat bodgily
    /// </summary>
    private void ResizeTopPane() {
        //Set up the resize collision rect
        CursorChangeRect = new Rect(0, CurrentTopPaneHeight, position.width, DividerHeight + 4);

        var drawUp = new Rect(0, CurrentTopPaneHeight - 1, position.width, 2);
        var drawMiddle = new Rect(0, CurrentTopPaneHeight, position.width, DividerHeight);
        var drawDown = new Rect(0, CurrentTopPaneHeight + 1, position.width, 2);
        var oldColor = GUI.color;
        GUI.color = SizerLineColour;
        GUI.DrawTexture(drawUp, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0,
            new Color(0f, 0f, 0f, 0f), 0, 0);
        GUI.DrawTexture(drawMiddle, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0,
            new Color(0f, 0f, 0f, 0.5f), 0, 0);
        GUI.DrawTexture(drawDown, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0,
            new Color(0f, 0f, 0f, 0f), 0, 0);
        GUI.color = oldColor;
        EditorGUIUtility.AddCursorRect(CursorChangeRect, MouseCursor.ResizeVertical);

        if (Event.current.type == EventType.MouseDown && CursorChangeRect.Contains(Event.current.mousePosition)) {
            Resize = true;
        }

        //If we've resized, store the new size and force a repaint
        if (Resize) {
            CurrentTopPaneHeight = Event.current.mousePosition.y;
            CursorChangeRect.Set(CursorChangeRect.x, CurrentTopPaneHeight, CursorChangeRect.width,
                CursorChangeRect.height);
            Repaint();
        }

        if (Event.current.type == EventType.MouseUp)
            Resize = false;

        CurrentTopPaneHeight = Mathf.Clamp(CurrentTopPaneHeight, 100, position.height - 100);
    }

    //Cache for GetSourceForFrame
    string SourceLines;
    LogStackFrame SourceLinesFrame;

    /// <summary>
    ///If the frame has a valid filename, get the source string for the code around the frame
    ///This is cached, so we don't keep getting it.
    /// </summary>
    string GetSourceForFrame(LogStackFrame frame) {
        if (SourceLinesFrame == frame) {
            return SourceLines;
        }


        if (frame.FileName == null) {
            return "";
        }

        var osFileName = XConsole.Logger.ConvertDirectorySeparatorsFromUnityToOS(frame.FileName);
        var filename = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), osFileName);
        if (!System.IO.File.Exists(filename)) {
            return "";
        }

        int lineNumber = frame.LineNumber - 1;
        int linesAround = 3;
        var lines = System.IO.File.ReadAllLines(filename);
        var firstLine = Mathf.Max(lineNumber - linesAround, 0);
        var lastLine = Mathf.Min(lineNumber + linesAround + 1, lines.Count());

        SourceLines = "";
        if (firstLine != 0) {
            SourceLines += "...\n";
        }

        for (int c1 = firstLine; c1 < lastLine; c1++) {
            string str = lines[c1] + "\n";
            if (c1 == lineNumber) {
                str = "<color=#ff0000ff>" + str + "</color>";
            }

            SourceLines += str;
        }

        if (lastLine != lines.Count()) {
            SourceLines += "...\n";
        }

        SourceLinesFrame = frame;
        return SourceLines;
    }

    void ClearSelectedMessage() {
        SelectedRenderLog = -1;
        SelectedCallstackFrame = -1;
        ShowFrameSource = false;
    }
}