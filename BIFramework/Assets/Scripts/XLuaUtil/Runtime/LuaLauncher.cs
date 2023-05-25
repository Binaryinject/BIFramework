using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using BIFramework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public class LuaLauncher : MonoBehaviour {
    public bool launchOnStart = true;

    [ListDrawerSettings(ShowFoldout = false, DefaultExpandedState = true)]
    public List<GameObject> luaGameObjects;

    private void Awake() {
        if (!LuaEnvironment.isReady) {
            foreach (var obj in luaGameObjects) {
                obj.SetActive(false);
            }
        }
    }

    private async UniTaskVoid Start() {
        if (launchOnStart && !LuaEnvironment.isReady) await Launch();
    }

    public async UniTask Launch() {
        SceneManager.sceneLoaded += (scene, _) => { SceneManager.SetActiveScene(scene); };
        Util.InitializeResolvers();
        await LuaEnvironment.Initialize();
        PipelineSetting();
        foreach (var obj in luaGameObjects) {
            obj.SetActive(true);
        }
    }

    public static void PipelineSetting() {
#if !UNITY_EDITOR
        //disable shapes gpu instancing mode if platform doesn't support it
        ShapesConfig.Instance.useImmediateModeInstancing = !Application.isMobilePlatform && SystemInfo.supportsInstancing;
#endif
        var renderAssets = (UniversalRenderPipelineAsset) GraphicsSettings.renderPipelineAsset;
        if (Application.isMobilePlatform) {
            Application.targetFrameRate = GlobalSO.Instance.frameRate switch {
                GlobalSO.FrameRate.NORMAL => 30,
                GlobalSO.FrameRate.HEIGHT => 60,
                GlobalSO.FrameRate.BEST => 60,
                _ => Application.targetFrameRate
            };
            renderAssets.renderScale = 0.667f;
        }
        else {
            Application.targetFrameRate = GlobalSO.Instance.frameRate switch {
                GlobalSO.FrameRate.NORMAL => 30,
                GlobalSO.FrameRate.HEIGHT => 60,
                GlobalSO.FrameRate.BEST => -1,
                _ => Application.targetFrameRate
            };
            renderAssets.renderScale = 1;
        }

        if (GlobalSO.Instance.inputMode == GlobalSO.InputMode.AUTO) {
            GlobalSO.Instance.inputMode = Application.isMobilePlatform ? GlobalSO.InputMode.MOBILE :
                Application.isConsolePlatform ? GlobalSO.InputMode.GAMEPAD : GlobalSO.InputMode.WINDOWS;
        }

        renderAssets.shadowDistance = 35;
    }
}