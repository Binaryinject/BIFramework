using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using BIFramework.Asynchronous;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.AddressableAssets;
using BIFramework.Execution;
using XLua;

namespace BIFramework {
    [LuaCallCSharp]
    public static class LuaEnvironment {
        public static LuaEnv luaEnv;
        public static bool isReady = false;
        public static HashSet<string> luaCodes = new();
        public const string LuaCodeFolder = "LuaCode";
        private static CancellationTokenSource tokenSource;

        public static LuaEnv LuaEnv {
            get { return luaEnv; }
        }

        /// <summary>
        /// 初始化lua虚拟机
        /// </summary>
        public static async UniTask Initialize() {
            isReady = true;
            Executors.Create();
            luaEnv = new LuaEnv();
            if (tokenSource != null) {
                tokenSource.Cancel();
                tokenSource.Dispose();
                tokenSource = null;
            }

            luaCodes = new HashSet<string>();
            await Addressables.InitializeAsync();
            var catalogs = await Addressables.CheckForCatalogUpdates();
            if (catalogs.Count > 0) {
                Debug.Log($"CheckForCatalogUpdates: {catalogs.Count} catalog need update.");
                await Addressables.UpdateCatalogs(catalogs);
                Debug.Log("UpdateCatalogs complete.");
            }

            ///下载依赖和更新
            await DownloadDependencies();
            await CreateInstanceSO();
            await PreloadLuaCode();
            await LuaEnv.Global.Get<Func<string, ILuaTask>>("require").Invoke("Config.Defines");
            tokenSource = new CancellationTokenSource();
            UniTask.RunOnThreadPool(DOTick, true, tokenSource.Token).Forget();
        }

        //先初始化所有SO配置
        public static async UniTask CreateInstanceSO() {
            await UniTask.WhenAll(GlobalSO.CreateInstance());
        }

        private static readonly string[] labels = {
            "LuaCode", "Config", "Default", "ShaderParams",
        };
        public static async UniTask DownloadDependencies() {
            var sizeTasks = new List<UniTask<long>>();
            foreach (var label in labels) {
                sizeTasks.Add(Addressables.GetDownloadSizeAsync(label).ToUniTask());
            }

            var sizes = await UniTask.WhenAll(sizeTasks);
            var downloadTasks = new List<UniTask>();
            var downloadSize = 0L;
            var builder = new StringBuilder($"GetDownloadSizeAsync: {Environment.NewLine}");
            for (var i = 0; i < sizes.Length; i++) {
                var size = sizes[i];
                var label = labels[i];
                if (size > 0) {
                    builder.Append($"[{label}] {Util.ByteConversionGBMBKB(size)} dependencies need download.{Environment.NewLine}");
                    //Addressables.ClearDependencyCacheAsync(nowKey);
                    downloadSize += size;
                    downloadTasks.Add(Addressables.DownloadDependenciesAsync(label, true).ToUniTask());
                }
            }

            if (downloadTasks.Count > 0) {
                Debug.Log(builder);
                await UniTask.WhenAll(downloadTasks);
                Debug.Log($"DownloadDependenciesAsync: {Util.ByteConversionGBMBKB(downloadSize)} download completes.");
            }
        }

        public static async UniTask PreloadLuaCode() {
            var handle = Addressables.LoadResourceLocationsAsync("LuaCode");
            var locations = await handle;
            //var codes = await Addressables.LoadAssetsAsync<TextAsset>(locations, null);

            foreach (var t in locations) {
                var res = t.PrimaryKey;
                //排除protobuf描述文件
                if (!res.Contains("proto.bytes")) luaCodes.Add(res);
            }

            //Addressables.Release(codes);
            Addressables.Release(handle);

            //注册共享访问
            LuaArrAccessAPI.RegisterPinFunc(luaEnv.L);
            luaEnv.AddBuildin("pb", XLua.LuaDLL.Lua.LoadLuaProfobuf);
            luaEnv.AddBuildin("conv", XLua.LuaDLL.Lua.LoadLuaProfoConv);
            luaEnv.AddBuildin("lpeg", XLua.LuaDLL.Lua.LoadLpeg);
            luaEnv.AddBuildin("rapidjson", XLua.LuaDLL.Lua.LoadRapidJson);
            luaEnv.AddBuildin("ffi", XLua.LuaDLL.Lua.LoadFFI);
            luaEnv.AddLoader(CustomLoader);
#if UNITY_EDITOR
            if (EditorPrefs.GetBool("emmy.service.debug")) {
                Debug.Log("<color=yellow>Emmylua debug tcp connected...</color>");
                var dbg = EditorPrefs.GetString("emmy.debug.text");
                if (!string.IsNullOrEmpty(dbg)) {
                    luaEnv.DoString($@"pcall(function () {dbg} end)");
                }
            }
#endif
        }

        public static string GetLuaFilePath(string file) {
            var scriptPath = new StringBuilder();
            scriptPath.Append(file.Replace(".", "/")).Append(".lua.txt");
            return $"Assets/{LuaCodeFolder}/{scriptPath}";
        }

        public static byte[] GetLuaBytes(string filepath) {
            if (!luaCodes.Contains(filepath)) {
                Debug.LogError($"lua script is not find! <color=red>[{filepath}]</color>");
                return null;
            }

            //这里同步方法调用，避免preload内存太大
            var code = Addressables.LoadAssetAsync<TextAsset>(filepath).WaitForCompletion();
            var bytes = new byte[code.bytes.Length];
            Array.Copy(code.bytes, bytes, code.bytes.Length);
            Addressables.Release(code);
            return bytes;
        }

        public static byte[] CustomLoader(ref string filepath) {
            byte[] bytes = null;
            if (!filepath.Contains("emmy_core")) bytes = GetLuaBytes(GetLuaFilePath(filepath));
#if UNITY_EDITOR
            filepath = Application.dataPath.TrimEnd("Assets".ToCharArray()) + GetLuaFilePath(filepath);
#endif
            return bytes;
        }

        public static void Dispose() {
            luaCodes = null;
            Addressables.ClearResourceLocators();

            tokenSource?.Cancel();

            if (luaEnv != null) {
                luaEnv.Dispose();
                luaEnv = null;
            }
        }

        private static async UniTask DOTick() {
            await UniTask.SwitchToThreadPool();
            while (true) {
                tokenSource.Token.ThrowIfCancellationRequested();
                await UniTask.Delay(TimeSpan.FromSeconds(2), DelayType.Realtime);
                try {
                    luaEnv.Tick();
                }
                catch (Exception e) {
                    Debug.LogWarning($"LuaEnv.Tick Error:{e}");
                }
            }
        }
    }
}