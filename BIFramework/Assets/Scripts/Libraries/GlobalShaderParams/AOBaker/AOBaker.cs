using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Diagnostics;
using Sirenix.OdinInspector;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

public class AOBaker : MonoBehaviour {
    public enum SamplesAOpreset {
        [LabelText("32")]
        VeryLow = 32,
        [LabelText("64")]
        Low = 64,
        [LabelText("128")]
        Medium = 128,
        [LabelText("256")]
        High = 256,
        [LabelText("512")]
        VeryHigh = 512,
        [LabelText("1024")]
        TooMuch = 1024,
        [LabelText("2048")]
        WayTooMuch = 2048
    }

    //Set this in the editor !
    public bool bakeOnStart = false;
    public UniversalRendererData forwardRendererData;

    public SamplesAOpreset samplesAO = SamplesAOpreset.High;

    public Transform meshParent;
    private MeshFilter[] mfs;
    private int[] saveLayer;
    private ShadowCastingMode[] saveShadowMode;

    private Vector3[] rayDir;

    private Bounds allBounds;

    private Camera AOCam;
    public RenderTexture AORT;
    public RenderTexture AORT2;
    private Texture2D vertTex;

    public Material AOMat;

    private int nbVert = 0;

    private int vertByRow = 256;

    private float radSurface;
    private static readonly int UCount = Shader.PropertyToID("_uCount");
    private static readonly int AOTex = Shader.PropertyToID("_AOTex");
    private static readonly int AOTex2 = Shader.PropertyToID("_AOTex2");
    private static readonly int UVertex = Shader.PropertyToID("_uVertex");
    private static readonly int Vp = Shader.PropertyToID("_VP");
    private static readonly int CurCount = Shader.PropertyToID("_curCount");
    const string AOCamName = "AOBaker";


    void Awake() {

        var features = forwardRendererData.rendererFeatures;
        foreach (var f in features)
        {
            if (f.name == "AOBlit")
            {
                var feature = (BlitFeature)f;
                var settings = feature.settings;
                settings.blitMaterial = AOMat;
                settings.setInverseViewMatrix = true;
                settings.dstType = BlitFeature.Target.RenderTextureObject;
                settings.cameraName = AOCamName;
                settings.requireDepth = true;
                settings.overrideGraphicsFormat = true;
                settings.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            }
        }

        forwardRendererData.SetDirty();
    }

    void Start() {
        if (bakeOnStart) BakeAORuntime();
    }
    [Button(ButtonSizes.Large)]
    public void BakeAORuntime() {
        if (forwardRendererData == null) {
            Debug.LogError("Please set forwardRendererData in the editor");
            this.enabled = false;
            return;
        }

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        nbVert = 0;
        mfs = meshParent.GetComponentsInChildren<MeshFilter>();

        var tmpMF = new List<MeshFilter>(mfs.Length);

        for (int i = 0; i < mfs.Length; i++) {
            if (mfs[i].gameObject.GetComponent<MeshRenderer>() != null) {
                nbVert += mfs[i].sharedMesh.vertexCount;
                tmpMF.Add(mfs[i]);
            }
        }

        mfs = tmpMF.ToArray();

        InitSamplePos();

        CreateAOCam();

        DoAO();

        DisplayAO();

        stopwatch.Stop();
        Debug.Log($"Time for AO  = {stopwatch.ElapsedMilliseconds / 1000f} sec");
    }

    void InitSamplePos() {
        GetBounds();

        radSurface = Mathf.Max(allBounds.extents.x, Mathf.Max(allBounds.extents.y, allBounds.extents.z));
        rayDir = new Vector3[(int)samplesAO];

        var golden_angle = Mathf.PI * (3 - Mathf.Sqrt(5));
        var start = 1 - 1.0f / (int)samplesAO;
        var end = 1.0f / (int)samplesAO - 1;

        for (int i = 0; i < (int)samplesAO; i++) {
            float theta = golden_angle * i;
            float z = start + i * (end - start) / (int)samplesAO;
            float radius = Mathf.Sqrt(1 - z * z);
            rayDir[i].x = radius * Mathf.Cos(theta);
            rayDir[i].y = radius * Mathf.Sin(theta);
            rayDir[i].z = z;
            rayDir[i] = allBounds.center + rayDir[i] * radSurface;
        }
    }

    void GetBounds() {
        saveLayer = new int[mfs.Length];
        saveShadowMode = new ShadowCastingMode[mfs.Length];

        for (int i = 0; i < mfs.Length; i++) {
            MeshRenderer mr = mfs[i].gameObject.GetComponent<MeshRenderer>();

            saveLayer[i] = mfs[i].gameObject.layer;
            saveShadowMode[i] = mr.shadowCastingMode;

            if (i == 0)
                allBounds = mr.bounds;
            else
                allBounds.Encapsulate(mr.bounds);

            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
        }
    }

