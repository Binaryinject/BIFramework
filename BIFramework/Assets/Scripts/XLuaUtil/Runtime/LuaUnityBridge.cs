using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Animancer;
using Cinemachine;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Pathfinding;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AddressableAssets;
using UnityEngine.AI;
using BIFramework.Views;
using UnityEngine.Networking;
using XLua;
using Object = UnityEngine.Object;
using Path = System.IO.Path;

namespace BIFramework {
    [LuaCallCSharp]
    public static class LuaUnityBridge {
        public static Vector3 SamplePosition(Vector3 pos) {
            return NavMesh.SamplePosition(pos, out var navMeshHit, float.MaxValue, -1) ? navMeshHit.position : Vector3.zero;
        }

        public static void SetBehaviorEnable(Object obj, bool enable) {
            var behavior = obj as Behaviour;
            var renderer = obj as Renderer;
            if (behavior) behavior.enabled = enable;
            if (renderer) renderer.enabled = enable;
        }

        public static void SetInputAxis() {
            CinemachineCore.GetInputAxis = name => {
                if (Camera.main.GetComponent<CinemachineBrain>().IsBlending) {
                    return 0;
                }

                return Input.GetAxis(name);
            };
        }

        public static RaycastHit PhysicsRaycast(Ray ray, float maxDist, int layer) {
            RaycastHit hitInfo;
            Physics.Raycast(ray, out hitInfo, maxDist, layer);
            return hitInfo;
        }

        public static RaycastHit PhysicsRaycastQT(Ray ray, float maxDist, int layer, QueryTriggerInteraction qt) {
            RaycastHit hitInfo;
            Physics.Raycast(ray, out hitInfo, maxDist, layer, qt);
            return hitInfo;
        }

        public static RaycastHit PhysicsRaycast(Ray ray) {
            RaycastHit hitInfo;
            Physics.Raycast(ray, out hitInfo);
            return hitInfo;
        }

        public static RaycastHit PhysicsRaycast(Vector3 origin, Vector3 direction, float maxDist, int layer) {
            RaycastHit hitInfo;
            Physics.Raycast(origin, direction, out hitInfo, maxDist, layer);
            return hitInfo;
        }

        public static RaycastHit PhysicsRaycastQT(Vector3 origin, Vector3 direction, float maxDist, int layer, QueryTriggerInteraction qt) {
            RaycastHit hitInfo;
            Physics.Raycast(origin, direction, out hitInfo, maxDist, layer, qt);
            return hitInfo;
        }

        public static RaycastHit PhysicsRaycast(Vector3 origin, Vector3 direction) {
            RaycastHit hitInfo;
            Physics.Raycast(origin, direction, out hitInfo);
            return hitInfo;
        }

        public static List<Collider> OverlapBoxNonAlloc(Vector3 position, Vector3 halfExtents, int mask) {
            var results = new Collider[8];
            Physics.OverlapBoxNonAlloc(position, halfExtents, results, Quaternion.identity, mask);
            return results.Where(t => t).ToList();
        }

        public static List<Collider> OverlapSphereNonAlloc(Vector3 position, float radius, int mask) {
            var results = new Collider[8];
            Physics.OverlapSphereNonAlloc(position, radius, results, mask);
            return results.Where(t => t).ToList();
        }

        public static bool IsOverlapByBox(Vector3 position, Vector3 halfExtents, int mask) {
            return Physics.OverlapBoxNonAlloc(position, halfExtents, new Collider[8], Quaternion.identity, mask) > 0;
        }

        public static bool IsOverlapBySphere(Vector3 position, float radius, int mask) {
            return Physics.OverlapSphereNonAlloc(position, radius, new Collider[8], mask) > 0;
        }

        public static bool IsOverlapAllByBox(Vector3 position, Vector3 halfExtents) {
            return Physics.OverlapBoxNonAlloc(position, halfExtents, new Collider[8]) > 0;
        }

        public static bool IsOverlapAllBySphere(Vector3 position, float radius) {
            return Physics.OverlapSphereNonAlloc(position, radius, new Collider[8]) > 0;
        }

        public static AnimationState GetState(this Animation ani, string clip) {
            return ani[clip];
        }

        public static LuaBehaviour GetLuaBehaviour(this GameObject gameObject, string lua) {
            if (string.IsNullOrEmpty(lua)) {
                return gameObject.GetComponent<LuaBehaviour>();
            }

            var behaviour = gameObject.GetComponents<LuaBehaviour>();
            return behaviour.FirstOrDefault(bv => bv.script.Filename == lua);
        }

        public static LuaBehaviour GetLuaBehaviour(this Transform transform, string lua) {
            if (string.IsNullOrEmpty(lua)) {
                return transform.GetComponent<LuaBehaviour>();
            }

            var behaviour = transform.GetComponents<LuaBehaviour>();
            return behaviour.FirstOrDefault(bv => bv.script.Filename == lua);
        }

        public static Tweener SetIdString(this Tweener tw, string id) {
            return tw.SetId(id);
        }

        public static Sequence SetIdString(this Sequence seq, string id) {
            return seq.SetId(id);
        }

