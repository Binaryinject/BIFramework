using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
using Sirenix.OdinInspector.Editor;
#endif


[CreateAssetMenu(fileName = "GlobalShaderParams", menuName = "SO/GlobalShaderParams")]
public class GlobalShaderParams : ScriptableObject {
#if UNITY_EDITOR
    [MenuItem("SO/GlobalShaderParams")]
    static void SelectionCommand() {
        var go = AssetDatabase.LoadAssetAtPath<Object>("Assets/_DynamicAssets/SO/ShaderParams");
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
    }
#endif
    public enum DataTypes {
        Float,
        Bool,
        Texture,
        CubeMap,
        Vector,
        Color,
        HDRColor,
    };

    [Serializable]
    public class ShaderVariable {
        [HideLabel]
        [ShowIf("enableSetting")]
        [GUIColor(0, 1, 0)]
        public string describe = "";

        [NonSerialized]
        public bool enableSetting = false;

        [ShowIf("enableSetting")]
        [GUIColor(1, 0.92f, 0.016f)]
        public DataTypes type = DataTypes.Float;

        [ShowIf("enableSetting")]
        public string name = "";

        [LabelText("Value")]
        [ShowIf("type", DataTypes.Float)]
        [PropertyRange("@minMaxRange.x", "@minMaxRange.y")]
        public float valueFloat;

        [ShowIf("@enableSetting && type == DataTypes.Float")]
        [MinMaxSlider(-1024, 1024, true)]
        public Vector2 minMaxRange = new(0, 1);

        [LabelText("Value")]
        [ShowIf("type", DataTypes.Bool)]
        public bool valueBool;

        [LabelText("Value")]
        [ShowIf("type", DataTypes.Texture)]
        [PreviewField]
        public Texture valueTexture;

        [LabelText("Value")]
        [ShowIf("type", DataTypes.CubeMap)]
        [PreviewField]
        public Cubemap valueCubemap;

        [LabelText("Value")]
        [ShowIf("type", DataTypes.Vector)]
        public Vector4 valueVector;


        [LabelText("Value")]
        [ShowIf("@type == DataTypes.Color && colorPalette == \"None\"")]
        public Color valueColor;

        [ShowIf("@type == DataTypes.Color && colorPalette != \"None\"")]
        [ColorPalette("$colorPalette")]
        [PropertyOrder(1)]
        [LabelText("Value")]
        [ShowInInspector]
        public Color valueColorGetter {
            get { return valueColor; }
            set { valueColor = value; }
        }


        [ShowIf("@enableSetting && type == DataTypes.Color")]
        [ValueDropdown("paletteNames")]
        public string colorPalette = "None";

#if UNITY_EDITOR
        public List<string> paletteNames {
            get {
                var names = new List<string> { "None" };
                names.AddRange(GlobalConfig<ColorPaletteManager>.Instance.ColorPalettes.Select(x => x.Name));
                return names;
            }
        }
#endif

        [LabelText("Value")]
        [ShowIf("type", DataTypes.HDRColor)]
        [ColorUsage(true, true)]
        public Color valueHDRColor;
    }

    [Serializable]
    public class ObjectVariable {
        public Vector3 position;
        public Vector3 scale = Vector3.one;
    }

    [Serializable]
    public class LightShaft {
        [LabelText("圣光")]
        [OnValueChanged("SetLightShaft")]
        public bool enable = false;

        [NonSerialized]
        public Vector3 lightRotate;

        public void SetLightShaft() {
            if (Application.isPlaying) {
                var vfx = GameObject.Find("@VFXROOT/$ENV");
                if (!vfx) return;
                var lightShaft = vfx.transform.Find("Particle Ray");
                if (lightShaft) {
                    lightShaft.rotation = Quaternion.Euler(lightRotate);
                    lightShaft.gameObject.SetActive(enable);
                }
            }
        }
    }
    
    [Serializable]
    public class Rain {
        [LabelText("雨")]
        [OnValueChanged("SetRain")]
        public bool enable = false;

        public void SetRain() {
            if (Application.isPlaying) {
                var vfx = GameObject.Find("@VFXROOT/$ENV");
                if (!vfx) return;
                var rain = vfx.transform.Find("Particle Rain");
                if (rain) {
                    rain.gameObject.SetActive(enable);
                }
            }
        }
    }
    
