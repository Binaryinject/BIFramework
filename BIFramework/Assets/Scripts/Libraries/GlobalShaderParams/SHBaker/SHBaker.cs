using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class SHBaker : MonoBehaviour {
    private static SHBaker _instance = null;
    public static SHBaker Instance {
        get {
            if (_instance == null) {
                var bakerGo = GameObject.Find("SHBaker");
                if (bakerGo && bakerGo.TryGetComponent<SHBaker>(out var baker)) {
                    _instance = baker;
                }
            }
            return _instance;
        }
    }

    private ReflectionProbe _rp = null;
    private static readonly int CustomSH = Shader.PropertyToID("custom_SH");

    void OnDestroy() {
        _instance = null;
    }

    private ReflectionProbe rp {
        get {
            if (_rp == null) {
                _rp = GetComponent<ReflectionProbe>();
                _rp.mode = ReflectionProbeMode.Realtime;
                _rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            }

            return _rp;
        }
    }
    
    public async UniTask<SphericalHarmonicsL2> BakeSHRuntime(Vector3 center, Cubemap cubemap = null) {
        Cubemap cubemapSrc;
        if (cubemap == null) {
            transform.position = center;
            var renderID = rp.RenderProbe();
            await UniTask.WaitUntil(() => rp.IsFinishedRendering(renderID));
            cubemapSrc = SphericalHarmonics.RenderTextureToCubemap(rp.realtimeTexture);
        }
        else cubemapSrc = cubemap;

        var stopWatch = new Stopwatch();
        stopWatch.Start();
        var sh = new SphericalHarmonicsL2();
        int size = cubemapSrc.width;
        for (int i = 0; i < 6; i++) {
            var srcColors = cubemapSrc.GetPixels((CubemapFace) i);
            for (int u = 0; u < size; u++) {
                for (int v = 0; v < size; v++) {
                    var dir = SphericalHarmonics.DirectionFromCubemapTexel(i, (float) u / size, (float) v / size);
                    float d_omega = SphericalHarmonics.DifferentialSolidAngle(size, u * 1.0f / size, v * 1.0f / size);
                    sh.AddDirectionalLight(dir.normalized, srcColors[v * size + u], d_omega / Mathf.PI / 2);
                }
            }
        }

        stopWatch.Stop();
        Debug.Log($"sh compute time: {stopWatch.ElapsedMilliseconds / 1000f}");
        return sh;
    }

    [Button]
    public void GetSHParams() {
        // var names = new List<string> {
        //     "unity_SHAr",
        //     "unity_SHAg",
        //     "unity_SHAb",
        //     "unity_SHBr",
        //     "unity_SHBg",
        //     "unity_SHBb",
        //     "unity_SHC"
        // };
        // var values = names.Select(Shader.GetGlobalVector).ToList();
        var values = Shader.GetGlobalVectorArray(CustomSH);

        var shBuffer = @$"vec4 shAr = vec4({values[0].x}, {values[0].y}, {values[0].z}, {values[0].w});
    vec4 shAg = vec4({values[1].x}, {values[1].y}, {values[1].z}, {values[1].w});
    vec4 shAb = vec4({values[2].x}, {values[2].y}, {values[2].z}, {values[2].w});
    vec4 shBr = vec4({values[3].x}, {values[3].y}, {values[3].z}, {values[3].w});
    vec4 shBg = vec4({values[4].x}, {values[4].y}, {values[4].z}, {values[4].w});
    vec4 shBb = vec4({values[5].x}, {values[5].y}, {values[5].z}, {values[5].w});
    vec4 shCr = vec4({values[6].x}, {values[6].y}, {values[6].z}, {values[6].w});
";
        GUIUtility.systemCopyBuffer = shBuffer;
        Debug.Log(shBuffer);
    }

    public void SetSHParams(SphericalHarmonicsL2 sh) {
        var shv = CalculateSHVairentMimicUnity(sh);
        Shader.SetGlobalVectorArray(CustomSH, shv);
    }

    List<Vector4> CalculateSHVairentMimicUnity(SphericalHarmonicsL2 sh) {
        var Y = new List<Vector4>();
        for (int ic = 0; ic < 3; ++ic) {
            Vector4 coefs = new Vector4();
            coefs.x = sh[ic, 3];
            coefs.y = sh[ic, 1];
            coefs.z = sh[ic, 2];
            coefs.w = sh[ic, 0] - sh[ic, 6];
            Y.Add(coefs);
        }

        for (int ic = 0; ic < 3; ++ic) {
            Vector4 coefs = new Vector4();
            coefs.x = sh[ic, 4];
            coefs.y = sh[ic, 5];
            coefs.z = sh[ic, 6] * 3.0f;
            coefs.w = sh[ic, 7];
            Y.Add(coefs);
        }

        {
            Vector4 coefs = new Vector4();
            coefs.x = sh[0, 8];
            coefs.y = sh[1, 8];
            coefs.z = sh[2, 8];
            coefs.w = 1.0f;
            Y.Add(coefs);
        }
        return Y;
    }
}