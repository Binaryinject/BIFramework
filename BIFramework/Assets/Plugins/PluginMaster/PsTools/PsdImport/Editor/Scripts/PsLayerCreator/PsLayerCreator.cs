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

#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;


namespace PluginMaster {
    public class PsLayerCreator {
        private TextureUtils.OutputObjectType _outputType;
        private bool _importIntoSelectedObject;
        private string _rootName;
        private float _pixelsPerUnit;
        private PsdFile _psdFile;
        private int _lastSortingOrder;
        private bool _createAtlas;
        private int _atlasMaxSize;
        private string _outputFolder;
        private bool _importInVisibleLayers;
        private bool _addPsComponents;
        private PsGroup.BlendingShaderType _blendingShader;
        private Dictionary<int, Tuple<Texture2D, Rect>> _layerTextures;
        private float _scale;
        private RectTransform parentRectTransform;

        private GameObject _lastObject = null;

        public PsLayerCreator(TextureUtils.OutputObjectType outputType, bool importIntoSelectedObject, string rootName, float pixelsPerUnit, PsdFile psdFile,
            int lastSortingOrder, bool createAtlas, int atlasMaxSize, string outputFolder, bool importInVisibleLayers, bool addPsComponents,
            PsGroup.BlendingShaderType blendingShader, Dictionary<int, Tuple<Texture2D, Rect>> layerTextures, float scale) {
            _outputType = outputType;
            _importIntoSelectedObject = importIntoSelectedObject;
            _rootName = rootName;
            _pixelsPerUnit = pixelsPerUnit;
            _psdFile = psdFile;
            _lastSortingOrder = lastSortingOrder;
            _createAtlas = createAtlas;
            _atlasMaxSize = atlasMaxSize;
            _outputFolder = outputFolder;
            _importInVisibleLayers = importInVisibleLayers;
            _addPsComponents = addPsComponents;
            _blendingShader = blendingShader;
            _layerTextures = layerTextures;
            _scale = scale;
        }

        public static List<Sprite> CreatePngFiles(string outputFolder, Dictionary<int, Tuple<Texture2D, Rect>> layerTextures, PsdFile psdFile, float scale,
            float pixelsPerUnit, bool createAtlas, int atlasMaxSize, bool importOnlyVisibleLayers) {
            var textures = new List<Texture2D>(layerTextures.Select(obj => obj.Value.Item1));
            var namesAndTexturesList = new List<Sprite>();
            foreach (var item in layerTextures.ToList()) {
                if (item.Value.Item1 == null) continue;
                var layer = psdFile.GetLayer(item.Key);
                if (importOnlyVisibleLayers && !(layer.Visible && layer.VisibleInHierarchy)) continue;
                var layerName = layer.Name;
                var scaledRect = new Rect(item.Value.Item2.x * scale, item.Value.Item2.y * scale, item.Value.Item2.width * scale,
                    item.Value.Item2.height * scale);
                var scaledTexture = TextureUtils.GetScaledTexture(item.Value.Item1, (int) scaledRect.width, (int) scaledRect.height);
                var sprite = TextureUtils.SavePngAsset(scaledTexture, outputFolder + layerName + ".png", pixelsPerUnit);
                namesAndTexturesList.Add(sprite);
            }
            
            return namesAndTexturesList;
        }

