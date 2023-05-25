using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.AccessControl;
using Animancer;
using Cinemachine;
using Com.TheFallenGames.OSA.Core;
using Com.TheFallenGames.OSA.CustomAdapters.GridView;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using DG.Tweening;
using DG.Tweening.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using XLua;
using UnityEngine.Rendering.Universal;
using UnityEngine.U2D;
using BIFramework.Asynchronous;
using BIFramework.Singleton;
using BIFramework.Pools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pathfinding;
using Pathfinding.RVO;
using TMPro;
using Unity.Mathematics;
using UnityEngine.AI;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Tween = DG.Tweening.Tween;

//using XUtils;

/// <summary>
/// xlua自定义导出
/// </summary>
#pragma warning disable 0618
public static class XLuaCustomExport {
    [LuaCallCSharp]
    public static List<Type> LuaCallCSharpUI {
        get {
            List<Type> list = new List<Type>();
            //            list.AddRange(AddNameSpaceClass<Lui.LButton>("Lui"));
            return list;
        }
    }


    public static List<Type> AddNameSpaceClass<T>(string namespaceName) {
        List<Type> typeList = new List<Type>();

        Assembly assembly = Assembly.GetAssembly(typeof(T));

        foreach (Type mType in assembly.GetExportedTypes()) {
            if (mType != null && !string.IsNullOrEmpty(mType.Namespace) && mType.Namespace.Split('.')[0] == namespaceName) {
                typeList.Add(mType);
            }
        }

        return typeList;
    }

    [CSharpCallLua]
    public static List<Type> CSharpCallLuaList = new() {
        typeof(Action<byte[]>),
        typeof(Action<Vector3Int>),
        typeof(Action<Vector3Int, LuaTable>),
        typeof(Action<GameObject, GameObject>),
        typeof(Action<Transform, int>),
        typeof(Action<Transform, object>),
        typeof(Action<string, Vector3>),
        typeof(Action<Collider>),
        typeof(Action<Collider[]>),
        typeof(Action<Transform[]>),
        typeof(UnityAction<int, GameObject>),
        typeof(UnityAction<int, Transform>),
        typeof(Action),
        typeof(Func<double, double, double>),
        typeof(Func<ILuaTask>),
        typeof(Func<string, ILuaTask>),
        typeof(Func<MonoBehaviour, ILuaTask>),
        typeof(Func<MonoBehaviour, Collision>),
        typeof(Func<MonoBehaviour, Collider>),
        typeof(Func<MonoBehaviour, string>),
        typeof(System.Func<bool>),
        typeof(Action<string>),
        typeof(Action<string, int>),
        typeof(Action<string, float>),
        typeof(Action<int, float>),
        typeof(System.Action<bool>),
        typeof(System.Action<Boolean>),
        typeof(Action<int>),
        typeof(Action<float>),
        typeof(Action<double>),
        typeof(Action<bool, GameObject>),
        typeof(Action<Vector3, Vector3, float>),
        typeof(IList<IResourceLocation>),
        typeof(Action<AsyncOperationHandle<GameObject>>),
        typeof(Action<AsyncOperationHandle<TextAsset>>),
        typeof(Action<AsyncOperationHandle<Texture>>),
        typeof(Action<AsyncOperationHandle<Sprite>>),
        typeof(Action<AsyncOperationHandle<Material>>),
        typeof(Action<AsyncOperationHandle<AudioClip>>),
        typeof(Action<AsyncOperationHandle<SpriteAtlas>>),
        typeof(Action<AsyncOperationHandle<SceneInstance>>),
        typeof(UnityEvent),
        typeof(UnityEvent<bool>),
        typeof(UnityEvent<int>),
        typeof(UnityEvent<float>),
        typeof(UnityEvent<string>),
        typeof(UnityAction<float>),
        typeof(UnityAction<int>),
        typeof(UnityAction),
        typeof(UnityAction<bool>),
        typeof(Toggle.ToggleEvent),
        typeof(Dropdown.DropdownEvent),
        typeof(System.Action<GameObject>),
        typeof(UnityAction<Vector2>),
        typeof(Action<PointerEventData>),
//        typeof(UnityEvent<Vector2>),
        //typeof(TweenCallback),
        typeof(UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>),
        typeof(UnityEngine.Events.UnityAction<string>),
        typeof(Action<Sprite>),
        typeof(System.Collections.IEnumerator),
        typeof(System.Action<Transform, int>),
        typeof(UnityAction<string, string>),
        typeof(DOGetter<Vector2>),
        typeof(DOGetter<Vector3>),
        typeof(DOGetter<Vector4>),
        typeof(DOGetter<Quaternion>),
        typeof(DOGetter<int>),
        typeof(DOGetter<float>),
        typeof(DOGetter<double>),
        typeof(DOGetter<UnityEngine.Color>),
        typeof(DOSetter<Vector2>),
        typeof(DOSetter<Vector3>),
        typeof(DOSetter<Vector4>),
        typeof(DOSetter<Quaternion>),
        typeof(DOSetter<int>),
        typeof(DOSetter<float>),
        typeof(DOSetter<double>),
        typeof(DOSetter<UnityEngine.Color>),
        typeof(CinemachineCore),
        typeof(CinemachineCore.AxisInputDelegate),
        typeof(Action<Texture2D>),
        typeof(Action<LuaItemViewsHolder>),
        typeof(Action<LuaItemViewsHolder, int>),
        typeof(Action<LuaCellViewHolder>),
        typeof(LuaListAdapter.CreateViewsHolderDelegate),
        typeof(LuaListAdapter.CollectItemsSizesDelegate),
    };

