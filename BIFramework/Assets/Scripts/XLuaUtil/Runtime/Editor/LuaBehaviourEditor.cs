using System;
using System.IO;
using System.Linq;
using Animancer.Editor;
using UnityEditor;
using UnityEngine;
using BIFramework.Views;
using BIFramework.Views.Variables;

[CustomEditor(typeof(LuaBehaviour))]
public class LuaBehaviourEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        var behaviour = target as LuaBehaviour;
        if (GUILayout.Button("Generate Variables To LuaCode", GUILayout.Height(30))) {
            if (string.IsNullOrEmpty(behaviour.script.Filename)) {
                Debug.LogError("Filename is empty");
                return;
            }

            var defineMember = Environment.NewLine;
            var classname = behaviour.script.Filename.Split('.').Last();

            foreach (var variable in behaviour.variables.Variables) {
                var variableName = variable.Name.Trim();
                if (string.IsNullOrEmpty(variableName))
                    continue;
                var variableType = variable.GetValue().GetType().ToString();

                if (variable.ValueType == typeof(int) || variable.ValueType == typeof(float)) variableType = "number";
                else if (variable.ValueType == typeof(bool)) variableType = "boolean";
                else if (variable.ValueType == typeof(string)) variableType = "string";
                else if (variable.ValueType == typeof(LuaBehaviour))
                {
                    var fileName = ((LuaBehaviour) variable.GetValue()).script.Filename.Split('.');
                    variableType = fileName.Last();
                }
                defineMember += @$"---@field private {variableName} {variableType}
";
            }

            //Debug.Log(defineMember);
            var text = ((TextAsset)behaviour.script.cachedAsset).text;
            var beginFlagText = "---======================== 面板变量 ========================";
            var endFlagText = "---=========================================================";
            var regionStart = text.IndexOf(beginFlagText) + beginFlagText.Length;
            if (regionStart < beginFlagText.Length) {
                Debug.LogError($"生成失败！没有找到标志位[{beginFlagText}]");
                return;
            }
            var regionEnd = text.IndexOf(endFlagText, regionStart, StringComparison.Ordinal);
            text = text.Remove(regionStart, regionEnd - regionStart);
            text = text.Insert(regionStart, defineMember);
            var savePath = Application.dataPath.TrimEnd("Assets".ToCharArray()) + AssetDatabase.GetAssetPath(behaviour.script.cachedAsset);
            File.WriteAllText(savePath, text);
            Debug.Log($"变量更新完成！生成数量->{behaviour.variables.Variables.Count}");
        }
    }
}