using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using BIFramework.Singleton;
using XLua;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "GlobalSO", menuName = "SO/Global")]
[LuaCallCSharp]
public class GlobalSO : ScriptableObjectSingleton<GlobalSO> {
#if UNITY_EDITOR
    [MenuItem("SO/GlobalSO %.")]
    static void SelectionCommand() {
        var go = AssetDatabase.LoadAssetAtPath<GlobalSO>("Assets/_DynamicAssets/SO/GlobalSO.asset");
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
    }
#endif

    #region Default Setting

    public enum FrameRate {
        [LabelText("30fps")]
        NORMAL,
        [LabelText("60fps")]
        HEIGHT,
        [LabelText("无限制")]
        BEST
    }

    [SerializeField]
    [BoxGroup("Frame Rate")]
    [LabelText("帧率限制")]
    [EnumToggleButtons]
    public FrameRate frameRate = FrameRate.HEIGHT;

    #endregion

    #region Mode

    public enum PlayModeEnum {
        [LabelText("冒险")]
        ADVENTURE,
        [LabelText("建造")]
        BUILD,
        [LabelText("建造测试")]
        BUILD_RUN,
    }

    [HideInInspector]
    [BoxGroup("Mode")]
    public UnityEvent onPlayModeChange = new();

    [SerializeField]
    [BoxGroup("Mode")]
    [LabelText("游玩模式")]
    [EnumToggleButtons]
    private PlayModeEnum _playMode = PlayModeEnum.ADVENTURE;

    [EnumToggleButtons]
    public PlayModeEnum playMode {
        get => _playMode;
        set {
            _playMode = value;
            onPlayModeChange?.Invoke();
        }
    }

    #endregion

    #region Input

    public enum InputMode {
        AUTO,
        WINDOWS,
        MOBILE,
        GAMEPAD
    }

    [SerializeField]
    [BoxGroup("Input")]
    [LabelText("操作模式")]
    [EnumToggleButtons]
    private InputMode _inputMode = InputMode.AUTO;

    public InputMode inputMode {
        get => _inputMode;
        set { _inputMode = value; }
    }

    #endregion

    #region Logo

    [BoxGroup("Logo")]
    [LabelText("默认场景名")]
    public string defaultScene = "SandBoxScene";

    [BoxGroup("Logo")]
    [LabelText("是否需要显示logo动画")]
    public bool isShowLogo = true;

    #endregion

    #region Login

    [BoxGroup("Login")]
    [LabelText("是否进行在线游戏")]
    public bool isOnline = true;
    
    [BoxGroup("Login")]
    [EnableIf("isOnline")]
    [LabelText("是否登录SDK")]
    public bool isLoginSdk = false;

    [BoxGroup("Login")]
    [EnableIf("isOnline")]
    [LabelText("登陆时是否需要UI")]
    public bool isNeedUIOnLogin = true;

    [BoxGroup("Login")]
    [EnableIf("isOnline")]
    [LabelText("默认玩家名字")]
    public string defaultPlayerName = "EastBest";

    #endregion
}