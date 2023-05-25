using UnityEngine;
using Random = UnityEngine.Random;

public class FixedPointRandomMove : MonoBehaviour {
    [Min(0)]
    public float moveSpeed = 0.1f;
    [Min(0)]
    public float sqrMagnitude = 0.04f;

    private Vector3 _origin = Vector3.zero;
    private float _angle = 0f;
    private float _endAngle = 0f;

    void Awake() {
        _origin = transform.position;
    }

    void Update() {
        var next = Vector3.Lerp(transform.position, transform.position + transform.forward, Time.deltaTime * moveSpeed);
        var vec = next - _origin;
        var curAngle = Vector3.Angle(transform.forward, -vec.normalized);
        if (Vector3.SqrMagnitude(vec) > sqrMagnitude && curAngle > 90f) {
            Choice();
        }
        if (_angle != 0f) {
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(transform.eulerAngles + new Vector3(0f, _angle, 0f)), Time.deltaTime);
        }
        if (curAngle < _endAngle) {
            _angle = 0f;
        }
        transform.position = next;
    }

    void Choice() {
        if (_angle == 0) {
            _angle = Random.Range(-1f, 1f) * 180f;
            if (Mathf.Abs(_angle) < 45f) {
                _angle += _angle > 0f ? 45f : -45f;
            }
            _endAngle = Random.Range(30f, 90f);
        }
        else {
            if (_angle > 0) {
                _angle = Random.Range(_angle + 10f, 180f);
            }
            else {
                _angle = Random.Range(-180f, _angle - 10f);
            }
        }
    }
}
