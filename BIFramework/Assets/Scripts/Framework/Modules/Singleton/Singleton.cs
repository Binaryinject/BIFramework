using System;
using UnityEngine;
using XLua;

namespace BIFramework.Singleton {
    
    [LuaCallCSharp]
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour {
        private static T _instance;
        private static object _lock = new();
        private static bool applicationIsQuitting = false;

        protected void SetInstance(T inst) {
            _instance = inst;
        }
        public static T Instance {
            get {
                if (applicationIsQuitting) return null;

                lock (_lock) {
                    if (_instance == null) {
                        _instance = (T) FindObjectOfType(typeof(T));
                        var objs = Resources.FindObjectsOfTypeAll(typeof(T));
                        if (objs.Length > 1) {
                            _instance = objs[0] as T;
                        }

                        if (_instance == null) {
                            GameObject gameObject = GameObject.Find("[Singleton]");
                            if (!gameObject) {
                                gameObject = new GameObject();
                            }

                            _instance = gameObject.AddComponent<T>();
                            gameObject.name = "[Singleton]";
                            DontDestroyOnLoad(gameObject);
                        }
                    }

                    return _instance;
                }
            }
        }

        public void OnDestroy() {
            applicationIsQuitting = true;
        }
    }
}