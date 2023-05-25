using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GlobalShaderParams))]
public class GlobalShaderParamsEditor : OdinEditor
{
    public override void OnInspectorGUI() {
        var gsp = target as GlobalShaderParams;
        EditorGUI.BeginChangeCheck();
        base.OnInspectorGUI();
        if (EditorGUI.EndChangeCheck()) {
            gsp.SetValues();
            EditorUtility.SetDirty(target);
        }
    }
}
