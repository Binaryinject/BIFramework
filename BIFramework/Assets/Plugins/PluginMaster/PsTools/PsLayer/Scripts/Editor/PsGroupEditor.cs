/*
Copyright (c) 2020 Omar Duarte
Unauthorized copying of this file, via any medium is strictly prohibited.
Writen and Modified by Omar Duarte, May 2020.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINARE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PluginMaster
{
    [CustomEditor(typeof(PsGroup), true)][CanEditMultipleObjects]
    public class PsGroupEditor : Editor
    {
        private SerializedProperty _visibleProp = null;
        private SerializedProperty _opacityProp = null;
        private SerializedProperty _shaderProp = null;
        private SerializedProperty _modeProp = null;
        protected virtual void OnEnable()
        {
            _visibleProp = serializedObject.FindProperty("_visible");
            _opacityProp = serializedObject.FindProperty("_opacity");
            _shaderProp = serializedObject.FindProperty("_blendingShader");
            _modeProp = serializedObject.FindProperty("_blendModeType");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var targetComponent = target as PsGroup;
            var targetComponents = targets.Select(obj => obj as PsGroup).ToArray();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Is Visible: ", GUILayout.Width(80));
            var visible = targetComponent.Visible;
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = _visibleProp.hasMultipleDifferentValues;
            visible = EditorGUILayout.Toggle(visible);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(targets, "Change Visibility");
                foreach (var comp in targetComponents) comp.Visible = visible;
                _visibleProp.boolValue = visible;
                EditorUtility.SetDirty(target);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Opacity: ", GUILayout.Width(80));
            
            var opacity = targetComponent.Opacity;
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = _opacityProp.hasMultipleDifferentValues;
            opacity = EditorGUILayout.Slider(opacity * 100f, 0f, 100f) / 100f;
            if (EditorGUI.EndChangeCheck())
            {   
                Undo.RegisterCompleteObjectUndo(targets, "Change Opacity");
                foreach (var comp in targetComponents) comp.Opacity = opacity;
                _opacityProp.floatValue = opacity;
                EditorUtility.SetDirty(target);
            }
            
            EditorGUILayout.EndHorizontal();
            var prevBlendMode = targetComponent.BlendModeType;
            if (target is PsLayer)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Shader: ", GUILayout.Width(80));
                var shader = targetComponent.BlendingShader;
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = _shaderProp.hasMultipleDifferentValues;
                shader = (PsGroup.BlendingShaderType)EditorGUILayout.Popup((int)shader,
                    new string[] { PsGroup.GetShaderTypeName(PsGroup.BlendingShaderType.DEFAULT),
                                PsGroup.GetShaderTypeName(PsGroup.BlendingShaderType.FAST),
                                PsGroup.GetShaderTypeName(PsGroup.BlendingShaderType.GRAB_PASS) });
                if (EditorGUI.EndChangeCheck())
                {
                    var showWarning = false;
                    var objectsToUndo = new List<UnityEngine.Object>(targets);
                    foreach (var comp in targetComponents)
                    {
                        if (comp is PsLayerImage
                            && shader == PsGroup.BlendingShaderType.FAST
                            && comp.thereAreUiLayersInChildren
                            && comp.canvas != null
                            && comp.canvas.renderMode != RenderMode.ScreenSpaceCamera)
                        {
                            showWarning = true;
                            if (!objectsToUndo.Contains(comp.canvas))
                            {
                                objectsToUndo.Add(comp.canvas);
                            }
                        }
                    }
                    Undo.RegisterCompleteObjectUndo(objectsToUndo.ToArray(), "Change BlendingShader");
                    foreach (var comp in targetComponents) comp.BlendingShader = shader;

                    if (showWarning)
                    {
                        Debug.LogWarning("The Fast shader only works with: Screen Space - Camera. Remember to set the render camera in the canvas component.");
                    }
                    _shaderProp.enumValueIndex = (int)shader;
                    
                    EditorUtility.SetDirty(target);
                    
                }
                
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Blend Mode: ", GUILayout.Width(80));
            string[] blendModeNames = null;
            if (targetComponent is PsLayer)
            {
                if (targetComponent.BlendingShader == PsGroup.BlendingShaderType.DEFAULT)
                {
                    blendModeNames = new string[] { "Pass Through", "Normal" };
                }
                else
                {
                    blendModeNames = targetComponent.BlendingShader == PsGroup.BlendingShaderType.GRAB_PASS ? PsdBlendModeType.GrabPassBlendModeNames : PsdBlendModeType.FastBlendModeNames;
                }
            }
            else
            {
                blendModeNames = PsdBlendModeType.GroupBlendModeNames;
            }
            
            var blendMode = targetComponent.BlendModeType;
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = _modeProp.hasMultipleDifferentValues;
            if (targetComponent.BlendingShader == PsGroup.BlendingShaderType.GRAB_PASS || !(targetComponent is PsLayer))
            {
                blendMode = (PsdBlendModeType.BlendModeType)EditorGUILayout.Popup((int)targetComponent.BlendModeType, blendModeNames);
            }
            else
            {
                var fastValue = (PsdBlendModeType.FastBlendModeType) EditorGUILayout.Popup((int)PsdBlendModeType.GetFastBlendModeType(targetComponent.BlendModeType), blendModeNames);
                blendMode = PsdBlendModeType.GetGroupBlendModeType(fastValue);
            }

            if(EditorGUI.EndChangeCheck() || prevBlendMode != blendMode)
            {
                Undo.RegisterCompleteObjectUndo(targets, "Change Blend Mode");
                foreach (var comp in targetComponents) comp.BlendModeType = blendMode;
                _modeProp.enumValueIndex = (int)blendMode;
                EditorUtility.SetDirty(target);
            }
            EditorGUILayout.EndHorizontal();
        }

        [MenuItem("GameObject/2D Object/Ps Layer - Sprite", false, int.MaxValue)]
        static void CreatePsLayerSprite(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("PsLayer Sprite");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<PsLayerSprite>();
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/UI/Ps Layer - Image", false, int.MaxValue)]
        static void CreatePsLayerUI(MenuCommand menuCommand)
        {
            var parentGo = menuCommand.context as GameObject;
            Canvas canvas = null;
            GameObject objectToUndo = null;
            if(parentGo != null)
            {
                canvas = parentGo.transform.GetComponentInParent<Canvas>();
            }
            else
            {
                objectToUndo = parentGo = new GameObject("Canvas");
            }
            if(canvas == null)
            {
                canvas = parentGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                parentGo.AddComponent<CanvasScaler>();
                parentGo.AddComponent<GraphicRaycaster>();
            }
            GameObject go = new GameObject("PsLayer Image");
            if(objectToUndo == null)
            {
                objectToUndo = go;
            }
            Undo.RegisterCreatedObjectUndo(objectToUndo, "Create " + objectToUndo.name);
            GameObjectUtility.SetParentAndAlign(go, parentGo);
            go.AddComponent<Image>();
            go.AddComponent<PsLayerImage>();
            Selection.activeObject = go;
        }
    }
}
