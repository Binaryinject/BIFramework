using System;
using System.Collections;
using System.Collections.Generic;
using LuaAccess;
using Sirenix.OdinInspector;
using UnityEngine;
using XLua;

[LuaCallCSharp]
public class LuaCsharpAccess : MonoBehaviour {
    public AInt cInt = null;
    public AFloat cFloat = null;
    public ABool cBool = null;
    public ADouble cDouble = null;
    public AVector3 cVector3 = null;
    public AVector4 cVector4 = null;
    public Action onChangeDataFinish = null;
    [Button(ButtonSizes.Large)]
    void ChangeData()
    {
        cInt?.Set(123456789);
        cFloat?.Set(1.234f);
        cBool?.Set(true);
        cDouble?.Set(123.456d);
        cVector3?.Set(new Vector3(1, 2, 3));
        cVector4?.Set(new Vector4(2.2f, 3.3f, 4.4f, 5.5f));
        onChangeDataFinish?.Invoke();
    }

}