    [Serializable]
    public class Clouds {
        [LabelText("云")]
        [OnValueChanged("SetClouds")]
        public bool enable = false;

        public void SetClouds() {
            if (Application.isPlaying) {
                var vfx = GameObject.Find("@VFXROOT/$ENV");
                if (!vfx) return;
                var clouds = vfx.transform.Find("Clouds");
                if (clouds) {
                    clouds.gameObject.SetActive(enable);
                }
            }
        }
    }

    [Serializable]
    public class FogSetting {
        [LabelText("启用")]
        [OnValueChanged("SetFog")]
        public bool enable = true;

        [EnableIf("enable")]
        public Color color = Color.gray;

        [EnableIf("enable")]
        public FogMode mode = FogMode.Exponential;

        [EnableIf("enable")]
        public float fogDensity = 0.01f;
        
        [EnableIf("enable")]
        public float fogStartDistance = 0f;
        
        [EnableIf("enable")]
        public float fogEndDistance = 100f;
        
        public void SetFog() {
            if (Application.isPlaying) {
                RenderSettings.fog = enable;
                RenderSettings.fogColor = color;
                RenderSettings.fogMode = mode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStartDistance;
                RenderSettings.fogEndDistance = fogEndDistance;
            }
        }
    }

    [Serializable]
    public class LightSetting {
        public Vector3 lightRotate;
        public float lightIntensity = 1;
        public Color lightColor = Color.white;
        
        private Light _dirLight = null;

        public void ClearLightData() {
            _dirLight = null;
        }

        public void OnLightChange() {
            if (!Application.isPlaying) return;
            if (_dirLight == null) {
                var vfx = GameObject.Find("@VFXROOT/$ENV");
                if (!vfx) return;
                var dlObject = vfx.transform.Find("Directional Light");
                _dirLight = dlObject.GetComponent<Light>();
                if (dlObject && _dirLight) {
                    lightRotate = _dirLight.transform.rotation.eulerAngles;
                    lightIntensity = _dirLight.intensity;
                    lightColor = _dirLight.color;
                }
                else {
                    Debug.LogError("没有找到 Directional Light");
                }
            }

            SetLightValues();
        }
        
        public void SetLightValues() {
            if (!Application.isPlaying) return;
            if (_dirLight == null) {
                var vfx = GameObject.Find("@VFXROOT/$ENV");
                if (!vfx) return;
                var dlObject = vfx.transform.Find("Directional Light");
                _dirLight = dlObject.GetComponent<Light>();
            }
            if (_dirLight) {
                _dirLight.transform.rotation = Quaternion.Euler(lightRotate);
                _dirLight.intensity = lightIntensity;
                _dirLight.color = lightColor;
            }
            else {
                Debug.LogError("没有找到 Directional Light");
            }
        }
    }

    [Serializable]
    public class GroupVariable {
        [HideLabel]
        [GUIColor(0, 1, 0)]
        [HorizontalGroup("TopLine")]
        public string describe = "";

        [HorizontalGroup("TopLine", Width = 10)]
        [GUIColor(1, 0.92f, 0.016f)]
        [HideLabel, NonSerialized, ShowInInspector, OnValueChanged("OnSettingChange")]
        public bool setting = false;

        public void OnSettingChange() {
            foreach (var shaderVariable in shaderVariables) {
                shaderVariable.enableSetting = setting;
            }
        }

