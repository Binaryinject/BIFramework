/*
Copyright (c) 2020 Omar Duarte
Unauthorized copying of this file, via any medium is strictly prohibited.
Writen by Omar Duarte, 2020.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Assertions;
using System.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace PluginMaster {
    public class PsdImportWindow : EditorWindow {
        #region VARIABLES

        #region EDITOR

        private GUISkin _skin = null;
        private const int _layerRowHeight = 40;
        private const int _layerRowPadding = 4;
        private const int _hierarchyThumbnailH = 32;
        private int _hierarchyThumbnailW = 32;
        private Rect _previewRect = Rect.zero;
        private Vector2 _layerScrollPos = Vector2.zero;
        private Texture2D _resultTexture = null;
        private bool _updatePreview = false;
        private int _importWidth = 0;
        private int _importHeight = 0;
        private float _aspectRatio = 0f;
        private float _pixelsPerUnit = 100f;
        private bool _createAtlas = true;
        private int _atlasMaxSize = 2048;
        private bool _importIntoSelectedObject = true;
        private string _rootName = null;
        private static string _outputFolder = "Assets/";
        private bool _addPsComponents = false;
        private bool _importInVisibleLayers = true;
        private PsGroup.BlendingShaderType _blendingShader = PsGroup.BlendingShaderType.DEFAULT;
        private Texture2D _logoTexture = null;
        private string _skinSpritePath;

        #endregion

        #region HIERARCHY ITEMS

        private abstract class HierarchyItem {
            public readonly int Id = -1;
            protected HierarchyItem(int id) => (Id) = (id);
        }

        private class LayerItem : HierarchyItem {
            public readonly Texture2D TextureNoBorder = null;
            public readonly Texture2D TextureWithBorder = null;

            public LayerItem(int id, Texture2D textureNoBorder, Texture2D textureWithBorder) : base(id) =>
                (TextureNoBorder, TextureWithBorder) = (textureNoBorder, textureWithBorder);
        }

        private class GroupItem : HierarchyItem {
            public bool IsOpen { get; set; }

            public GroupItem(int id, bool isOpen) : base(id) => (IsOpen) = (isOpen);
        }

        private Dictionary<int, HierarchyItem> _hierarchyItems = new();
        private int _openItemCount = 0;

        #endregion

        #region FILE

        private string _psdPath = "";
        private string _prevPsdPath = "";
        private bool _pathChanged = false;
        private PsdFile _psdFile = null;
        private Thread _psdFileThread = null;
        private float _loadingProgress = 0f;

        #endregion

        #region PREVIEW LAYERS

        [DebuggerDisplay("Name = {Name}")]
        private class PreviewLayer {
            public readonly string Name = null;
            public readonly Texture2D Texture = null;
            public readonly PsdBlendModeType BlendMode = PsdBlendModeType.NORMAL;
            public bool Visible { get; set; }
            public readonly float Alpha = 1f;

            public PreviewLayer(string name, Texture2D texture, PsdBlendModeType blendMode, bool visible, float alpha) =>
                (Name, Texture, BlendMode, Visible, Alpha) = (name, texture, blendMode, visible, alpha);
        }

        private Dictionary<int, PreviewLayer> _previewLayers = new();

        #endregion

        #region TEXTURES

        private Queue<Layer> _textureLoadingPendingLayers = new();
        private Thread _loadThumbnailThread = null;

        private int _textureCount = 0;

        private class PendingData {
            public bool pending = false;
            public Layer layer = null;
            public Color32[] thumbnailPixels = null;
            public Color32[] thumbnailPixelsWithBorder = null;
            public Color32[] layerPixels = null;
            public Rect layerRect = Rect.zero;

            public void Reset() {
                pending = false;
                layer = null;
                thumbnailPixels = null;
                thumbnailPixelsWithBorder = null;
                layerPixels = null;
                layerRect = Rect.zero;
            }
        }

        private PendingData _pixelsPending = new();
        private static bool _repaint = false;
        private TextureUtils.LayerTextureLoader _textureLoader = null;
        private Dictionary<int, Tuple<Texture2D, Rect>> _layerTextures = new();
        private bool _loadingError = false;
        private string _errorMsg = null;

        #endregion

        #region OBJECTS CREATION

        private PsLayerCreator _layerCreator = null;

        #endregion

        #endregion

        #region EDITOR

        #region WINDOW

        [MenuItem("Tools/Plugin Master/Psd Import", false, 200)]
        public static void ShowWindow() => GetWindow<PsdImportWindow>();


        void OnEnable() {
            var path = new FileInfo(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:Script PsdImportWindow")[0]));
            var directory = $"Assets{path.Directory.Parent.ToString().Substring(Application.dataPath.Length)}";
            _skinSpritePath = $"{directory}/Skin/Sprites";
            _skin = AssetDatabase.LoadAssetAtPath<GUISkin>($"{directory}/Skin/PsdImporterSkin.guiskin");
            Assert.IsNotNull(_skin);
            _logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{_skinSpritePath}/PsdImportLogo.png");
            Assert.IsNotNull(_logoTexture);
#if UNITY_2019_1_OR_NEWER
            minSize = new Vector2(838, 548);
#else
            maxSize = minSize = new Vector2(834, 548);
#endif
        }

        private void OnDestroy() {
            if (_textureLoader != null || _psdFile != null) {
                EditorUtility.ClearProgressBar();
            }

            if (_textureLoader != null) {
                _textureLoader.ProgressChanged -= OnTextureLoading;
                _textureLoader.OnLoadingComplete -= OnTextureLoadingComplete;
                _textureLoader.Cancel();
                _textureLoader = null;
                _loadThumbnailThread.Abort();
                _pixelsPending.pending = false;
                _repaint = false;
            }

            DestroyAllTextures();

            if (_psdFile != null) {
                _psdFile.OnProgressChanged -= OnFileLoading;
                _psdFile.OnDone -= OnFileLoaded;
                _psdFile.OnError -= OnFileLoadingError;
                _psdFile.Cancel();
                _psdFile = null;
                _psdFileThread.Abort();
                _repaint = false;
            }
        }

        private bool BrowsePanel() {
            using (new GUILayout.VerticalScope()) {
                if (GUILayout.Button("Browse...", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))) {
                    _prevPsdPath = _psdPath;
                    var psdPath = EditorUtility.OpenFilePanel("Select Psd file to import...", string.Empty, "psd");
                    if (psdPath != _prevPsdPath && psdPath.Length != 0) {
                        _psdPath = psdPath;
                        LoadFile(_psdPath);
                    }
                }

                if (_psdFile == null) return false;

                using (new GUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(System.IO.Path.GetFileName(_psdPath), _skin.GetStyle("pathBox"));
                    //var reloadTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{_skinSpritePath}/Sprites/reload.png");
                    if (GUILayout.Button("Reload")) {
                        DestroyAllTextures();
                        LoadFile(_psdPath);
                    }
                }
            }

            if (_pathChanged) {
                _pathChanged = false;
                _hierarchyItems.Clear();
                _textureLoadingPendingLayers.Clear();
                _layerTextures.Clear();
                CreateItemDictionary(_psdFile.RootLayers.ToArray());
                LoadPendingTextures();
            }

            return true;
        }

        private bool ProgressBar() {
            if (_loadingProgress < 1f) {
                EditorUtility.DisplayProgressBar("Loading", ((int) (_loadingProgress * 100)).ToString() + " %", _loadingProgress);
                return false;
            }

            if (_pixelsPending.pending || _textureLoader != null) return false;
            return true;
        }

        private void PreviewPanel() {
            if (_updatePreview) {
                _previewLayers.Clear();

                foreach (var item in _hierarchyItems.Values) {
                    if (!(item is LayerItem)) continue;
                    if (((LayerItem) item).TextureNoBorder == null) continue;
                    var scaledTexture = TextureUtils.GetScaledTexture(((LayerItem) item).TextureNoBorder, (int) _previewRect.width, (int) _previewRect.height);
                    var layer = _psdFile.GetLayer(item.Id);
                    _previewLayers.Add(item.Id,
                        new PreviewLayer(layer.Name, scaledTexture, layer.BlendModeInHierarchy, layer.Visible && layer.VisibleInHierarchy,
                            layer.AlphaInHierarchy));
                }

                _importWidth = Mathf.RoundToInt(_psdFile.BaseLayer.Rect.width);
                _importHeight = Mathf.RoundToInt(_psdFile.BaseLayer.Rect.height);
                _aspectRatio = _psdFile.BaseLayer.Rect.width / _psdFile.BaseLayer.Rect.height;

                _resultTexture = GetPreviewTexture((int) _previewRect.width, (int) _previewRect.height);
                _resultTexture = TextureUtils.SetTextureBorder(_resultTexture, (int) _previewRect.width, (int) _previewRect.height);
                _updatePreview = false;
            }

            using (new GUILayout.VerticalScope()) {
                GUILayout.FlexibleSpace();
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();
                    var style = new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleCenter};
                    GUILayout.Label(_resultTexture, style);
                    GUILayout.FlexibleSpace();
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void HierarchyPanel() {
            using (new GUILayout.VerticalScope()) {
                using (var scrollView =
                    new EditorGUILayout.ScrollViewScope(_layerScrollPos, false, true, GUIStyle.none, _skin.verticalScrollbar, GUIStyle.none)) {
                    _layerScrollPos = scrollView.scrollPosition;
                    using (new GUILayout.VerticalScope(_skin.GetStyle("bottomBorder"))) {
                        _openItemCount = 0;
                        CreateLayerHierarchy(_psdFile.RootLayers.ToArray());
                    }
                }
            }
        }

        private void ImportVisiblePanel() {
            using (new GUILayout.VerticalScope(_skin.GetStyle("darkBox"), GUILayout.Width(400))) {
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.Space(4);
                    _importInVisibleLayers = EditorGUILayout.Toggle(_importInVisibleLayers, GUILayout.Width(18));
                    EditorGUILayout.LabelField("Import invisible Layers", new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleLeft});
                }
            }
        }

        private void SizePanel() {
            var labelMiddleRightStyle = new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleRight};
            var textFieldMiddleRightStyle = new GUIStyle(EditorStyles.textField) {alignment = TextAnchor.MiddleRight};

            using (new GUILayout.VerticalScope(_skin.GetStyle("darkBox"), GUILayout.Width(400))) {
                using (new GUILayout.HorizontalScope(GUILayout.MaxWidth(400))) {
                    GUILayout.Space(104);
                    GUILayout.Label("Pixels", GUILayout.Width(100));
                    GUILayout.Label("Unit Size", GUILayout.Width(100));
                }

                using (new GUILayout.HorizontalScope(GUILayout.MaxWidth(400))) {
                    GUILayout.Label("Width: ", labelMiddleRightStyle, GUILayout.Width(100));
                    _importWidth = EditorGUILayout.IntField(_importWidth, textFieldMiddleRightStyle, GUILayout.Width(100));
                    _importWidth = Mathf.RoundToInt(
                        EditorGUILayout.FloatField((float) _importWidth / _pixelsPerUnit, textFieldMiddleRightStyle, GUILayout.Width(100)) * _pixelsPerUnit);
                    if (_importWidth < 1) _importWidth = 1;
                    _importHeight = Mathf.RoundToInt((float) _importWidth / _aspectRatio);
                    if (_importHeight < 1) {
                        _importHeight = 1;
                        _importWidth = Mathf.RoundToInt((float) _importHeight * _aspectRatio);
                    }
                }

                using (new GUILayout.HorizontalScope(GUILayout.MaxWidth(400))) {
                    GUILayout.Label("Height: ", labelMiddleRightStyle, GUILayout.Width(100));
                    _importHeight = EditorGUILayout.IntField(_importHeight, textFieldMiddleRightStyle, GUILayout.Width(100));
                    _importHeight = Mathf.RoundToInt(
                        EditorGUILayout.FloatField((float) _importHeight / _pixelsPerUnit, textFieldMiddleRightStyle, GUILayout.Width(100)) * _pixelsPerUnit);
                    if (_importHeight < 1) _importHeight = 0;
                    _importWidth = Mathf.RoundToInt((float) _importHeight * _aspectRatio);
                    if (_importWidth < 1) {
                        _importWidth = 1;
                        _importHeight = Mathf.RoundToInt((float) _importWidth / _aspectRatio);
                    }
                }

                GUILayout.Space(8);
                using (new GUILayout.HorizontalScope(GUILayout.MaxWidth(400))) {
                    GUILayout.Label("Pixels Per Unit: ", labelMiddleRightStyle, GUILayout.Width(204));
                    _pixelsPerUnit = EditorGUILayout.FloatField(_pixelsPerUnit, textFieldMiddleRightStyle, GUILayout.Width(100));
                }
            }
        }

        private void AtlasPanel() {
            using (new GUILayout.VerticalScope(_skin.GetStyle("darkBox"), GUILayout.Width(400))) {
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.Space(4);
                    _createAtlas = EditorGUILayout.Toggle(_createAtlas, GUILayout.Width(18));
                    EditorGUILayout.LabelField("Create Atlas", _skin.GetStyle("Label"), GUILayout.Width(151));
                    if (_createAtlas) {
                        EditorGUILayout.LabelField("Atlas Max Size: ", new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleRight}, GUILayout.Width(100));
                        _atlasMaxSize = EditorGUILayout.IntField(_atlasMaxSize, new GUIStyle(EditorStyles.textField) {alignment = TextAnchor.MiddleRight},
                            GUILayout.Width(100));
                    }
                }
            }
        }

        private void OutputFolderPanel() {
            using (new GUILayout.VerticalScope(_skin.GetStyle("darkBox"), GUILayout.Width(400))) {
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.Space(4);
                    EditorGUILayout.LabelField("Output Folder:", new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleLeft}, GUILayout.Width(80));

                    EditorGUILayout.LabelField(_outputFolder, _skin.GetStyle("pathBox"), GUILayout.Width(262));
                    if (GUILayout.Button("...", GUILayout.Width(29), GUILayout.Height(26))) {
                        var outputFolder = EditorUtility.OpenFolderPanel("Select Output folder...", _outputFolder, "Assets");
                        if (outputFolder.Contains(Application.dataPath)) {
                            _outputFolder = outputFolder.Replace(Application.dataPath, "Assets") + "/";
                        }
                        else if (outputFolder != "") {
                            EditorUtility.DisplayDialog("Output Folder Error", "Output folder must be under Assets folder", "Ok");
                        }
                    }
                }
            }
        }

        private void RootObjectPanel() {
            using (new GUILayout.VerticalScope(_skin.GetStyle("darkBox"), GUILayout.Width(400))) {
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.Space(4);
                    EditorGUILayout.LabelField("Root object name:", new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleLeft}, GUILayout.Width(100));
                    _rootName = EditorGUILayout.TextField(_rootName, new GUIStyle(EditorStyles.textField) {alignment = TextAnchor.MiddleRight},
                        GUILayout.Width(90));
                    GUILayout.Space(10);
                    _importIntoSelectedObject = EditorGUILayout.Toggle(_importIntoSelectedObject, GUILayout.Width(18));
                    EditorGUILayout.LabelField("Import into selected object", _skin.GetStyle("Label"), GUILayout.Width(150));
                }
            }
        }

        private void PsComponentsPanel() {
            using (new GUILayout.VerticalScope(_skin.GetStyle("darkBox"), GUILayout.Width(400))) {
                using (new GUILayout.HorizontalScope()) {
                    GUILayout.Space(4);
                    _addPsComponents = EditorGUILayout.Toggle(_addPsComponents, GUILayout.Width(18));
                    EditorGUILayout.LabelField("Add Ps Components:", new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleLeft}, GUILayout.Width(130));
                    if (_addPsComponents) {
                        EditorGUILayout.LabelField("Blending Sahder: ", new GUIStyle(_skin.label) {alignment = TextAnchor.MiddleRight}, GUILayout.Width(100));
                        _blendingShader = (PsGroup.BlendingShaderType) EditorGUILayout.Popup((int) _blendingShader,
                            new string[] {
                                PsGroup.GetShaderTypeName(PsGroup.BlendingShaderType.DEFAULT), PsGroup.GetShaderTypeName(PsGroup.BlendingShaderType.FAST),
                                PsGroup.GetShaderTypeName(PsGroup.BlendingShaderType.GRAB_PASS)
                            }, GUILayout.Width(120));
                    }
                }
            }
        }

        private void ButtonsPanel() {
            var importScale = _psdFile.BaseLayer.Rect.width > _psdFile.BaseLayer.Rect.height
                ? _importWidth / _psdFile.BaseLayer.Rect.width
                : _importHeight / _psdFile.BaseLayer.Rect.height;
            using (new GUILayout.HorizontalScope(GUILayout.Width(400))) {
                if (GUILayout.Button("Import PNG files")) {
                    if (Directory.Exists(_outputFolder)) Directory.Delete(_outputFolder, true);
                    var analysis = Path.GetFileNameWithoutExtension(_psdFile.Path + "_").Split('_');
                    var atlasFileName = $"{_outputFolder}_{(string.IsNullOrEmpty(analysis[1]) ? analysis[0] : analysis[1])}.spriteatlasv2";
                    if (File.Exists(atlasFileName)) File.Delete(atlasFileName);
                    AssetDatabase.Refresh();
                    var textures = PsLayerCreator.CreatePngFiles(_outputFolder, _layerTextures, _psdFile, importScale, _pixelsPerUnit, _createAtlas,
                        _atlasMaxSize, _importInVisibleLayers);
                    if (_createAtlas) {
                        TextureUtils.CreateAtlas(TextureUtils.OutputObjectType.UI_IMAGE, textures, _atlasMaxSize, atlasFileName, _pixelsPerUnit);
                    }
                }

                // if (GUILayout.Button("Create Sprites"))
                // {
                //     if (BlendModeWarning())
                //     {
                //         _layerCreator = new PsLayerCreator(
                //                 TextureUtils.OutputObjectType.SPRITE_RENDERER,
                //                 _importIntoSelectedObject,
                //                 _rootName,
                //                 _pixelsPerUnit,
                //                 _psdFile,
                //                 _previewLayers.Count,
                //                 _createAtlas,
                //                 _atlasMaxSize,
                //                 _outputFolder,
                //                 _importOnlyVisibleLayers,
                //                 _addPsComponents,
                //                 _blendingShader,
                //                 _layerTextures,
                //                 importScale);
                //         _layerCreator.CreateGameObjets();
                //     }
                // }
                if (GUILayout.Button("Create UI Images")) {
                    if (Directory.Exists(_outputFolder)) Directory.Delete(_outputFolder, true);
                    if (BlendModeWarning()) {
                        if (ScreenSpaceWarning()) {
                            _layerCreator = new PsLayerCreator(TextureUtils.OutputObjectType.UI_IMAGE, _importIntoSelectedObject, _rootName, _pixelsPerUnit,
                                _psdFile, _previewLayers.Count, _createAtlas, _atlasMaxSize, _outputFolder, _importInVisibleLayers, _addPsComponents,
                                _blendingShader, _layerTextures, importScale);
                            _layerCreator.CreateGameObjets();
                        }
                    }
                }
            }
        }

        private void OnGUI() {
            if (_loadingError && Event.current.type == EventType.Repaint) {
                EditorUtility.ClearProgressBar();
                _psdFile = null;
                _loadingError = false;
                _loadingProgress = 0f;
                _hierarchyItems.Clear();
                _textureLoadingPendingLayers.Clear();
                _layerTextures.Clear();
                _psdPath = _prevPsdPath;
                EditorUtility.DisplayDialog("File Error",
                    "There was an error while opening the file.\n" +
                    "Try opening the file in Photoshop or another editor and save it under a different name, then try importing it again.\n\n" +
                    "Error Details: " + _errorMsg, "Ok");
                _errorMsg = null;
                return;
            }

            GUI.skin = _skin;

            titleContent = new GUIContent("Psd Import", null, "Photoshop file Importer");

            using (new GUILayout.VerticalScope()) {
                GUILayout.Space(4);
                using (new GUILayout.VerticalScope()) {
                    GUILayout.Label(_logoTexture, GUILayout.Width(200), GUILayout.Height(44));
                }

                using (new GUILayout.HorizontalScope()) {
                    using (new GUILayout.VerticalScope()) {
                        if (!BrowsePanel()) return;
                        if (!ProgressBar()) return;
                        PreviewPanel();
                        HierarchyPanel();
                    }

                    using (new GUILayout.VerticalScope()) {
                        ImportVisiblePanel();
                        SizePanel();
                        AtlasPanel();
                        OutputFolderPanel();
                        RootObjectPanel();
                        PsComponentsPanel();
                        GUILayout.FlexibleSpace();
                        ButtonsPanel();
                    }
                }
            }
        }

        private bool ScreenSpaceWarning() {
            if (!_addPsComponents) return true;
            if (_blendingShader != PsGroup.BlendingShaderType.FAST) return true;
            if (!_importIntoSelectedObject) return true;
            var rootParent = Selection.activeTransform;
            if (rootParent == null) return true;
            var canvas = rootParent.GetComponent<Canvas>();
            if (canvas == null) return true;
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera) return true;
            return EditorUtility.DisplayDialog("Warning: Render mode will change.",
                "The Fast shader only works with: Screen Space - Camera.\n\n" +
                "Would you like to continue? The canvas render mode of the selected object will be set to Screen Space - Camera.\n" +
                "Or Would you like to go back and select a different shader?", "Continue", "Back to Import Window");
        }

        private bool BlendModeWarning() {
            if (!_addPsComponents) return true;
            if (_blendingShader == PsGroup.BlendingShaderType.GRAB_PASS) return true;

            foreach (var item in _hierarchyItems.Values) {
                var layer = _psdFile.GetLayer(item.Id);
                if (layer.VisibleInHierarchy && layer.Visible) {
                    switch (_blendingShader) {
                        case PsGroup.BlendingShaderType.DEFAULT:
                            if (layer.BlendModeKey != PsdBlendModeType.NORMAL && layer.BlendModeKey != PsdBlendModeType.PASS_THROUGH) {
                                return EditorUtility.DisplayDialog("Warning: Unsupported blend modes.",
                                    "One or more layers are set to blend modes not supported by the Default Shader.\n" +
                                    "The Default Shader only supports the Normal mode.\n\n" +
                                    "Would you like to continue? Unsupported modes will be set to Normal mode.\n" +
                                    "Or Would you like to go back and select a different shader?", "Continue", "Back to Import Window");
                            }

                            break;
                        case PsGroup.BlendingShaderType.FAST:
                            if (((PsdBlendModeType) layer.BlendModeKey).GrabPass) {
                                return EditorUtility.DisplayDialog("Warning: Unsupported blend modes.",
                                    "One or more layers are set to blend modes not supported by the Fast Shader.\n" +
                                    "The Fast Shader doesn't support the following modes:  " +
                                    "Darker Color, Lighter Color, Pin Light, Hard Mix, Difference, Hue, Saturation, Color and Luminosity.\n\n" +
                                    "Would you like to continue? Unsupported modes will be set to Normal mode.\n" +
                                    "Or Would you like to go back and select a different shader?", "Continue", "Back to Import Window");
                            }

                            break;
                    }
                }
            }

            return true;
        }

        #endregion

        #region PREVIEW HIERARCHY

        private void CreateLayerHierarchy(Layer[] layers) {
            foreach (var layer in layers) {
                GroupItem groupItem = null;
                using (new EditorGUILayout.HorizontalScope(_skin.GetStyle("tableRow"), GUILayout.MinHeight(_layerRowHeight))) {
                    using (new EditorGUILayout.VerticalScope(_skin.GetStyle("tableCell"), GUILayout.Width(_layerRowHeight))) {
                        GUILayout.Space(8);
                        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(_hierarchyThumbnailH - _layerRowPadding * 2))) {
                            GUILayout.Space(8);
                            var wasVisible = layer.Visible;
                            layer.Visible = EditorGUILayout.Toggle(layer.Visible,
                                _skin.GetStyle(layer.VisibleInHierarchy ? "visibilityToggle" : "invisibleToggle"), GUILayout.MaxWidth(_hierarchyThumbnailH));
                            if (wasVisible != layer.Visible) {
                                UpdateVisibility(layer, layer.Visible);
                                _updatePreview = true;
                            }
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope(GUILayout.Height(_hierarchyThumbnailH - _layerRowPadding * 2))) {
                        var indentSize = 18 * layer.HierarchyDepth;
                        GUILayout.Space(indentSize);
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(_layerRowHeight), GUILayout.Height(_layerRowHeight))) {
                            if (layer is LayerGroup) {
                                groupItem = (GroupItem) _hierarchyItems[layer.Id];
                                GUILayout.Space(14);
                                groupItem.IsOpen = EditorGUILayout.Toggle(groupItem.IsOpen, _skin.GetStyle("folderToggle"));
                            }
                            else {
                                ++_openItemCount;
                                LayerItem layerItem = (LayerItem) _hierarchyItems[layer.Id];
                                GUILayout.Space(4);
                                GUILayout.Label(layerItem.TextureWithBorder, GUILayout.Height(_hierarchyThumbnailH));
                            }
                        }

                        using (new EditorGUILayout.VerticalScope(_skin.GetStyle("tableCell"), GUILayout.MinHeight(_layerRowHeight))) {
                            GUILayout.Space(7);
                            EditorGUILayout.LabelField(layer.Name, _skin.GetStyle("rowlabel"));
                        }

                        using (new EditorGUILayout.VerticalScope(_skin.GetStyle("tableCell"), GUILayout.MinHeight(_layerRowHeight))) {
                            EditorGUILayout.LabelField("Blending: " + ((PsdBlendModeType) layer.BlendModeKey).Name, _skin.GetStyle("rowlabel"),
                                GUILayout.MaxHeight(15), GUILayout.Width(170));
                            EditorGUILayout.LabelField("Opacity: " + (int) ((float) layer.Opacity / 2.55f) + "%", _skin.GetStyle("rowlabel"),
                                GUILayout.MaxHeight(15), GUILayout.Width(170));
                        }
                    }
                }

                if (groupItem != null && groupItem.IsOpen) {
                    ++_openItemCount;
                    var group = (LayerGroup) layer;
                    CreateLayerHierarchy(group.Children);
                }
            }
        }

        private void UpdateVisibility(Layer layer, bool value, bool isChild = false) {
            if (isChild) {
                layer.VisibleInHierarchy = value;
            }

            if (layer is LayerGroup) {
                var children = ((LayerGroup) layer).Children;
                foreach (var child in children) {
                    UpdateVisibility(child, value, true);
                }
            }
            else if (_previewLayers.ContainsKey(layer.Id)) {
                _previewLayers[layer.Id].Visible = value && layer.Visible;
            }
        }

        private void DestroyAllTextures() {
            var textures = FindObjectsOfType<Texture2D>();
            DestroyTextures(textures);
        }

        private void DestroyTextures(Texture2D[] textures) {
            foreach (var texture in textures) {
                DestroyImmediate(texture);
            }

            Resources.UnloadUnusedAssets();
        }

        private void DestroyUnusedTextures() {
            var textures = FindObjectsOfType<Texture2D>();
            var textureSet = new HashSet<Texture2D>(textures);
            textureSet.Remove(_resultTexture);
            foreach (var item in _hierarchyItems.Values) {
                if (!(item is LayerItem)) continue;
                var layerItem = item as LayerItem;
                textureSet.Remove(layerItem.TextureNoBorder);
                textureSet.Remove(layerItem.TextureWithBorder);
            }

            foreach (var item in _layerTextures) {
                textureSet.Remove(item.Value.Item1);
            }

            DestroyTextures(textureSet.ToArray());
            textureSet.Clear();
            textureSet = null;
            textures = null;
        }

        #endregion

        #endregion

        #region LOAD FILE

        private void LoadFile(string path) {
            _pathChanged = false;
            _psdFile = new PsdFile(path);
            _psdFile.OnProgressChanged += OnFileLoading;
            _psdFile.OnDone += OnFileLoaded;
            _psdFile.OnError += OnFileLoadingError;
            var threadDelegate = new ThreadStart(_psdFile.Load);
            _psdFileThread = new Thread(threadDelegate);
            _psdFileThread.Start();
            DestroyAllTextures();
        }

        private void OnFileLoading(float progress) {
            _loadingProgress = progress * 0.3f;
            _repaint = true;
        }

        private void OnFileLoaded() {
            _psdFile.OnProgressChanged -= OnFileLoading;
            _psdFile.OnDone -= OnFileLoaded;
            _psdFile.OnError -= OnFileLoadingError;
            _pathChanged = true;
            var analysis = (Path.GetFileNameWithoutExtension(_psdPath) + "_").Split('_');
            _rootName = string.IsNullOrEmpty(analysis[1]) ? analysis[0] : analysis[1];
            _outputFolder = $"Assets/_StaticAssets/UI/{_rootName}/";
            var aspect = _psdFile.BaseLayer.Rect.width / _psdFile.BaseLayer.Rect.height;
            _hierarchyThumbnailW = (int) ((float) _hierarchyThumbnailH * aspect);
            _previewRect = GetPreviewRect((int) _psdFile.BaseLayer.Rect.width, (int) _psdFile.BaseLayer.Rect.height, 400, 300);
            _repaint = true;
        }

        private void OnFileLoadingError(string message) {
            _errorMsg = message;
            _loadingError = true;
            _psdPath = _prevPsdPath;
            _psdFile.OnProgressChanged -= OnFileLoading;
            _psdFile.OnDone -= OnFileLoaded;
            _psdFile.OnError -= OnFileLoadingError;
            _psdFile = null;
            _psdFileThread.Abort();
        }

        #endregion

        #region LOAD TEXTURES

        private void CreateItemDictionary(Layer[] layers) {
            foreach (var layer in layers) {
                if (layer is LayerGroup) {
                    _hierarchyItems.Add(layer.Id, new GroupItem(layer.Id, ((LayerGroup) layer).IsOpen));
                    CreateItemDictionary(((LayerGroup) layer).Children);
                    continue;
                }

                _textureLoadingPendingLayers.Enqueue(layer);
                ++_textureCount;
            }
        }

        private void LoadPendingTextures() {
            _pixelsPending = new PendingData();
            if (_textureLoadingPendingLayers.Count == 0) {
                LoadNextLayer();
                return;
            }

            var layer = _textureLoadingPendingLayers.Dequeue();

            _textureLoader = new TextureUtils.LayerTextureLoader(layer, (int) _psdFile.BaseLayer.Rect.width, (int) _psdFile.BaseLayer.Rect.height,
                _hierarchyThumbnailW, _hierarchyThumbnailH, (int) _previewRect.width, (int) _previewRect.height);
            _textureLoader.ProgressChanged += OnTextureLoading;
            _textureLoader.OnLoadingComplete += OnTextureLoadingComplete;
            _textureLoader.OnError += OnTextureLoadingError;
            var threadDelegate = new ThreadStart(_textureLoader.LoadLayerPixels);
            _loadThumbnailThread = new Thread(threadDelegate);
            _loadThumbnailThread.Name = layer.Name;
            _loadThumbnailThread.Start();
        }

        private void OnTextureLoading(float progress) {
            _loadingProgress = 0.3f + (progress + (float) _textureCount - (float) _textureLoadingPendingLayers.Count - 1f) / (float) _textureCount * 0.7f;
            _repaint = true;
        }

        private void OnTextureLoadingComplete(Layer layer, Color32[] thumbnailPixels, Color32[] thumbnailPixelsWithBorder, Color32[] layerPixels,
            Rect layerRect) {
            _textureLoader.ProgressChanged -= OnTextureLoading;
            _textureLoader.OnLoadingComplete -= OnTextureLoadingComplete;

            _pixelsPending.pending = true;
            _pixelsPending.layer = layer;
            _pixelsPending.thumbnailPixels = thumbnailPixels;
            _pixelsPending.thumbnailPixelsWithBorder = thumbnailPixelsWithBorder;
            _pixelsPending.layerPixels = layerPixels;
            _pixelsPending.layerRect = layerRect;

            _textureLoader = null;
        }

        private void OnTextureLoadingError() {
            _textureLoader.ProgressChanged -= OnTextureLoading;
            _textureLoader.OnLoadingComplete -= OnTextureLoadingComplete;
            _textureLoader.OnError -= OnTextureLoadingError;
            _textureLoader = null;
            _loadingError = true;
            _loadThumbnailThread.Abort();
        }

        private void LoadNextLayer() {
            if (_textureLoadingPendingLayers.Count == 0) {
                _loadingProgress = 1f;
                _updatePreview = true;
                EditorUtility.ClearProgressBar();
                _repaint = true;
                DestroyUnusedTextures();
            }
            else {
                LoadPendingTextures();
            }
        }

        private void Update() {
            if (_pixelsPending.pending && _textureLoader == null) {
                var layer = _pixelsPending.layer;
                if (_layerTextures.ContainsKey(layer.Id)) {
                    LoadNextLayer();
                    _pixelsPending.pending = false;
                }

                if (_pixelsPending.thumbnailPixels == null) {
                    _hierarchyItems.Add(layer.Id, new LayerItem(layer.Id, null, null));
                    _layerTextures.Add(layer.Id, new Tuple<Texture2D, Rect>(null, Rect.zero));
                    LoadNextLayer();
                    _pixelsPending.pending = false;
                    return;
                }

                var thumbnailTexture = new Texture2D((int) _previewRect.width, (int) _previewRect.height, TextureFormat.RGBA32, true);
                thumbnailTexture.SetPixels32(_pixelsPending.thumbnailPixels);
                thumbnailTexture.Apply();

                var hierarchyThumbnailTexture = new Texture2D(_hierarchyThumbnailW, _hierarchyThumbnailH, TextureFormat.RGBA32, true);
                hierarchyThumbnailTexture.SetPixels32(_pixelsPending.thumbnailPixelsWithBorder);
                hierarchyThumbnailTexture.Apply();
                _hierarchyItems.Add(layer.Id, new LayerItem(layer.Id, thumbnailTexture, hierarchyThumbnailTexture));

                var layerTexture = new Texture2D((int) _pixelsPending.layerRect.width, (int) _pixelsPending.layerRect.height, TextureFormat.RGBA32, true);
                layerTexture.SetPixels32(_pixelsPending.layerPixels);
                layerTexture.Apply();

                _layerTextures.Add(layer.Id, new Tuple<Texture2D, Rect>(layerTexture, _pixelsPending.layerRect));

                LoadNextLayer();

                _pixelsPending.pending = false;
                _pixelsPending.Reset();
            }

            if (_repaint) {
                Repaint();
                _repaint = false;
            }
        }

        #endregion

        #region PREVIEW IMAGE

        private Texture2D GetPreviewTexture(int width, int height) {
            var resultTexture = new Texture2D(width, height, TextureFormat.RGBA32, true);

            var resultTexturePixels = resultTexture.GetPixels();

            for (int i = 0; i < resultTexturePixels.Length; ++i) {
                var r = i / width;
                var c = i - r * width;
                resultTexturePixels[i] = (r % 16 < 8) == (c % 16 < 8) ? Color.white : new Color(0.8f, 0.8f, 0.8f);
            }

            for (int layerIdx = _previewLayers.Count - 1; layerIdx >= 0; --layerIdx) {
                var previewLayer = _previewLayers.ElementAt(layerIdx).Value;
                if (!previewLayer.Visible) continue;
                var sourcePixels = previewLayer.Texture.GetPixels();
                for (int i = 0; i < resultTexturePixels.Length; ++i) {
                    resultTexturePixels[i] = GetBlendedPixel(resultTexturePixels[i], sourcePixels[i], previewLayer.Alpha, previewLayer.BlendMode);
                }
            }

            resultTexture.SetPixels(resultTexturePixels);
            resultTexture.Apply();
            return resultTexture;
        }

        private struct FloatColor {
            public float r, g, b;

            public FloatColor(float r, float g, float b) {
                this.r = r;
                this.g = g;
                this.b = b;
            }

            public FloatColor(Color color) {
                r = color.r;
                g = color.g;
                b = color.b;
            }

            public static implicit operator Color(FloatColor c) => new(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b));
            public static implicit operator FloatColor(Color c) => new(c.r, c.g, c.b);
        }

        private static float GetLuminosity(FloatColor color) {
            return 0.3f * color.r + 0.59f * color.g + 0.11f * color.b;
        }

        private static FloatColor SetLuminosity(FloatColor RGBColor, float L) {
            var delta = L - GetLuminosity(RGBColor);
            return ClipColor(new FloatColor(RGBColor.r + delta, RGBColor.g + delta, RGBColor.b + delta));
        }

        private static FloatColor ClipColor(FloatColor color) {
            var L = GetLuminosity(color);
            var min = Mathf.Min(color.r, color.g, color.b);
            var max = Mathf.Max(color.r, color.g, color.b);

            var result = color;
            if (min < 0f) {
                result.r = L + (((color.r - L) * L) / (L - min));
                result.g = L + (((color.g - L) * L) / (L - min));
                result.b = L + (((color.b - L) * L) / (L - min));
            }

            if (max > 1f) {
                result.r = L + (((color.r - L) * (1f - L)) / (max - L));
                result.g = L + (((color.g - L) * (1f - L)) / (max - L));
                result.b = L + (((color.b - L) * (1f - L)) / (max - L));
            }

            return result;
        }

        private static float GetSaturation(FloatColor RGBColor) {
            var min = Mathf.Min(RGBColor.r, RGBColor.g, RGBColor.b);
            var max = Mathf.Max(RGBColor.r, RGBColor.g, RGBColor.b);
            return max - min;
        }

        private class ColorComponent {
            public delegate float BlendDelegate(float backdrop, float source);
        }

        private static FloatColor SetSaturation(FloatColor RGBColor, float S) {
            const int MIN_COLOR = 0, MID_COLOR = 1, MAX_COLOR = 2;
            int R = MIN_COLOR, G = MIN_COLOR, B = MIN_COLOR;
            float minValue, midValue, maxValue;

            if (RGBColor.r <= RGBColor.g && RGBColor.r <= RGBColor.b) {
                minValue = RGBColor.r;
                if (RGBColor.g <= RGBColor.b) {
                    G = MID_COLOR;
                    B = MAX_COLOR;
                    midValue = RGBColor.g;
                    maxValue = RGBColor.b;
                }
                else {
                    B = MID_COLOR;
                    G = MAX_COLOR;
                    midValue = RGBColor.b;
                    maxValue = RGBColor.g;
                }
            }
            else if (RGBColor.g <= RGBColor.r && RGBColor.g <= RGBColor.b) {
                minValue = RGBColor.g;
                if (RGBColor.r <= RGBColor.b) {
                    R = MID_COLOR;
                    B = MAX_COLOR;
                    midValue = RGBColor.r;
                    maxValue = RGBColor.b;
                }
                else {
                    B = MID_COLOR;
                    R = MAX_COLOR;
                    midValue = RGBColor.b;
                    maxValue = RGBColor.r;
                }
            }
            else {
                minValue = RGBColor.b;
                if (RGBColor.r <= RGBColor.g) {
                    R = MID_COLOR;
                    G = MAX_COLOR;
                    midValue = RGBColor.r;
                    maxValue = RGBColor.g;
                }
                else {
                    G = MID_COLOR;
                    R = MAX_COLOR;
                    midValue = RGBColor.g;
                    maxValue = RGBColor.r;
                }
            }

            if (maxValue > minValue) {
                midValue = (((midValue - minValue) * S) / (maxValue - minValue));
                maxValue = S;
            }
            else {
                midValue = maxValue = 0.0f;
            }

            minValue = 0.0f;

            return new FloatColor(R == MIN_COLOR ? minValue : (R == MID_COLOR ? midValue : maxValue),
                G == MIN_COLOR ? minValue : (G == MID_COLOR ? midValue : maxValue), B == MIN_COLOR ? minValue : (B == MID_COLOR ? midValue : maxValue));
        }

        private static float AlphaBlend(float backdrop, float blend, float sourceAlpha) {
            return backdrop * (1f - sourceAlpha) + blend * sourceAlpha;
        }

        private static Color GetBlendResult(Color backdrop, Color source, ColorComponent.BlendDelegate blendDelegate) {
            var result = new Color(AlphaBlend(backdrop.r, blendDelegate(backdrop.r, source.r), source.a),
                AlphaBlend(backdrop.g, blendDelegate(backdrop.g, source.g), source.a), AlphaBlend(backdrop.b, blendDelegate(backdrop.b, source.b), source.a));
            return result;
        }

        private static Color GetBlendResult(Color backdrop, Color blended, float sourceAlpha) {
            var result = new Color(AlphaBlend(backdrop.r, blended.r, sourceAlpha), AlphaBlend(backdrop.g, blended.g, sourceAlpha),
                AlphaBlend(backdrop.b, blended.b, sourceAlpha));
            return result;
        }

        private static Color GetBlendedPixel(Color backdrop, Color source, float layerAlpha, PsdBlendModeType blendMode) {
            source.a *= layerAlpha;
            if (blendMode == PsdBlendModeType.NORMAL) {
                return GetBlendResult(backdrop, source, source.a);
            }
            else if (blendMode == PsdBlendModeType.DISSOLVE) {
                var randomAlpha = source.a;
                if (UnityEngine.Random.value > randomAlpha) {
                    randomAlpha = 0;
                }

                var result = backdrop * (1 - randomAlpha) + source * randomAlpha;
                result.a = backdrop.a + source.a;
                return result;
            }
            /////////////////////////////////////////////////////////
            else if (blendMode == PsdBlendModeType.DARKEN) {
                ColorComponent.BlendDelegate Darken = delegate(float bc, float sc) { return Mathf.Min(bc, sc); };
                return GetBlendResult(backdrop, source, Darken);
            }
            else if (blendMode == PsdBlendModeType.MULTIPLY) {
                ColorComponent.BlendDelegate Multiply = delegate(float bc, float sc) { return bc * sc; };
                return GetBlendResult(backdrop, source, Multiply);
            }
            else if (blendMode == PsdBlendModeType.COLOR_BURN) {
                ColorComponent.BlendDelegate ColorBurn = delegate(float bc, float sc) { return sc == 0f ? 0f : 1f - Mathf.Min(1f, (1f - bc) / sc); };
                return GetBlendResult(backdrop, source, ColorBurn);
            }
            else if (blendMode == PsdBlendModeType.LINEAR_BURN) {
                ColorComponent.BlendDelegate LinearBurn = delegate(float bc, float sc) { return Mathf.Clamp01(bc + sc - 1f); };
                return GetBlendResult(backdrop, source, LinearBurn);
            }
            else if (blendMode == PsdBlendModeType.DARKER_COLOR) {
                return GetLuminosity(backdrop) < GetLuminosity(source) ? backdrop : GetBlendResult(backdrop, source, source.a);
            }
            /////////////////////////////////////////////////////////
            else if (blendMode == PsdBlendModeType.LIGHTEN) {
                ColorComponent.BlendDelegate Lighten = delegate(float bc, float sc) { return Mathf.Max(bc, sc); };
                return GetBlendResult(backdrop, source, Lighten);
            }
            else if (blendMode == PsdBlendModeType.SCREEN) {
                ColorComponent.BlendDelegate Screen = delegate(float bc, float sc) { return 1f - (1f - bc) * (1f - sc); };
                return GetBlendResult(backdrop, source, Screen);
            }
            else if (blendMode == PsdBlendModeType.COLOR_DODGE) {
                ColorComponent.BlendDelegate ColorDodge = delegate(float bc, float sc) { return sc == 1f ? 1f : Mathf.Min(1f, bc / (1f - sc)); };
                return GetBlendResult(backdrop, source, ColorDodge);
            }
            else if (blendMode == PsdBlendModeType.LINEAR_DODGE) {
                ColorComponent.BlendDelegate LinearDodge = delegate(float bc, float sc) { return bc + sc * source.a; };
                return GetBlendResult(backdrop, source, LinearDodge);
            }
            else if (blendMode == PsdBlendModeType.LIGHTER_COLOR) {
                return GetLuminosity(backdrop) > GetLuminosity(source) ? backdrop : GetBlendResult(backdrop, source, source.a);
            }
            /////////////////////////////////////////////////////////
            else if (blendMode == PsdBlendModeType.OVERLAY) {
                ColorComponent.BlendDelegate Overlay = delegate(float bc, float sc) { return bc > 0.5f ? (1f - 2f * (1f - bc) * (1f - sc)) : 2f * bc * sc; };
                return GetBlendResult(backdrop, source, Overlay);
            }
            else if (blendMode == PsdBlendModeType.SOFT_LIGHT) {
                ColorComponent.BlendDelegate SoftLight = delegate(float bc, float sc) {
                    if (sc <= 0.5f) {
                        return bc - (1f - 2f * sc) * bc * (1f - bc);
                    }
                    else {
                        var d = bc <= 0.25f ? ((16f * bc - 12f) * bc + 4f) * bc : Mathf.Sqrt(bc);
                        return bc + (2f * sc - 1) * (d - bc);
                    }
                };
                return GetBlendResult(backdrop, source, SoftLight);
            }
            else if (blendMode == PsdBlendModeType.HARD_LIGHT) {
                ColorComponent.BlendDelegate HardLight = delegate(float bc, float sc) { return bc <= 0.5f ? (1f - 2f * (1f - bc) * (1f - sc)) : 2f * bc * sc; };
                return GetBlendResult(backdrop, source, HardLight);
            }
            else if (blendMode == PsdBlendModeType.VIVID_LIGHT) {
                ColorComponent.BlendDelegate VividLight = delegate(float bc, float sc) {
                    return sc <= 0.5f ? sc == 0f ? 0f : Mathf.Clamp01(1f - (1f - bc) / (2f * sc)) : sc == 1f ? 1f : bc / (2 * (1f - sc));
                };
                return GetBlendResult(backdrop, source, VividLight);
            }
            else if (blendMode == PsdBlendModeType.LINEAR_LIGHT) {
                ColorComponent.BlendDelegate LinearLight = delegate(float bc, float sc) { return bc + 2f * sc - 1f; };
                return GetBlendResult(backdrop, source, LinearLight);
            }
            else if (blendMode == PsdBlendModeType.PIN_LIGHT) {
                ColorComponent.BlendDelegate PinLight = delegate(float bc, float sc) {
                    return bc < 2f * sc - 1f ? 2f * sc - 1f : (bc < 2f * sc ? bc : 2f * sc);
                };
                return GetBlendResult(backdrop, source, PinLight);
            }
            else if (blendMode == PsdBlendModeType.HARD_MIX) {
                ColorComponent.BlendDelegate HardMix = delegate(float bc, float sc) { return sc < 1f - bc ? 0f : 1f; };
                return GetBlendResult(backdrop, source, HardMix);
            }
            /////////////////////////////////////////////////////////
            else if (blendMode == PsdBlendModeType.DIFFERENCE) {
                ColorComponent.BlendDelegate Difference = delegate(float bc, float sc) { return Mathf.Abs(sc - bc); };
                return GetBlendResult(backdrop, source, Difference);
            }
            else if (blendMode == PsdBlendModeType.EXCLUSION) {
                ColorComponent.BlendDelegate Exclusion = delegate(float bc, float sc) { return sc + bc - 2f * sc * bc; };
                return GetBlendResult(backdrop, source, Exclusion);
            }
            else if (blendMode == PsdBlendModeType.SUBTRACT) {
                ColorComponent.BlendDelegate Substract = delegate(float bc, float sc) { return bc - sc; };
                return GetBlendResult(backdrop, source, Substract);
            }
            else if (blendMode == PsdBlendModeType.DIVIDE) {
                ColorComponent.BlendDelegate Divide = delegate(float bc, float sc) { return sc == 0f ? 1f : bc / sc; };
                return GetBlendResult(backdrop, source, Divide);
            }
            /////////////////////////////////////////////////////////
#if UNITY_WEBGL
#else
            else if (blendMode == PsdBlendModeType.HUE) {
                var result = (Color) SetLuminosity(SetSaturation(source, GetSaturation(backdrop)), GetLuminosity(backdrop));
                return GetBlendResult(backdrop, result, source.a);
            }
            else if (blendMode == PsdBlendModeType.SATURATION) {
                var result = (Color) SetLuminosity(SetSaturation(backdrop, GetSaturation(source)), GetLuminosity(backdrop));
                return GetBlendResult(backdrop, result, source.a);
            }
            else if (blendMode == PsdBlendModeType.COLOR) {
                var result = (Color) SetLuminosity(source, GetLuminosity(backdrop));
                return GetBlendResult(backdrop, result, source.a);
            }
            else if (blendMode == PsdBlendModeType.LUMINOSITY) {
                var result = (Color) SetLuminosity(backdrop, GetLuminosity(source));
                return GetBlendResult(backdrop, result, source.a);
            }
#endif
            /////////////////////////////////////////////////////////
            else if (blendMode == PsdBlendModeType.PASS_THROUGH) {
                return backdrop;
            }

            return Color.black;
        }

        #endregion

        #region UTILS

        private static Rect GetPreviewRect(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight) {
            var resultWidth = maxWidth;
            var aspectRatio = (float) sourceWidth / (float) sourceHeight;
            var resultHeight = (int) ((float) resultWidth / aspectRatio);

            if (resultHeight > maxHeight) {
                resultHeight = maxHeight;
                resultWidth = (int) ((float) resultHeight * aspectRatio);
            }

            if (resultWidth < sourceWidth && resultHeight < sourceHeight) {
                return new Rect(0, 0, resultWidth, resultHeight);
            }

            return new Rect(0, 0, sourceWidth, sourceHeight);
        }

        #endregion
    }
}