        public void CreateGameObjets() {
            foreach (var item in _layerTextures.ToList()) {
                if (item.Value.Item1 == null) continue;
                var scaledRect = new Rect(item.Value.Item2.x * _scale, item.Value.Item2.y * _scale, item.Value.Item2.width * _scale,
                    item.Value.Item2.height * _scale);
                var scaledTexture = TextureUtils.GetScaledTexture(item.Value.Item1, (int) scaledRect.width, (int) scaledRect.height);
                _layerTextures[item.Key] = new Tuple<Texture2D, Rect>(scaledTexture, scaledRect);
            }

            Transform rootParent = null;
            if (_importIntoSelectedObject) {
                rootParent = Selection.activeTransform;
            }

            GameObject root = new GameObject(_rootName);
            root.transform.parent = rootParent;
            _lastObject = root;

            if (_outputType == TextureUtils.OutputObjectType.UI_IMAGE) {
                if (root.transform.GetComponentInParent<Canvas>() == null) {
                    EditorUtility.DisplayDialog("Error", "Please choose to child canvas component!", "OK");
                    GameObject.DestroyImmediate(root);
                    return;
                }

                parentRectTransform = root.transform.GetComponentInParent<RectTransform>();
                var rectTransform = root.AddComponent<RectTransform>();
                SetRectTransform(rectTransform, parentRectTransform.rect);
                rectTransform.gameObject.layer = LayerMask.NameToLayer("UI");
                // if (_blendingShader == PsGroup.BlendingShaderType.FAST)
                // {
                //     var canvas = root.transform.GetComponentInParent<Canvas>();
                //     canvas.renderMode = RenderMode.ScreenSpaceCamera;
                //     canvas.worldCamera = Camera.main;
                //     canvas.sortingOrder = 20000;
                // }
                // else
                // {
                //     root.transform.GetComponentInParent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                // }
            }

            var analysis = (Path.GetFileNameWithoutExtension(_psdFile.Path) + "_").Split('_');
            var atlasFileName = $"{_outputFolder}_{(string.IsNullOrEmpty(analysis[1]) ? analysis[0] : analysis[1])}.spriteatlasv2";
            if (File.Exists(atlasFileName)) File.Delete(atlasFileName);
            AssetDatabase.Refresh();

            var objectsAndTexturesList = new List<Tuple<string, GameObject, Sprite, Rect>>();
            CreateHierarchy(root.transform, _psdFile.RootLayers.ToArray(), _lastSortingOrder, out _lastSortingOrder, ref objectsAndTexturesList);
            root.transform.localPosition = Vector3.zero;

            if (_createAtlas) {
                TextureUtils.CreateAtlas(_outputType, objectsAndTexturesList.Select(v => v.Item3).ToList(), _atlasMaxSize, atlasFileName, _pixelsPerUnit);
            }
        }