        [LabelText("属性")]
        [ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "describe")]
        public List<ShaderVariable> shaderVariables = new();
    }

    [FoldoutGroup("天空材质")]
    public Material skyMaterial;
    //Variables
    [FoldoutGroup("后效处理")]
    public VolumeProfile volumeProfile;
    
    [FoldoutGroup("阳光")]
    [HideLabel]
    public LightSetting lightData;

    // [FoldoutGroup("雾")]
    // [HideLabel]
    // public FogSetting fogData;
    
    [FoldoutGroup("其他效果")]
    [HideLabel]
    public LightShaft lightShaftData;
    
    [FoldoutGroup("其他效果")]
    [HideLabel]
    public Rain rainData;
    
    [FoldoutGroup("其他效果")]
    [HideLabel]
    public Clouds cloudsData;

    [LabelText("属性")]
    [FoldoutGroup("全局属性")]
    [ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "describe")]
    public List<GroupVariable> groupVariables = new();
    
    public static List<GroupVariable> systemGroupVariables = null;
    
    [GUIColor(0, 1, 0)]
    [Button("复制全局属性", ButtonSizes.Large)]
    [ButtonGroup("CopyButton")]
    public void CopyGlobalVariables() {
        systemGroupVariables = groupVariables;
    }
    
    [GUIColor(0, 1, 0)]
    [Button("粘贴全局属性", ButtonSizes.Large)]
    [ButtonGroup("CopyButton")]
    public void PasteGlobalVariables() {
        if (systemGroupVariables != null) groupVariables = systemGroupVariables;
    }

    [GUIColor(1, 1, 0)]
    [Button("生效当前全部参数", ButtonSizes.Large)]
    public async void ApplyValuesButton() {
        shChange = true;
        await SetValues();
    }
    
    public async UniTaskVoid ApplyValues() {
        shChange = true;
        await SetValues();
    }

    private bool shChange = false;
    private Cubemap ambientCubemap = null;
    private static GlobalShaderParams shaderParams = null;
    private static readonly int DirLightIntensity = Shader.PropertyToID("_DirLightIntensity");

    public void ResetAmbientCubemap() {
        ambientCubemap = null;
    }
    public async UniTask BakeSHBuffers() {
        if (!shChange) return;
        shChange = false;
        var baker = SHBaker.Instance;
        baker.SetSHParams(await baker.BakeSHRuntime(Vector3.zero, ambientCubemap));
    }

    public static async UniTask ApplyProfile(string shaderParamFileName = "GlobalShaderParams") {
        if (shaderParams) Addressables.Release(shaderParams);
        shaderParams = await Addressables.LoadAssetAsync<GlobalShaderParams>($"Assets/_DynamicAssets/SO/ShaderParams/{shaderParamFileName}.asset");
        Debug.Log($"<color=yellow>[{shaderParamFileName}]</color> Profile Used");
        shaderParams.ResetAmbientCubemap();
        shaderParams.shChange = true;
        await shaderParams.SetValues();
    }

    public async UniTask SetValues() {
        if (skyMaterial) RenderSettings.skybox = skyMaterial;
        lightShaftData.lightRotate = lightData.lightRotate;
        lightShaftData.SetLightShaft();
        cloudsData.SetClouds();
        rainData.SetRain();
        lightData.SetLightValues();
        if (volumeProfile != null) {
            var vp = GameObject.Find("PostProcessVolume");
            if (vp) vp.GetComponent<Volume>().profile = volumeProfile;
        }
        Shader.SetGlobalFloat(DirLightIntensity, lightData.lightIntensity);
        foreach (var group in groupVariables) {
            foreach (var variable in group.shaderVariables) {
                switch (variable.type) {
                    case DataTypes.Float:
                        Shader.SetGlobalFloat(variable.name, variable.valueFloat);
                        break;
                    case DataTypes.Bool:
                        if (variable.valueBool) Shader.EnableKeyword(variable.name);
                        else Shader.DisableKeyword(variable.name);
                        break;
                    case DataTypes.Texture:
                        if (variable.valueTexture) Shader.SetGlobalTexture(variable.name, variable.valueTexture);
                        break;
                    case DataTypes.CubeMap:
                        if (variable.valueCubemap) {
                            if (variable.name == "_Ambient") {
#if UNITY_EDITOR
                                var assetPath = AssetDatabase.GetAssetPath(variable.valueCubemap);
                                var ti = (TextureImporter)TextureImporter.GetAtPath(assetPath);
                                var setting = ti.GetDefaultPlatformTextureSettings();
                                if (!ti.isReadable || setting.maxTextureSize > 128) {
                                    ti.isReadable = true;
                                    setting.maxTextureSize = 128;
                                    ti.isReadable = true;
                                    ti.SetPlatformTextureSettings(setting);
                                    AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                                }
#endif
                                if (ambientCubemap != variable.valueCubemap) shChange = true;
                                ambientCubemap = variable.valueCubemap;
                            }
                            else 
                                Shader.SetGlobalTexture(variable.name, variable.valueCubemap);
                        }
                        break;
                    case DataTypes.Vector:
                        Shader.SetGlobalVector(variable.name, variable.valueVector);
                        break;
                    case DataTypes.Color:
                        Shader.SetGlobalColor(variable.name, variable.valueColor);
                        break;
                    case DataTypes.HDRColor:
                        Shader.SetGlobalColor(variable.name, variable.valueHDRColor);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        
        await BakeSHBuffers();
    }
}