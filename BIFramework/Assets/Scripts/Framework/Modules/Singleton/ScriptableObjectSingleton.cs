using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using XLua;

namespace BIFramework.Singleton {
    [LuaCallCSharp]
    public class ScriptableObjectSingleton<T> : ScriptableObject where T : ScriptableObject {
        private static T _instance;

        public static T Instance {
            get {
                return _instance;
            }
        }

        public static async UniTask CreateInstance() {
            if (_instance == null) {
                var key = $"Assets/_DynamicAssets/SO/{typeof(T)}.asset";
                if (Util.AddressableResourceExists(key, typeof(T))) {
                    var result = await Addressables.LoadAssetAsync<T>(key);
                    _instance = Instantiate(result);
                    Addressables.Release(result);
                }
            }

            if (_instance == null) {
                _instance = CreateInstance<T>();
            }
        }
    }
}