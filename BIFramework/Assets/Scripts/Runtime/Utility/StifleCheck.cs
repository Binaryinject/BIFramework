using System;
using UnityEngine;

public class StifleCheck : MonoBehaviour {
    [SerializeField]
    private SphereCollider _collider;
    [SerializeField]
    private LayerMask _layer;

    private bool _isStifle = false;
    private int _count = 0;

    public Action<bool> onStifle;
    
    private void OnTriggerEnter(Collider other) {
        if ((_layer.value & (1 << other.gameObject.layer)) > 0) {
            _count++;
            if (!_isStifle) {
                _isStifle = true;
                onStifle?.Invoke(_isStifle);
            }
        }
    }
    private void OnTriggerExit(Collider other) {
        if ((_layer.value & (1 << other.gameObject.layer)) > 0) {
            _count--;
            if (_count == 0) {
                _isStifle = false;
                onStifle?.Invoke(_isStifle);
            }
        }
    }
}
