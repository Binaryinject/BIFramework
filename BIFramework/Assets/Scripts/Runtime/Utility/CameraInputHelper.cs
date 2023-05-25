using System.Collections.Generic;
using Cinemachine;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CameraInputHelper : MonoBehaviour {
    public GameObject mapCenter;
    public CinemachineFreeLook freeLook;

    private Plane _plane;
    private Vector3 _touchStartPoint;
    private Vector3 _touchCurrentPosition;
    private Vector3 _newPosition;
    
    void Start() {
        
        _plane = new Plane(Vector3.up, Vector3.zero);
        _newPosition = mapCenter.transform.position;
    }

    private void Update() {
        MapScroll();
        
        MapRotate();
        
        Panning();
    }

    private void MapRotate() {
        if(Input.GetMouseButton(0)) return;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (Input.GetMouseButton(1)) {
            freeLook.m_XAxis.m_InputAxisValue = Input.GetAxis("Mouse X");
        }
        else {
            freeLook.m_XAxis.m_InputAxisValue = 0;
        }
#else
        //TODO 移动平台逻辑
#endif
    }

    private void MapScroll() {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        freeLook.m_YAxis.m_InputAxisValue = Input.GetAxis("Mouse ScrollWheel");
#else
        //TODO 移动平台逻辑
#endif
    }

    void Panning() {
        var hitPoint = Vector3.zero;
        if (Input.GetMouseButtonDown(0)) {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (_plane.Raycast(ray, out var enter)) {
                hitPoint = ray.GetPoint(enter);
                _touchStartPoint = hitPoint;
            }
        }

        if (Input.GetMouseButton(0)) {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (_plane.Raycast(ray, out var enter)) {
                hitPoint = ray.GetPoint(enter);
                _touchCurrentPosition = hitPoint;
                _newPosition = mapCenter.transform.position + _touchStartPoint - _touchCurrentPosition;
            }
        }

        mapCenter.transform.position = Vector3.Lerp(mapCenter.transform.position, _newPosition, Time.deltaTime * 10);
    }

}