    void CreateAOCam() {
        AOCam = gameObject.GetComponent<Camera>();
        if (AOCam == null)
            AOCam = gameObject.AddComponent<Camera>();

        //Set the name of the AOCamera gameobject to filter blit pass based on name
        AOCam.gameObject.name = AOCamName;

        AOCam.enabled = true;

        AOCam.orthographic = true;
        AOCam.cullingMask = LayerMask.GetMask("AOLayer");
        AOCam.clearFlags = CameraClearFlags.Depth;
        AOCam.nearClipPlane = 0.0001f;
        AOCam.allowHDR = false;
        AOCam.allowMSAA = false;
        AOCam.allowDynamicResolution = false;

        AOCam.depthTextureMode = DepthTextureMode.Depth;

        AOCam.orthographicSize = radSurface * 1.1f;
        AOCam.farClipPlane = radSurface * 2;
        AOCam.aspect = 1f;


        var additionalCamData = AOCam.GetUniversalAdditionalCameraData();
        additionalCamData.renderShadows = false;
        additionalCamData.requiresColorOption = CameraOverrideOption.On;
        additionalCamData.requiresDepthOption = CameraOverrideOption.On;
        additionalCamData.renderPostProcessing = true;

        var height = (int)Mathf.Ceil(nbVert / (float)vertByRow);

        AORT = new RenderTexture(vertByRow, height, 0, RenderTextureFormat.ARGBHalf);
        AORT.anisoLevel = 0;
        AORT.filterMode = FilterMode.Point;

        AORT2 = new RenderTexture(vertByRow, height, 0, RenderTextureFormat.ARGBHalf);
        AORT2.anisoLevel = 0;
        AORT2.filterMode = FilterMode.Point;

        vertTex = new Texture2D(vertByRow, height, TextureFormat.RGBAFloat, false);
        vertTex.anisoLevel = 0;
        vertTex.filterMode = FilterMode.Point;

        //Set last Blit settings
        var features = forwardRendererData.rendererFeatures;
        foreach (var f in features)
        {
            if (f.name == "AOBlit")
            {
                var feature = (BlitFeature)f;
                var settings = feature.settings;
                settings.dstTextureObject = AORT;
            }
        }

        forwardRendererData.SetDirty();

        FillVertexTexture();
    }

    void FillVertexTexture() {
        var idVert = 0;
        var sizeRT = vertTex.width * vertTex.height;
        var vertInfo = new Color[sizeRT];
        for (int i = 0; i < mfs.Length; i++) {
            Transform cur = mfs[i].gameObject.transform;
            var vert = mfs[i].sharedMesh.vertices;
            for (int j = 0; j < vert.Length; j++) {
                var pos = cur.TransformPoint(vert[j]);
                vertInfo[idVert].r = pos.x;
                vertInfo[idVert].g = pos.y;
                vertInfo[idVert].b = pos.z;
                idVert++;
            }
        }

        vertTex.SetPixels(vertInfo);
        vertTex.Apply(false, false);
    }

    void changeAspectRatio() {
        var targetaspect = 1.0f;

        // determine the game window's current aspect ratio
        var windowaspect = Screen.width / (float)Screen.height;

        // current viewport height should be scaled by this amount
        var scaleheight = windowaspect / targetaspect;


        // if scaled height is less than current height, add letterbox
        if (scaleheight < 1.0f) {
            var rect = AOCam.rect;

            rect.width = 1.0f;
            rect.height = scaleheight;
            rect.x = 0;
            rect.y = (1.0f - scaleheight) / 2.0f;

            AOCam.rect = rect;
        }
        else // add pillarbox
        {
            float scalewidth = 1.0f / scaleheight;

            var rect = AOCam.rect;

            rect.width = scalewidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scalewidth) / 2.0f;
            rect.y = 0;

            AOCam.rect = rect;
        }
    }


    void DoAO() {
        AOMat.SetInt(UCount, (int)samplesAO);
        AOMat.SetTexture(AOTex, AORT);
        AOMat.SetTexture(AOTex2, AORT2);
        AOMat.SetTexture(UVertex, vertTex);

        var AOLayer = LayerMask.NameToLayer("AOLayer");
        for (int i = 0; i < mfs.Length; i++) {
            mfs[i].gameObject.layer = AOLayer;
        }

        for (int i = 0; i < (int)samplesAO; i++) {
            AOCam.transform.position = rayDir[i];
            AOCam.transform.LookAt(allBounds.center);

            var V = AOCam.worldToCameraMatrix;
            var P = AOCam.projectionMatrix;

            // Invert Y for rendering to a render texture
            for (int a = 0; a < 4; a++) {
                P[1, a] = -P[1, a];
            }

            // Scale and bias from OpenGL -> D3D depth range
            for (int a = 0; a < 4; a++) {
                P[2, a] = P[2, a] * 0.5f + P[3, a] * 0.5f;
            }

            AOMat.SetMatrix(Vp, P * V);
            AOMat.SetInt(CurCount, i);
            AOCam.Render();

            Graphics.CopyTexture(AORT, AORT2);
        }

        for (int i = 0; i < mfs.Length; i++) {
            mfs[i].gameObject.layer = saveLayer[i];
            mfs[i].gameObject.GetComponent<MeshRenderer>().shadowCastingMode = saveShadowMode[i];
        }

        AOCam.enabled = false;
    }


    void DisplayAO() {
        //Create a texture containing AO information read by the mesh shader
        var alluv = new List<Vector2[]>(mfs.Length);

        //var matShowAO = new Material(Shader.Find("GeoAO/VertAOOpti"));
        Shader.SetGlobalTexture(AOTex, AORT);
        //matShowAO.SetTexture(AOTex, AORT);
        float w = AORT2.width - 1;
        float h = AORT2.height - 1;
        int idVert = 0;
        for (int i = 0; i < mfs.Length; i++) {
            var vert = mfs[i].sharedMesh.vertices;
            alluv.Add(new Vector2[vert.Length]);
            for (int j = 0; j < vert.Length; j++) {
                alluv[i][j] = new Vector2(idVert % vertByRow / w, idVert / vertByRow / h);
                idVert++;
            }

            mfs[i].mesh.uv3 = alluv[i]; //This creates a new instance of the mesh !
            mfs[i].mesh.UploadMeshData(true);
            //mfs[i].gameObject.GetComponent<Renderer>().material = matShowAO;
        }
    }
}