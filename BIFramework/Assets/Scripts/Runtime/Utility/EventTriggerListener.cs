using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;
using XLua;

[LuaCallCSharp]
public class EventTriggerListener : EventTrigger {
    public delegate void VoidDelegate<T>(T eventData);

    public VoidDelegate<PointerEventData> onClick;
    public VoidDelegate<PointerEventData> onDown;
    public VoidDelegate<PointerEventData> onEnter;
    public VoidDelegate<PointerEventData> onExit;
    public VoidDelegate<PointerEventData> onUp;
    public VoidDelegate<PointerEventData> onBeginDrag;
    public VoidDelegate<PointerEventData> onDrag;
    public VoidDelegate<PointerEventData> onEndDrag;
    public VoidDelegate<BaseEventData> onSelect;
    public VoidDelegate<BaseEventData> onUpdateSelect;
    public VoidDelegate<AxisEventData> onMove;

    public static EventTriggerListener Get(GameObject go) {
        var listener = go.GetComponent<EventTriggerListener>();
        if (listener == null) listener = go.AddComponent<EventTriggerListener>();
        return listener;
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        onClick?.Invoke(eventData);
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        onDown?.Invoke(eventData);
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        onEnter?.Invoke(eventData);
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        onExit?.Invoke(eventData);
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        onUp?.Invoke(eventData);
    }

    public override void OnSelect(BaseEventData eventData)
    {
        onSelect?.Invoke(eventData);
    }

    public override void OnUpdateSelected(BaseEventData eventData)
    {
        onUpdateSelect?.Invoke(eventData);
    }

    public override void OnMove(AxisEventData eventData)
    {
        onMove?.Invoke(eventData);
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        onBeginDrag?.Invoke(eventData);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        onDrag?.Invoke(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        onEndDrag?.Invoke(eventData);
    }
}