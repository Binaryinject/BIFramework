using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using XLua;

[LuaCallCSharp]
public class ICollider : MonoBehaviour {
    #region ENUM/STRUCT

    public enum Type {
        Box,
        Sphere,
        Capsule
    }

    #endregion

    #region FIELDS

    [SerializeField]
    [BoxGroup("类型参数")]
    [LabelText("类型")]
    [EnumToggleButtons]
    private Type _type = Type.Box;

    [SerializeField]
    [BoxGroup("类型参数")]
    [ShowIf("@_type == Type.Box")]
    [LabelText("半范围")]
    private Vector3 _halfExtents = new(0.2f, 0.2f, 0.2f);

    [SerializeField]
    [BoxGroup("类型参数")]
    [ShowIf("@_type == Type.Sphere || _type == Type.Capsule")]
    [LabelText("半径")]
    private float _radius = 0.2f;

    [SerializeField]
    [BoxGroup("类型参数")]
    [ShowIf("@_type == Type.Capsule")]
    [LabelText("相对中心的偏移距离")]
    private float _offset = 0.2f;

    [SerializeField]
    [LabelText("目标层级")]
    private LayerMask _mask;

    private Collider[] _results = new Collider[8];
    private List<Transform> _colliders = new();
    private List<Transform> _news = new();

    private bool _isValid = false;

    #endregion


    #region PROPERTIES

    public Type type {
        get => _type;
        set => _type = value;
    }
    
    public Vector3 halfExtents {
        get => _halfExtents;
        set => _halfExtents = value;
    }
    
    public float radius {
        get => _radius;
        set => _radius = value;
    }
    
    public float offset {
        get => _offset;
        set => _offset = value;
    }

    public bool isValid {
        get => _isValid;
        set {
            _isValid = value;
            _colliders.Clear();
            _news.Clear();
        }
    }

    #endregion

    #region EVENTS

    public Action<Transform[]> onCollider;

    #endregion

    #region CUSTOM METHODS

    private void OnStart() {
    }

    private void OnFixedUpdate() {
        if (isValid) {
            switch (_type) {
                case Type.Box:
                    Physics.OverlapBoxNonAlloc(transform.position, _halfExtents, _results, transform.rotation, _mask);
                    break;
                case Type.Sphere:
                    Physics.OverlapSphereNonAlloc(transform.position, _radius, _results, _mask);
                    break;
                case Type.Capsule:
                    Physics.OverlapCapsuleNonAlloc(transform.position + transform.up * _offset, transform.position + transform.up * -_offset, _radius, _results, _mask);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            for (int i = 0; i < _results.Length; i++) {
                if (_results[i] != null) {
                    var tf = _results[i].transform;
                    if (!_news.Contains(tf) && !_colliders.Contains(tf)) {
                        _news.Add(tf);
                    }
                }

                _results[i] = null;
            }

            if (_news.Count > 0) {
                _colliders.AddRange(_news);
                onCollider?.Invoke(_news.ToArray());
                _news.Clear();
            }
        }
    }

    #endregion

    #region MONO METHODS

    void Start() {
        OnStart();
    }

    void FixedUpdate() {
        OnFixedUpdate();
    }

    #endregion
}