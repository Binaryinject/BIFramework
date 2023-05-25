using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XLua;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BIFramework {
    [LuaCallCSharp]
    public class GrayscaleGroup : MonoBehaviour {
        [SerializeField]
        [HideInInspector]
        private bool _grayscaleInGroup = false;

        [SerializeField]
        [HideInInspector]
        private bool _includeInactive = false;

        private static Material _grayscaleUIMat = null;

        [ShowInInspector]
        [PropertyOrder(1)]
        public bool GrayscaleInGroup {
            get { return _grayscaleInGroup; }
            set {
                if (_grayscaleUIMat == null) _grayscaleUIMat = Resources.Load<Material>("GrayscaleUI");
                var images = GetComponentsInChildren<Image>(_includeInactive);
                foreach (var image in images) {
                    image.material = value ? _grayscaleUIMat : null;
                }

                var tmps = GetComponentsInChildren<TextMeshProUGUI>(_includeInactive);

                foreach (var tmp in tmps) {
                    TMPro_changeMaterial changeMaterial = null;
                    foreach (var change in grayscaleFontMaterials) {
                        if (change.defaultMaterial == tmp.font.material) {
                            changeMaterial = change;
                            break;
                        }
                    }

                    if (changeMaterial == null) {
                        if (!Application.isPlaying) {
#if UNITY_EDITOR
                            var newChange = new TMPro_changeMaterial();
                            newChange.defaultMaterial = tmp.font.material;
                            Material preset = null;
                            preset = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_StaticAssets/Fonts/{tmp.font.name} Grayscale.mat");
                            newChange.grayscaleMaterial = preset;
                            grayscaleFontMaterials.Add(newChange);

                            if (preset) tmp.fontMaterial = preset;
#endif
                        }
                    }
                    else {
                        if (value && changeMaterial.grayscaleMaterial) {
                            tmp.fontMaterial = changeMaterial.grayscaleMaterial;
                        }
                        else if (!value) {
                            tmp.fontMaterial = changeMaterial.defaultMaterial;
                        }
                    }
                }

                _grayscaleInGroup = value;
            }
        }

        [ShowInInspector]
        [PropertyOrder(2)]
        public bool IncludeInactive {
            get { return _includeInactive; }
            set { _includeInactive = value; }
        }

        [Serializable]
        public class TMPro_changeMaterial {
            [HorizontalGroup("TMChange")]
            [HideLabel]
            public Material defaultMaterial = null;

            [HorizontalGroup("TMChange")]
            [HideLabel]
            public Material grayscaleMaterial = null;
        }

        [PropertyOrder(3)]
        [ShowIf("@grayscaleFontMaterials.Count > 0")]
        [OnValueChanged("OnValueChange")]
        [InlineButton("Reset")]
        [ListDrawerSettings(ShowFoldout = true, IsReadOnly = true)]
        public List<TMPro_changeMaterial> grayscaleFontMaterials = new();

        private void OnValueChange() {
            GrayscaleInGroup = _grayscaleInGroup;
        }

        private void Reset() {
            grayscaleFontMaterials = new();
            OnValueChange();
        }
    }
}