        public static LuaTable FindVisibleTargets(this Transform tf, float viewRadius, float viewAngle, LuaTable targetMask, int obstacleMask) {
            var visibleTargets = LuaEnvironment.LuaEnv.NewTable();
            var layer = LayerMask.GetMask(targetMask.Cast<string[]>());
            var targetsInViewRadius = Physics.OverlapSphere(tf.position, viewRadius, layer);

            for (int i = 0; i < targetsInViewRadius.Length; i++) {
                var target = targetsInViewRadius[i].transform;
                var dirToTarget = (target.position - tf.position).normalized;
                if (Vector3.Angle(tf.forward, dirToTarget) < viewAngle / 2) {
                    float dstToTarget = Vector3.Distance(tf.position, target.position);

                    if (!Physics.Raycast(tf.position, dirToTarget, dstToTarget, obstacleMask)) {
                        visibleTargets.Set(i + 1, target);
                    }
                }
            }

            return visibleTargets;
        }

        public static bool IsPointerOverUI(int idx = 0) {
            return IsPointerOver("UI", idx);
        }

        public static bool IsPointerOver(string layerName, int idx = 0) {
            GameObject obj = CurrentRaycastObject(idx);
            return obj != null && obj.layer == LayerMask.NameToLayer(layerName);
        }

        public static GameObject CurrentRaycastObject(int idx) {
            GameObject obj = null;
            if (Application.isMobilePlatform) {
                if (Input.touchCount > 0) {
                    if (EventSystem.current.IsPointerOverGameObject()) {
                        PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
                        pointerEventData.position = Input.GetTouch(idx).position;
                        pointerEventData.pressPosition = Input.GetTouch(idx).position;
                        List<RaycastResult> result = new List<RaycastResult>();
                        EventSystem.current.RaycastAll(pointerEventData, result);
                        if (result.Count > 0) {
                            obj = result[0].gameObject;
                        }
                    }
                }
            }
            else {
                if (EventSystem.current.IsPointerOverGameObject()) {
                    PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
                    pointerEventData.position = Input.mousePosition;
                    List<RaycastResult> result = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointerEventData, result);
                    if (result.Count > 0) {
                        obj = result[0].gameObject;
                    }
                }
            }

            return obj;
        }

        public static object GetCinemachineComponentLua(this CinemachineVirtualCamera camera, Type type) {
            if (type == typeof(CinemachineBasicMultiChannelPerlin)) {
                return camera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
            }

            if (type == typeof(CinemachinePOV)) {
                return camera.GetCinemachineComponent<CinemachinePOV>();
            }


            return null;
        }

        public static Vector3 WorldToCanvasPosition(RectTransform rectCanvas, Vector3 worldPos) {
            Vector2 sizeDelta = rectCanvas.sizeDelta;
            Vector3 vector = Camera.main.WorldToViewportPoint(worldPos);
            Vector2 vector2 = new Vector2(vector.x - 0.5f, vector.y - 0.5f);
            Vector2 v = new Vector2(vector2.x * sizeDelta.x, vector2.y * sizeDelta.y);
            return v;
        }

