using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

public class LuaCodeTemplate {
    [MenuItem("Assets/Create/Lua Script", false, 80)]
    public static void CreatNewLua() {
        var guid = AssetDatabase.FindAssets("t:Script LuaCodeTemplate");
        var path = AssetDatabase.GUIDToAssetPath(guid[0]);
        path = path.Substring(path.IndexOf("Assets"));
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, ScriptableObject.CreateInstance<MyDoCreateScriptAsset>(),
            GetSelectedPathOrFallback() + "/LuaClass.txt", null, new FileInfo(path).Directory.FullName + "/Template/lua.txt");
    }

    public static string GetSelectedPathOrFallback() {
        string path = "Assets";
        foreach (UnityEngine.Object obj in Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets)) {
            path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                path = Path.GetDirectoryName(path);
                break;
            }
        }

        return path;
    }
}

class MyDoCreateScriptAsset : EndNameEditAction {
    public override void Action(int instanceId, string pathName, string resourceFile) {
        ProjectWindowUtil.ShowCreatedAsset(CreateScriptAssetFromTemplate(pathName, resourceFile));
    }

    internal static UnityEngine.Object CreateScriptAssetFromTemplate(string relative, string resourceFile) {
        var fullPath = Path.GetFullPath(relative);
        var streamReader = new StreamReader(resourceFile);
        var text = streamReader.ReadToEnd();
        streamReader.Close();
        
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(relative);
        text = Regex.Replace(text, "#NAME#", fileNameWithoutExtension);
        text = Regex.Replace(text, "#AUTHOR#", Environment.UserName);
        text = Regex.Replace(text, "#TIME#", DateTime.Now.ToString("yyyy/MM/dd hh:mm:ss"));
        
        var fileInfo = new FileInfo(fullPath);
        var fileName = fileNameWithoutExtension + ".lua.txt";
        fullPath = $"{fileInfo.Directory.FullName}\\{fileName}";
        relative = $"Assets{fullPath.Substring(Application.dataPath.Length)}";
        File.WriteAllText(fullPath, text, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(relative);
        return AssetDatabase.LoadAssetAtPath(relative, typeof(UnityEngine.Object));
    }
}