    [ReflectionUse]
    public static List<Type> CSharpCallLuaByReflec = new() {
        //typeof(System.Action<GameObject>), typeof(System.Action<string, string, PointerEventData>), typeof(System.Action<bool>),
    };

    [ReflectionUse]
    public static List<Type> LuaCallCSharpByRelfect = new() { };

    [LuaCallCSharp]
    public static List<Type> LuaCallCSharpList = new() {
        typeof(System.Action<GameObject>),
        typeof(UnityEngine.Physics),
#if UNITY_2018_1_OR_NEWER
        typeof(UnityEngine.Profiling.Profiler),
#else
        typeof(UnityEngine.Profiler),
#endif
        typeof(System.Action<bool>),
    };

    [LuaCallCSharp]
    public static List<Type> LuaCallCSharpListUnity = new() {
        /*************** System ***************/
        typeof(System.IO.File),
        typeof(FileSecurity),
        typeof(System.Reflection.BindingFlags),
        typeof(System.Object),
        typeof(System.DateTime),
        typeof(System.TimeSpan),
        typeof(System.IO.Directory),
        typeof(System.GC),
        typeof(System.IO.Path),
        typeof(System.Action),
        typeof(System.ValueType),
        /*************** Unity Resources ***************/
        typeof(Addressables),
        typeof(AsyncOperation),
        typeof(List<AsyncOperationHandle>),
        typeof(List<string>),
        typeof(List<Camera>),
        typeof(Dictionary<string, string>),
        typeof(Dictionary<Vector3Int, System.Int32>),
        typeof(Dictionary<Vector3Int, Vector3>),
        typeof(SceneInstance),
        typeof(AsyncOperationHandle<GameObject>),
        typeof(AsyncOperationHandle<TextAsset>),
        typeof(AsyncOperationHandle<Texture>),
        typeof(AsyncOperationHandle<Sprite>),
        typeof(AsyncOperationHandle<Material>),
        typeof(AsyncOperationHandle<AudioClip>),
        typeof(AsyncOperationHandle<SpriteAtlas>),
        typeof(AsyncOperationHandle<SceneInstance>),
        /*************** unity结合lua，这部分导出很多功能在lua侧重新实现，没有实现的功能才会跑到cs侧 ***************/
        typeof(Bounds),
        typeof(Color),
        typeof(LayerMask),
        typeof(Mathf),
        typeof(Plane),
        typeof(Quaternion),
        typeof(Ray),
        typeof(RaycastHit),
        typeof(Time),
        typeof(Touch),
        typeof(TouchPhase),
        typeof(Vector2),
        typeof(Vector3),
        typeof(Vector4),
        typeof(Vector2Int),
        typeof(Vector3Int),
        typeof(math),
        /*************** Unity Structs&Enums ***************/
        typeof(Vector2),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(Color),
        typeof(LayerMask),
        typeof(Rect),
        typeof(KeyCode),
        typeof(Debug),
        typeof(RuntimePlatform),
        typeof(FogMode),
        typeof(LightmapsMode),
        typeof(EventSystem),
        typeof(RectTransformUtility),
        typeof(Graphic),
        typeof(Component),
        typeof(AnimatorStateInfo),

        /***************Unity Commom***************/
        typeof(UnityEngine.Object),
        typeof(UnityEvent),
        typeof(UnityEvent<bool>),
        typeof(UnityEvent<int>),
        typeof(UnityEvent<float>),
        typeof(UnityEvent<string>),
        typeof(Application),
        typeof(GameObject),
        typeof(Transform),
        typeof(RectTransform),
        typeof(Time),
        typeof(WWW),
        typeof(Rigidbody),
        typeof(CharacterController),
        typeof(PlayerPrefs),
        typeof(ImageConversion),
        typeof(ScriptableObject),
        typeof(Application),
        typeof(UnityWebRequest),
        typeof(UnityWebRequest.Result),
        typeof(Camera.GateFitMode),
        typeof(Camera.FieldOfViewAxis),
        typeof(Camera.GateFitParameters),
        typeof(Camera.StereoscopicEye),
        typeof(ScriptableObjectSingleton<GlobalSO>),
        typeof(PoolHelper),
        typeof(UnityEngine.Random),
        typeof(RigidbodyConstraints),
        typeof(CinemachineImpulseSource),
        typeof(RenderMode),
        typeof(UniversalAdditionalCameraData),
        typeof(CameraRenderType),
        typeof(CameraClearFlags),
        /*************** Post Processing ***************/
        typeof(Bloom),
        typeof(ChannelMixer),
        typeof(ChromaticAberration),
        typeof(ColorAdjustments),
        typeof(ColorCurves),
        typeof(DepthOfField),
        typeof(FilmGrain),
        typeof(LensDistortion),
        typeof(LiftGammaGain),
        typeof(MotionBlur),
        typeof(PaniniProjection),
        typeof(ShadowsMidtonesHighlights),
        typeof(SplitToning),
        typeof(Tonemapping),
        typeof(Vignette),
        typeof(WhiteBalance),

#if !UNITY_2019_1_OR_NEWER
        typeof(GUIText),
#endif
        typeof(Input),
        typeof(Renderer),
        typeof(Camera),
        typeof(Screen),
        typeof(AnimationClip),
        typeof(AnimatorCullingMode),
        typeof(RuntimeAnimatorController),
        typeof(Animator),
#if UNITY_2018_1_OR_NEWER
        typeof(NavMeshAgent),
        typeof(NavMeshPath),
        typeof(NavMesh),
#else
        typeof(NavMeshAgent),
        typeof(NavMeshPath),
        typeof(NavMesh),
#endif
        typeof(RaycastHit),
        typeof(Physics),
        typeof(Resources),
        typeof(ResourceRequest),
        typeof(Mesh),
        typeof(SkinnedMeshRenderer),
        typeof(RenderTexture),
        typeof(RenderTextureFormat),
        typeof(RenderTextureReadWrite),
        typeof(Shader),
        typeof(Collider),
        typeof(SphereCollider),
        typeof(RenderSettings),
        typeof(MeshFilter),
        typeof(Material),
        typeof(SpriteRenderer),
        typeof(SystemInfo),
        typeof(SceneManager),
        typeof(LoadSceneMode),
        typeof(Scene),
        typeof(UnityEventBase),
        typeof(MeshRenderer),
        typeof(MaterialPropertyBlock),
        typeof(Volume),
        typeof(VolumeProfile),
        typeof(GraphicsDeviceType),
        typeof(TextAsset),
        typeof(Texture),
        typeof(AudioClip),
        typeof(AsyncOperationStatus),
        typeof(DeviceType),
        typeof(OperatingSystemFamily),
        typeof(Mathf),
        typeof(StaticBatchingUtility),
        typeof(LightmapSettings),
        typeof(AudioSource),
        typeof(Color),
        typeof(AnimationState),
        typeof(Animation),
        typeof(Graphics),
        typeof(Texture2D),
        typeof(Collision),
        typeof(MonoBehaviour),
        typeof(Behaviour),
        typeof(Util),

        /******************** UnityEngine.UI ***********************/
        typeof(Text),
        typeof(CanvasGroup),
        typeof(Canvas),
        typeof(Button),
        typeof(Toggle),
        typeof(ToggleGroup),
        typeof(InputField),
        typeof(ScrollRect),
        typeof(Scrollbar),
        typeof(Dropdown),
        typeof(Sprite),
        typeof(Slider),
        typeof(Image),
        typeof(RawImage),
        typeof(Dropdown),
        typeof(LayoutUtility),
        typeof(LayoutElement),
        typeof(LayoutRebuilder),
        typeof(VerticalLayoutGroup),
        typeof(GUIUtility),
        typeof(CollisionFlags),
        typeof(MaskableGraphic),
        typeof(Addressables),
        typeof(TextMeshPro),
        typeof(TextMeshProUGUI),
        typeof(TMP_SpriteAsset),
        typeof(TMP_InputField),
        typeof(TMP_Dropdown),
        typeof(TMP_Text),
        typeof(GraphicRaycaster),
        typeof(CanvasScaler),
        typeof(Selectable),
        typeof(UIBehaviour),
        typeof(LuaListAdapter),
        typeof(LuaGridAdapter),
        typeof(BaseParams),
        typeof(GridParams),
        typeof(LuaItemViewsHolder),
        typeof(LuaCellViewHolder),
        typeof(OSA<BaseParams, LuaItemViewsHolder>),
        typeof(GridAdapter<GridParams, LuaCellViewHolder>),
        typeof(TextMeshProAsyncExtensions),
        typeof(DOTweenAsyncExtensions),
        /******************** YieldInstruction ***********************/
        typeof(YieldInstruction),
        typeof(WaitForSeconds),
        typeof(WaitUntil),
        typeof(WaitForFixedUpdate),
        typeof(WaitForSecondsRealtime),
        typeof(WaitWhile),
        typeof(WaitForEndOfFrame),
        typeof(UniTask),
        typeof(UniTaskVoid),
        typeof(UniTask<int>),
        typeof(UniTask<float>),
        typeof(UniTask<long>),
        typeof(UniTask<bool>),
        typeof(UniTask<string>),
        typeof(UniTask<IList<IResourceLocation>>),
        typeof(UniTask<Dictionary<string, List<int>>>),
        typeof(UnityAsyncExtensions),
        typeof(UniTaskAsyncEnumerable),
        typeof(AddressablesAsyncExtensions),
        /********************Animancer *************************/
        typeof(AnimancerState),
        typeof(AnimancerEvent),
        typeof(AnimancerEvent.Sequence),
        typeof(AnimancerComponent),
        typeof(NamedAnimancerComponent),
        /********************A*Pathfinding*************************/
        typeof(AstarPath),
        typeof(RVOController),
        typeof(RVOObstacle),
        typeof(RVOSimulator),
        typeof(Seeker),
        typeof(AIPath),
        typeof(AIBase),
        /********************Json*************************/
        typeof(JObject),
        typeof(JArray),
        typeof(JsonConvert),
        typeof(System.Boolean),
    };

