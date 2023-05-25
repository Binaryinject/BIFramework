/*
 * MIT License
 *
 * Copyright (c) 2018 Clark Yang
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of
 * this software and associated documentation files (the "Software"), to deal in
 * the Software without restriction, including without limitation the rights to
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.Collections;
using System.Text;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using BIFramework.Asynchronous;
using BIFramework.Execution;
using BIFramework.Views.Variables;
using XLua;
using Object = System.Object;

namespace BIFramework.Views {
    [LuaCallCSharp]
    public class LuaBehaviour : MonoBehaviour, ILuaExtendable {
        public ScriptReference script;
        public VariableArray variables;

        protected LuaTable scriptEnv;
        protected LuaTable metatable;
        protected Action<MonoBehaviour> onAwake;
        protected Action<MonoBehaviour> onEnable;
        protected Action<MonoBehaviour> onDisable;
        protected Func<MonoBehaviour, ILuaTask> onStart;
        protected Action<MonoBehaviour> onUpdate;
        protected Action<MonoBehaviour> onFixedUpdate;
        protected Action<MonoBehaviour> onDestroy;
        protected Action<MonoBehaviour, Collision> onCollisionEnter;
        protected Action<MonoBehaviour, Collision> onCollisionStay;
        protected Action<MonoBehaviour, Collision> onCollisionExit;
        protected Action<MonoBehaviour, Collider> onTriggerEnter;
        protected Action<MonoBehaviour, Collider> onTriggerStay;
        protected Action<MonoBehaviour, Collider> onTriggerExit;
        protected Action<MonoBehaviour> onAnimatorMove;
        protected Action<MonoBehaviour, string> onAnimatorEvent;

        public UniTask endOfStart = new();
        private readonly UniTaskCompletionSource taskSource = new();

        public virtual LuaTable GetMetatable() {
            return metatable;
        }

        protected virtual void Initialize() {
            var luaEnv = LuaEnvironment.LuaEnv;
            scriptEnv = luaEnv.NewTable();

            LuaTable meta = luaEnv.NewTable();
            meta.Set("__index", luaEnv.Global);
            scriptEnv.SetMetaTable(meta);
            meta.Dispose();

            scriptEnv.Set("target", this);
            if (string.IsNullOrEmpty(script.Filename)) {
                Debug.LogError("lua script is empty!");
            }

            string scriptPath = LuaEnvironment.GetLuaFilePath(script.Filename);
            string scriptText = Encoding.UTF8.GetString(LuaEnvironment.GetLuaBytes(scriptPath));
#if UNITY_EDITOR
            scriptPath = Application.dataPath.TrimEnd("Assets".ToCharArray()) + scriptPath;
#endif
            var result = luaEnv.DoString(scriptText, scriptPath, scriptEnv);

            if (result.Length != 1 || !(result[0] is LuaTable))
                throw new Exception("");

            metatable = (LuaTable) result[0];
            if (variables?.Variables != null) {
                foreach (var variable in variables.Variables) {
                    var name = variable.Name.Trim();
                    if (string.IsNullOrEmpty(name))
                        continue;

                    metatable.Set(name, variable.GetValue());
                }
            }

            onAwake = metatable.Get<Action<MonoBehaviour>>("Awake");
            onEnable = metatable.Get<Action<MonoBehaviour>>("OnEnable");
            onDisable = metatable.Get<Action<MonoBehaviour>>("OnDisable");
            onStart = metatable.Get<Func<MonoBehaviour, ILuaTask>>("Start");
            onUpdate = metatable.Get<Action<MonoBehaviour>>("Update");
            onFixedUpdate = metatable.Get<Action<MonoBehaviour>>("FixedUpdate");
            onDestroy = metatable.Get<Action<MonoBehaviour>>("OnDestroy");
            onCollisionEnter = metatable.Get<Action<MonoBehaviour, Collision>>("OnCollisionEnter");
            onCollisionStay = metatable.Get<Action<MonoBehaviour, Collision>>("OnCollisionStay");
            onCollisionExit = metatable.Get<Action<MonoBehaviour, Collision>>("OnCollisionExit");
            onTriggerEnter = metatable.Get<Action<MonoBehaviour, Collider>>("OnTriggerEnter");
            onTriggerStay = metatable.Get<Action<MonoBehaviour, Collider>>("OnTriggerStay");
            onTriggerExit = metatable.Get<Action<MonoBehaviour, Collider>>("OnTriggerExit");
            onAnimatorMove = metatable.Get<Action<MonoBehaviour>>("OnAnimatorMove");
            onAnimatorEvent = metatable.Get<Action<MonoBehaviour, string>>("OnAnimatorEvent");
            endOfStart = taskSource.Task;
        }

        protected virtual void Awake() {
            Initialize();
            onAwake?.Invoke(this);
        }

        protected virtual void OnEnable() {
            onEnable?.Invoke(this);
        }

        protected virtual void OnDisable() {
            onDisable?.Invoke(this);
        }

        async UniTaskVoid Start() {
            var task = onStart?.Invoke(this);
            if (task != null) 
                await task;
            taskSource?.TrySetResult();
        }

        protected virtual void Update() {
            onUpdate?.Invoke(this);
        }

        protected virtual void FixedUpdate() {
            onFixedUpdate?.Invoke(this);
        }

        protected virtual void OnDestroy() {
            onDestroy?.Invoke(this);
            onDestroy = null;
            onUpdate = null;
            onStart = null;
            onEnable = null;
            onDisable = null;
            onAwake = null;
            onCollisionEnter = null;
            onCollisionStay = null;
            onCollisionExit = null;
            onTriggerEnter = null;
            onTriggerStay = null;
            onTriggerExit = null;
            onAnimatorMove = null;
            onAnimatorEvent = null;
            //注销lua的所有代理事件
            LuaEnvironment.LuaEnv.Global.Get<LuaFunction>("DisposeAllListeners").Call(metatable);
            if (metatable != null) {
                metatable.Dispose();
                metatable = null;
            }

            if (scriptEnv != null) {
                scriptEnv.Dispose();
                scriptEnv = null;
            }
        }

        protected virtual void OnCollisionEnter(Collision other) {
            //Debug.Log(other.collider.name);
            onCollisionEnter?.Invoke(this, other);
        }

        protected virtual void OnCollisionStay(Collision other) {
            //Debug.Log(other.collider.name);
            onCollisionStay?.Invoke(this, other);
        }

        protected virtual void OnCollisionExit(Collision other) {
            onCollisionExit?.Invoke(this, other);
        }

        protected virtual void OnTriggerEnter(Collider other) {
            onTriggerEnter?.Invoke(this, other);
        }

        protected virtual void OnTriggerStay(Collider other) {
            onTriggerStay?.Invoke(this, other);
        }

        protected virtual void OnTriggerExit(Collider other) {
            onTriggerExit?.Invoke(this, other);
        }

        protected virtual void OnAnimatorMove() {
            onAnimatorMove?.Invoke(this);
        }

        public virtual void OnAnimatorEvent(string name) {
            onAnimatorEvent?.Invoke(this, name);
        }
    }
}