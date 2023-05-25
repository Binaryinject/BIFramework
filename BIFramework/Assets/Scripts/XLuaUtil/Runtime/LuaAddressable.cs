using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using BIFramework.Asynchronous;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using TMPro;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using XLua;
using Object = System.Object;

namespace BIFramework {
    [LuaCallCSharp]
    public static class LuaAddressable {
        /// <summary>
        /// addressable所有资源类型加载
        /// </summary>
        /// <param name="type"></param>
        /// <param name="key"></param>
        public static Object LoadAssetAsync(Type type, string key) {
            if (type == typeof(TextAsset)) return Addressables.LoadAssetAsync<TextAsset>(key).ToUniTask();
            if (type == typeof(GameObject)) return Addressables.LoadAssetAsync<GameObject>(key).ToUniTask();
            if (type == typeof(Texture)) return Addressables.LoadAssetAsync<Texture>(key).ToUniTask();
            if (type == typeof(Sprite)) return Addressables.LoadAssetAsync<Sprite>(key).ToUniTask();
            if (type == typeof(Material)) return Addressables.LoadAssetAsync<Material>(key).ToUniTask();
            if (type == typeof(AudioClip)) return Addressables.LoadAssetAsync<AudioClip>(key).ToUniTask();
            if (type == typeof(SpriteAtlas)) return Addressables.LoadAssetAsync<SpriteAtlas>(key).ToUniTask();
            if (type == typeof(TMP_SpriteAsset)) return Addressables.LoadAssetAsync<TMP_SpriteAsset>(key).ToUniTask();
            Debug.LogError($"LoadAssetAsync has unknown type error: {type}");
            return null;
        }

        public static Object LoadAssetsAsync(Type type, IList<IResourceLocation> locations) {
            if (type == typeof(TextAsset)) return Addressables.LoadAssetsAsync<TextAsset>(locations, null).ToUniTask();
            if (type == typeof(GameObject)) return Addressables.LoadAssetsAsync<GameObject>(locations, null).ToUniTask();
            if (type == typeof(Texture)) return Addressables.LoadAssetsAsync<Texture>(locations, null).ToUniTask();
            if (type == typeof(Sprite)) return Addressables.LoadAssetsAsync<Sprite>(locations, null).ToUniTask();
            if (type == typeof(Material)) return Addressables.LoadAssetsAsync<Material>(locations, null).ToUniTask();
            if (type == typeof(AudioClip)) return Addressables.LoadAssetsAsync<AudioClip>(locations, null).ToUniTask();
            if (type == typeof(SpriteAtlas)) return Addressables.LoadAssetsAsync<SpriteAtlas>(locations, null).ToUniTask();
            if (type == typeof(TMP_SpriteAsset)) return Addressables.LoadAssetsAsync<TMP_SpriteAsset>(locations, null).ToUniTask();
            Debug.LogError($"LoadAssetsAsync has unknown type error: {type}");
            return null;
        }

        public static UniTask<GameObject> InstantiateAsync(string key, Transform parent) {
            return Addressables.InstantiateAsync(key, parent).ToUniTask();
        }

        public static UniTask<GameObject> InstantiateAsyncByDetails(string key, Transform parent, Vector3 position, Quaternion rotation) {
            return Addressables.InstantiateAsync(key, position, rotation, parent).ToUniTask();
        }

        public static int GetLocationsCount(IList<IResourceLocation> resourceLocations) {
            return resourceLocations.Count;
        }

        public static UniTask<IList<IResourceLocation>> GetLocationTask(AsyncOperationHandle<IList<IResourceLocation>> handle) {
            return handle.ToUniTask();
        }
        public static AsyncOperationHandle<IList<IResourceLocation>> LoadLocationsAsync(string tag) {
            return Addressables.LoadResourceLocationsAsync(tag);
        }

        public static void Release(Type type, Object handle) {
            if (type == typeof(TextAsset)) Addressables.Release((TextAsset) handle);
            else if (type == typeof(GameObject)) Addressables.Release((GameObject) handle);
            else if (type == typeof(Texture)) Addressables.Release((Texture) handle);
            else if (type == typeof(Sprite)) Addressables.Release((Sprite) handle);
            else if (type == typeof(Material)) Addressables.Release((Material) handle);
            else if (type == typeof(AudioClip)) Addressables.Release((AudioClip) handle);
            else if (type == typeof(SpriteAtlas)) Addressables.Release((SpriteAtlas) handle);
            else if (type == typeof(TMP_SpriteAsset)) Addressables.Release((TMP_SpriteAsset) handle);
            else {
                Debug.LogError($"Release has unknown type error: {type}");
            }
        }

        public static void ReleaseAssets(Type type, Object handle) {
            if (type == typeof(TextAsset)) Addressables.Release((IList<TextAsset>) handle);
            else if (type == typeof(GameObject)) Addressables.Release((IList<GameObject>) handle);
            else if (type == typeof(Texture)) Addressables.Release((IList<Texture>) handle);
            else if (type == typeof(Sprite)) Addressables.Release((IList<Sprite>) handle);
            else if (type == typeof(Material)) Addressables.Release((IList<Material>) handle);
            else if (type == typeof(AudioClip)) Addressables.Release((IList<AudioClip>) handle);
            else if (type == typeof(SpriteAtlas)) Addressables.Release((IList<SpriteAtlas>) handle);
            else if (type == typeof(TMP_SpriteAsset)) Addressables.Release((IList<TMP_SpriteAsset>) handle);
            else {
                Debug.LogError($"ReleaseAssets has unknown type error: {type}");
            }
        }

        public static bool ReleaseInstance(GameObject instance) {
            return Addressables.ReleaseInstance(instance);
        }

        public static void ReleaseLocation(AsyncOperationHandle<IList<IResourceLocation>> handle) {
            Addressables.Release(handle);
        }

        public static UniTask<SceneInstance> LoadSceneAsync(object key, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true,
            int priority = 100) {
            return Addressables.LoadSceneAsync(key, loadMode, activateOnLoad, priority).ToUniTask();
        }

        public static UniTask<SceneInstance> UnloadSceneAsync(SceneInstance handle) {
            return Addressables.UnloadSceneAsync(handle).ToUniTask();
        }
    }
}