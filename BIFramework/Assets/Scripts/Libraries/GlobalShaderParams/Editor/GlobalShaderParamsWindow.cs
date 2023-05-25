using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

public class GlobalShaderParamsWindow : OdinMenuEditorWindow
{
    [MenuItem("Window/GlobalShaderParams Window")]
    static void Init()
    {
        var window = GetWindow<GlobalShaderParamsWindow>();
        window.titleContent = new GUIContent("GlobalShaderParams");
        window.Show();
    }


    protected override OdinMenuTree BuildMenuTree() {
        var tree = new OdinMenuTree(false);
        var guids = AssetDatabase.FindAssets("t:GlobalShaderParams", new[] { "Assets/_DynamicAssets/SO/ShaderParams" });
        
        foreach (var guid in guids) {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var treeName = Path.GetFileNameWithoutExtension(path);
            tree.Add(treeName, AssetDatabase.LoadAssetAtPath<GlobalShaderParams>(path));
        }
        tree.EnumerateTree().AddThumbnailIcons();
        tree.Config.DrawSearchToolbar = true;
        return tree;
    }

    public void OnFocus() {
        ForceMenuTreeRebuild();
    }
}