    [GCOptimize]
    public static List<Type> LuaCallCSharpStruct = new() {
        // typeof(Vector2),
        // typeof(Vector3),
        // typeof(Vector4),
        // typeof(Quaternion),
        // typeof(Color),
        // typeof(Ray),
        // typeof(Bounds),
        // typeof(Mathf),
        // typeof(Touch),
        // typeof(RaycastHit),
        // typeof(AnimatorStateInfo),
    };

    [BlackList]
    public static List<List<string>> BlackList = new() {
        new List<string> {"UnityEngine.Texture", "imageContentsHash"},
        new List<string> {"UnityEngine.MeshRenderer", "scaleInLightmap"},
        new List<string> {"UnityEngine.MeshRenderer", "stitchLightmapSeams"},
        new List<string> {"UnityEngine.MeshRenderer", "receiveGI"},
        new List<string> {"UnityEngine.AudioSource", "gamepadSpeakerOutputType"},
        new List<string> {"UnityEngine.AudioSource", "GamepadSpeakerOutputType"},
        new List<string> {"UnityEngine.AudioSource", "GamepadSpeakerSupportsOutputType", "UnityEngine.GamepadSpeakerOutputType"},
        new List<string> {"UnityEngine.AudioSource", "SetGamepadSpeakerRestrictedAudio", "System.Int32", "System.Boolean"},
        new List<string> {"UnityEngine.AudioSource", "SetGamepadSpeakerMixLevelDefault", "System.Int32"},
        new List<string> {"UnityEngine.AudioSource", "SetGamepadSpeakerMixLevel", "System.Int32", "System.Int32"},
        new List<string> {"UnityEngine.AudioSource", "DisableGamepadOutput"},
        new List<string> {"UnityEngine.AudioSource", "PlayOnGamepad", "System.Int32"},
        new List<string> {"Animancer.AnimancerComponent", "InitialUpdateMode"},
        new List<string> {"Animancer.AnimancerEvent+Sequence", "ShouldNotModifyReason"},
        new List<string> {"Animancer.AnimancerEvent+Sequence", "SetShouldNotModifyReason", "System.String"},
        new List<string> {"UnityEngine.WWW", "movie"},
        new List<string> {"UnityEngine.UI.Graphic", "OnRebuildRequested"},
        new List<string> {"UnityEngine.UI.Text", "OnRebuildRequested"},
        new List<string> {"UnityEngine.Input", "IsJoystickPreconfigured", "System.String"},
        new List<string> {"UnityEngine.Texture2D", "alphaIsTransparency"},
        new List<string> {"UnityEngine.Security", "GetChainOfTrustValue"},
        new List<string> {"UnityEngine.CanvasRenderer", "onRequestRebuild"},
        new List<string> {"UnityEngine.Light", "areaSize"},
        new List<string> {"UnityEngine.Light", "lightmapBakeType"},
        new List<string> {"UnityEngine.Light", "SetLightDirty"},
        new List<string> {"UnityEngine.Light", "shadowRadius"},
        new List<string> {"UnityEngine.Light", "shadowAngle"},
        new List<string> {"UnityEngine.AnimatorOverrideController", "PerformOverrideClipListCleanup"},
#if !UNITY_WEBPLAYER
        new List<string> {"UnityEngine.Application", "ExternalEval"},
#endif
        new List<string> {"UnityEngine.GameObject", "networkView"}, //4.6.2 not support
        new List<string> {"UnityEngine.Component", "networkView"}, //4.6.2 not support
        new List<string> {"System.IO.FileInfo", "GetAccessControl", "System.Security.AccessControl.AccessControlSections"},
        new List<string> {"System.IO.FileInfo", "SetAccessControl", "System.Security.AccessControl.FileSecurity"},
        new List<string> {"System.IO.DirectoryInfo", "GetAccessControl", "System.Security.AccessControl.AccessControlSections"},
        new List<string> {"System.IO.DirectoryInfo", "SetAccessControl", "System.Security.AccessControl.DirectorySecurity"},
        new List<string> {"System.IO.DirectoryInfo", "CreateSubdirectory", "System.String", "System.Security.AccessControl.DirectorySecurity"},
        new List<string> {"System.IO.DirectoryInfo", "Create", "System.Security.AccessControl.DirectorySecurity"},
        new List<string> {"UnityEngine.MonoBehaviour", "runInEditMode"},
#if UNITY_2018_1_OR_NEWER
        new List<string> {"UnityEngine.QualitySettings", "streamingMipmapsRenderersPerFrame"},
#endif
    };

