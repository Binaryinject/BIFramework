using UnityEngine;
using XLua;

[LuaCallCSharp]
public class UIFollowTarget : MonoBehaviour {
    public Transform target;
    public Vector2 offset;

    private RectTransform _rectTransform;
    private Vector2 viewPoint;
    private float width;
    private float height;
    
    private void Awake() {
        _rectTransform = GetComponent<RectTransform>();
        width = Screen.width;
        height= Screen.height;
    }

    // Update is called once per frame
    void Update()
    {
        if (target) {
            viewPoint = Camera.main.WorldToViewportPoint(target.position);
            _rectTransform.anchoredPosition = new Vector2(width * viewPoint.x, height * viewPoint.y) + offset;
        }
    }
}