        private void CreateHierarchy(Transform parentTransform, Layer[] children, int initialSortingOrder, out int lastSortingOrder,
            ref List<Tuple<string, GameObject, Sprite, Rect>> objectsAndTexturesList) {
            if (_outputType == TextureUtils.OutputObjectType.UI_IMAGE) {
                Array.Reverse(children);
                parentTransform.gameObject.layer = LayerMask.NameToLayer("UI");
            }

            lastSortingOrder = initialSortingOrder;
            foreach (var childLayer in children) {
                if (!_importInVisibleLayers && !(childLayer.Visible && childLayer.VisibleInHierarchy)) continue;

                GameObject childGameObject = null;

                string objName = childLayer.Name;

                if (childLayer is LayerGroup) {
                    childGameObject = new GameObject(objName);
                    childGameObject.SetActive(childLayer.Visible);
                    childGameObject.transform.parent = parentTransform.transform;
                    _lastObject = childGameObject;
                    CreateHierarchy(childGameObject.transform, ((LayerGroup) childLayer).Children, lastSortingOrder, out lastSortingOrder,
                        ref objectsAndTexturesList);
                    if (_addPsComponents) {
                        PsGroup groupComp = childGameObject.AddComponent<PsGroup>();
                        groupComp.Initialize((PsdBlendModeType) childLayer.BlendModeKey, childLayer.Alpha, childLayer.Visible, childLayer.VisibleInHierarchy,
                            _blendingShader);
                    }

                    if (_outputType == TextureUtils.OutputObjectType.UI_IMAGE) {
                        var rectTransform = childGameObject.AddComponent<RectTransform>();
                        SetRectTransform(rectTransform, childLayer.Rect);
                    }

                    continue;
                }

                Rect layerRect = _layerTextures[childLayer.Id].Item2;
                Texture2D texture = _layerTextures[childLayer.Id].Item1;
                if (texture == null) continue;

                childGameObject = new GameObject(objName);
                childGameObject.layer = LayerMask.NameToLayer("UI");
                childGameObject.transform.parent = parentTransform.transform;
                childGameObject.SetActive(childLayer.Visible);

                lastSortingOrder--;

                SpriteRenderer renderer = null;
                Image image = null;
                var hasImage = true;
                if (_outputType == TextureUtils.OutputObjectType.SPRITE_RENDERER) {
                    childGameObject.transform.position = new Vector3((layerRect.width / 2 + layerRect.x) / _pixelsPerUnit,
                        -(layerRect.height / 2 + layerRect.y) / _pixelsPerUnit, 0);
                    renderer = childGameObject.AddComponent<SpriteRenderer>();
                    renderer.sortingOrder = lastSortingOrder;
                }
                else {
                    var sp = (objName + "_").Split('_');
                    if (sp[0] == "Text") {
                        hasImage = false;
                        var tmp = childGameObject.AddComponent<TextMeshProUGUI>();
                        tmp.text = sp[1];
                        tmp.color = GetMajorityColor(texture);
                        tmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
                        tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
                        tmp.enableAutoSizing = true;
                        SetRectTransform(tmp.rectTransform, layerRect);
                    }
                    else if (sp[0] == "Button") {
                        childGameObject.AddComponent<Button>();
                    }

                    if (hasImage) {
                        image = childGameObject.AddComponent<Image>();
                        SetRectTransform(image.rectTransform, layerRect);
                    }
                }

                Sprite childSprite = null;
                if (image || renderer) {
                    childSprite = TextureUtils.SavePngAsset(texture, _outputFolder + childLayer.Name + ".png", _pixelsPerUnit);
                    if (_outputType == TextureUtils.OutputObjectType.SPRITE_RENDERER) {
                        renderer.sprite = childSprite;
                    }
                    else {
                        image.sprite = childSprite;
                    }
                }

                if (_addPsComponents && hasImage) {
                    PsLayer layerComp = null;
                    if (_outputType == TextureUtils.OutputObjectType.SPRITE_RENDERER) {
                        layerComp = childGameObject.AddComponent<PsLayerSprite>();
                    }
                    else {
                        layerComp = childGameObject.AddComponent<PsLayerImage>();
                    }

                    layerComp.Initialize((PsdBlendModeType) childLayer.BlendModeKey, childLayer.Alpha, childLayer.Visible, childLayer.VisibleInHierarchy,
                        _blendingShader);
                }

                var tuple = new Tuple<string, GameObject, Sprite, Rect>(childLayer.Id.ToString("D4") + "_" + objName, childGameObject, childSprite, layerRect);
                objectsAndTexturesList.Add(tuple);
            }
        }

        public Color GetMajorityColor(Texture2D texture) {
            var pixels = texture.GetPixels();
            var pList = new Dictionary<Color, int>();
            foreach (var color in pixels) {
                if (color.a == 0) continue;
                if (!pList.ContainsKey(color)) pList.Add(color, 1);
                else {
                    pList[color]++;
                }
            }

            var dictionary = pList.OrderByDescending(v => v.Value).ToDictionary(v => v.Key, v => v.Value);

            return dictionary.First().Key;
        }

        public void SetRectTransform(RectTransform rectTransform, Rect layerRect) {
            rectTransform.localScale = Vector3.one;
            var groupLayer = layerRect.width == 0 || layerRect.height == 0;
            var rect = groupLayer ? parentRectTransform.rect : layerRect;
            rectTransform.anchoredPosition = new Vector3(rect.x + rect.width / 2, -(rect.y + rect.height / 2), 0f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rect.width);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rect.height);
            if (!groupLayer) rectTransform.anchoredPosition += new Vector2(-parentRectTransform.rect.width / 2, parentRectTransform.rect.height / 2);
        }
    }
}

#endif