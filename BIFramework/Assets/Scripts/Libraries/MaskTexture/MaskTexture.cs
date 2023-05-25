using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

public class MaskTexture : MonoBehaviour {
    public bool update = false;
    public Vector3Int gridSize = new(30, 30, 30);

    const int TEXTURE_SIZE = 512;
    [ShowIf("@m_Texture != null")]
    [PreviewField(ObjectFieldAlignment.Center, Height = 256, FilterMode = FilterMode.Point)]
    [HideLabel]
    [Sirenix.OdinInspector.ReadOnly]
    public Texture2D preview;
    
    public bool enableFX {
        set {
            if (value) Shader.EnableKeyword("_MASKON");
            else Shader.DisableKeyword("_MASKON");
        }
    }

    private Texture2D m_Texture = null;
    private static readonly int MapMaskTexture = Shader.PropertyToID("_MapMaskTexture");


    public void MaskTextureJob(Bounds[] bounds) {
        m_Texture = new Texture2D(gridSize.x, gridSize.z, TextureFormat.RGBA32, false);
        m_Texture.filterMode = FilterMode.Point;
        m_Texture.wrapMode = TextureWrapMode.Clamp;
        var data = m_Texture.GetPixelData<Color32>(0);
        var job = new CalcMaskIntoNativeArrayBurstParallel {
            data = data,
            bounds = new NativeArray<Bounds>(bounds, Allocator.TempJob),
            gridSize = new int2(gridSize.x, gridSize.z),
        };
        job.Schedule(gridSize.z, 1).Complete();
        job.bounds.Dispose();
        m_Texture.Apply(false);
        var scale = gridSize.x > gridSize.z ? TEXTURE_SIZE / gridSize.x : TEXTURE_SIZE / gridSize.z;
        preview = new Texture2D(gridSize.x * scale, gridSize.z * scale, TextureFormat.RGBA32, false);
        preview.filterMode = FilterMode.Point;
        preview.wrapMode = TextureWrapMode.Clamp;
        Graphics.ConvertTexture(m_Texture, preview);
        Shader.SetGlobalTexture(MapMaskTexture, preview);
        
    }

    [GUIColor(0, 1, 0)]
    [Button(ButtonSizes.Large)]
    public void GenerateMaskTexture() {
        //var sw = new Stopwatch();
        //sw.Start();
        transform.position = Vector3.zero;
        var box = GetComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(0.4f * gridSize.x, 0.4f * gridSize.y, 0.4f * gridSize.z);
        var eachs = transform.GetComponentsInChildren<BoxCollider>();
        var list = eachs.ToList();
        list.RemoveAll(v => v.transform == transform);
        eachs = list.ToArray();
        var bounds = new Bounds[eachs.Length];
        for (int i = 0; i < eachs.Length; i++) {
            var pos = eachs[i].transform.position;
            var size = eachs[i].size;
            eachs[i].transform.position = new Vector3(pos.x, 0, pos.z);
            eachs[i].size = new Vector3(size.x, 0.4f * gridSize.y, size.z);
            bounds[i] = eachs[i].bounds;
        }

        MaskTextureJob(bounds);
        //sw.Stop();
        //Debug.Log($"mask texture generate use time: {sw.ElapsedMilliseconds / 1000f} sec");
    }

    [BurstCompile]
    struct CalcMaskIntoNativeArrayBurstParallel : IJobParallelFor {
        [NativeDisableParallelForRestriction]
        public NativeArray<Color32> data;

        [NativeDisableParallelForRestriction]
        public NativeArray<Bounds> bounds;

        public int2 gridSize;

        public void Execute(int z) {
            var offset = new float3(-0.2f * gridSize.x + 0.2f, 0, -0.2f * gridSize.y + 0.2f);
            var idx = z * gridSize.x;
            for (var x = 0; x < gridSize.x; ++x) {
                var inside = false;
                var pos = offset + new float3(x * 0.4f, 0, z * 0.4f);
                for (int i = 0; i < bounds.Length; i++) {
                    if (bounds[i].Contains(pos)) {
                        inside = true;
                        break;
                    }
                }

                data[idx++] = inside ? Color.white : Color.black;
            }
        }
    }

    private void Update() {
        if (update) {
            GenerateMaskTexture();
        }
    }
}