        public static Vector2 ScreenToUIPosition(RectTransform rect, Vector2 screenPos, Camera uiCamera) {
            Vector2 uiPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPos, uiCamera, out uiPos);
            return uiPos;
        }

        public static byte[] StringToBytes(string str) {
            return Encoding.UTF8.GetBytes(str);
        }

        public static string BytesToString(byte[] bytes) {
            return Encoding.UTF8.GetString(bytes);
        }

        public static void SetXAxisSpeed(this CinemachineFreeLook cm, float value) {
            cm.m_XAxis.m_MaxSpeed = value;
        }

        public static void SetYAxisSpeed(this CinemachineFreeLook cm, float value) {
            cm.m_YAxis.m_MaxSpeed = value;
        }

        public static void SetXAxisName(this CinemachineFreeLook cm, string value) {
            cm.m_XAxis.m_InputAxisName = value;
        }

        public static void SetXAxisValue(this CinemachineFreeLook cm, float value) {
            cm.m_XAxis.m_InputAxisValue = value;
        }

        public static void SetYAxisName(this CinemachineFreeLook cm, string value) {
            cm.m_YAxis.m_InputAxisName = value;
        }

        public static void SetYAxisValue(this CinemachineFreeLook cm, float value) {
            cm.m_YAxis.m_InputAxisValue = value;
        }

        public static void SetHorizontalAxisName(this CinemachineVirtualCamera cm, string value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_HorizontalAxis.m_InputAxisName = value;
        }

        public static void SetHorizontalAxisValue(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_HorizontalAxis.m_InputAxisValue = value;
        }

        public static void SetHorizontalAxisSpeed(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_HorizontalAxis.m_MaxSpeed = value;
        }

        public static void SetVerticalAxisName(this CinemachineVirtualCamera cm, string value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_VerticalAxis.m_InputAxisName = value;
        }

        public static void SetVerticalAxisValue(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_VerticalAxis.m_InputAxisValue = value;
        }

        public static void SetVerticalValue(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_VerticalAxis.Value = value;
        }

        public static float GetVerticalValue(this CinemachineVirtualCamera cm) {
            return cm.GetCinemachineComponent<CinemachinePOV>().m_VerticalAxis.Value;
        }

        public static void SetHorizontalValue(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_HorizontalAxis.Value = value;
        }

        public static float GetHorizontalValue(this CinemachineVirtualCamera cm) {
            return cm.GetCinemachineComponent<CinemachinePOV>().m_HorizontalAxis.Value;
        }


        public static void SetVerticalAxisSpeed(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachinePOV>().m_VerticalAxis.m_MaxSpeed = value;
        }

        public static void SetFieldOfView(this CinemachineVirtualCamera cm, float value) {
            cm.m_Lens.FieldOfView = value;
        }

        public static float GetFieldOfView(this CinemachineVirtualCamera cm) {
            return cm.m_Lens.FieldOfView;
        }

        public static void SetDeadZoneWidth(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_DeadZoneWidth = value;
        }

        public static void SetDeadZoneHeight(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_DeadZoneHeight = value;
        }

        public static float GetDeadZoneWidth(this CinemachineVirtualCamera cm) {
            return cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_DeadZoneWidth;
        }

        public static float GetDeadZoneHeight(this CinemachineVirtualCamera cm) {
            return cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_DeadZoneHeight;
        }

        public static void SetDeadZoneDepth(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_DeadZoneDepth = value;
        }

        public static void SetSoftZoneWidth(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_SoftZoneWidth = value;
        }

        public static void SetSoftZoneHeight(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_SoftZoneHeight = value;
        }

        public static void SetCameraDistance(this CinemachineVirtualCamera cm, float value) {
            cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_CameraDistance = value;
        }

        public static void SetTrackedObjectOffset(this CinemachineVirtualCamera cm, Vector3 value) {
            cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_TrackedObjectOffset = value;
        }

        public static Vector3 GetTrackedObjectOffset(this CinemachineVirtualCamera cm) {
            return cm.GetCinemachineComponent<CinemachineFramingTransposer>().m_TrackedObjectOffset;
        }

        public static void SetAmplitudeGain(this CinemachineBasicMultiChannelPerlin cm, float value) {
            cm.m_AmplitudeGain = value;
        }

        public static void SetRotate(this CinemachineFreeLook cm, float value) {
            cm.m_XAxis.m_InputAxisValue = 0;
            cm.m_XAxis.Value = value;
        }

        public static Vector2 ScreenPointToLocalPointInRectangle(this RectTransform rt, Vector2 screenPoint, Camera camera) {
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPoint, camera, out localPoint);
            return localPoint;
        }

        public static Vector3 GetVector3(this Int3 value) {
            return (Vector3) value;
        }

        public static string GetColliderType(this GameObject go) {
            if (go.GetComponent<BoxCollider>()) {
                return "BoxCollider";
            }

            if (go.GetComponent<CapsuleCollider>()) {
                return "CapsuleCollider";
            }

            if (go.GetComponent<SphereCollider>()) {
                return "SphereCollider";
            }

            return go.GetComponent<MeshCollider>() ? "MeshCollider" : "";
        }

        public static string GetColliderType(this Transform tf) {
            if (tf.GetComponent<BoxCollider>()) {
                return "BoxCollider";
            }

            if (tf.GetComponent<CapsuleCollider>()) {
                return "CapsuleCollider";
            }

            if (tf.GetComponent<SphereCollider>()) {
                return "SphereCollider";
            }

            return tf.GetComponent<MeshCollider>() ? "MeshCollider" : "";
        }

        public static bool IsNull(UnityEngine.Object uObj) {
            return uObj == null;
        }

        public static string GetDirectoryName(string path) {
            return Path.GetDirectoryName(path);
        }

        public static string GetFileNameWithoutExtension(string path) {
            return Path.GetFileNameWithoutExtension(path);
        }

        public static (bool, int) TryGetTupleValue(this Dictionary<string, int> dictionary, string key) {
            return dictionary.TryGetValue(key, out var value) ? (true, value) : (false, 0);
        }

        public static async UniTask<UnityWebRequest> SendWebRequestTask(this UnityWebRequest request) {
            return await request.SendWebRequest().ToUniTask();
        }

        public static UniTaskCompletionSource GetCompletionSource() {
            return new UniTaskCompletionSource();
        }

        public static UniTaskCompletionSource<LuaTable> GetCompletionSourceTable() {
            return new UniTaskCompletionSource<LuaTable>();
        }

        public static UniTaskCompletionSource<LuaFunction> GetCompletionSourceFunction() {
            return new UniTaskCompletionSource<LuaFunction>();
        }

        public static UniTaskCompletionSource<int> GetCompletionSourceInt() {
            return new UniTaskCompletionSource<int>();
        }

        public static UniTaskCompletionSource<float> GetCompletionSourceFloat() {
            return new UniTaskCompletionSource<float>();
        }

        public static UniTaskCompletionSource<bool> GetCompletionSourceBool() {
            return new UniTaskCompletionSource<bool>();
        }
    }
}