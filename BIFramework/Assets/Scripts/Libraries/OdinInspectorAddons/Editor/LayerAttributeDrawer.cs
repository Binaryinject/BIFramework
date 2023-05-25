using System;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace BIFramework.OdinInspectorAddons {
    public class LayerAttributeDrawer : OdinValueDrawer<int> {

        protected override void DrawPropertyLayout(GUIContent label) {
            var hasLayerAtt = false;
            foreach (var attribute in Property.Attributes) {
                if (attribute.GetType() == typeof(LayerAttribute)) {
                    hasLayerAtt = true;
                    break;
                }
            }

            if (hasLayerAtt) {
                DrawPropertyForInt(EditorGUILayout.GetControlRect(), ValueEntry, label, GetLayers());
            }
            else CallNextDrawer(label);
        }

        private string[] GetLayers()
        {
            return UnityEditorInternal.InternalEditorUtility.layers;
        }
        
        private static void DrawPropertyForInt(Rect rect, IPropertyValueEntry<int> property, GUIContent label, string[] layers)
        {
            int index = 0;
            string layerName = LayerMask.LayerToName(property.SmartValue);
            for (int i = 0; i < layers.Length; i++)
            {
                if (layerName.Equals(layers[i], StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
            
            int newIndex = EditorGUI.Popup(rect, label.text, index, layers);
            string newLayerName = layers[newIndex];
            int newLayerNumber = LayerMask.NameToLayer(newLayerName);

            if (property.SmartValue != newLayerNumber)
            {
                property.SmartValue = newLayerNumber;
            }
        }

        private static int IndexOf(string[] layers, string layer)
        {
            var index = Array.IndexOf(layers, layer);
            return Mathf.Clamp(index, 0, layers.Length - 1);
        }
    }
}