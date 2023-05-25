using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using BIFramework;
using BIFramework.Execution;
using XLua;

public class Behavior3 {
    [MenuItem("XLua/Generate Behavior Node")]
    public static void GenerateNode() {
        if (Application.isPlaying && LuaEnvironment.isReady) {
            var scriptPath = LuaEnvironment.GetLuaFilePath("Common.behavior3.export_node");
            var scriptText = Encoding.UTF8.GetString(LuaEnvironment.GetLuaBytes(scriptPath));
            LuaEnvironment.luaEnv.DoString(scriptText, scriptPath, LuaEnvironment.luaEnv.Global);
        }
        else {
            Debug.LogError("请在Playing模式下xLua环境准备好后运行！");
        }
    }
}