    //[MenuItem("XLua/获取所有的LuaCallCSharp")]
    public static List<Type> GetLuaCallCSharpList() {
        //TODO 获取所有使用了LuaCallCSharp这个Attribute的类

        return null;
    }

    #region 其它库的注册

    /// <summary>
    /// dotween的扩展方法在lua中调用
    /// </summary>
    [LuaCallCSharp]
    public static List<Type> dotween_lua_call_cs_list = new() {
        typeof(AutoPlay),
        typeof(AxisConstraint),
        typeof(Ease),
        typeof(LogBehaviour),
        typeof(LoopType),
        typeof(PathMode),
        typeof(PathType),
        typeof(RotateMode),
        typeof(ScrambleMode),
        typeof(TweenType),
        typeof(UpdateType),
        typeof(DOTween),
        typeof(DOVirtual),
        typeof(EaseFactory),
        typeof(Tweener),
        typeof(Tween),
        typeof(Sequence),
        typeof(TweenParams),
        typeof(ABSSequentiable),
        typeof(TweenerCore<Vector3, Vector3, DG.Tweening.Plugins.Options.VectorOptions>),
        typeof(TweenerCore<Single, Single, DG.Tweening.Plugins.Options.FloatOptions>),
        typeof(TweenCallback),
        typeof(TweenExtensions),
        typeof(TweenSettingsExtensions),
        typeof(ShortcutExtensions),
        typeof(DOTweenModuleAudio),
        typeof(DOTweenModulePhysics),
        typeof(DOTweenModuleSprite),
        typeof(DOTweenModulePhysics2D),
        typeof(DOTweenModuleUtils),
        typeof(DOTweenModuleUI),
        typeof(DOTweenModuleUnityVersion),

        //在生成xlua的代码时以下会报错
        //typeof(DG.Tweening.DOTweenPath),
        //typeof(DG.Tweening.DOTweenVisualManager),
    };

    #endregion
}