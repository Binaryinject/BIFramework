using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace LuaAccess {
    [LuaCallCSharp]
    public class AVector4 : ABase
    {
        public Vector4 Get() {
            return new Vector4((float)access.GetDouble(index), (float)access.GetDouble(index + 1),
                (float)access.GetDouble(index + 2), (float)access.GetDouble(index + 3));
        }

        public void Set(Vector4 value) {
            access.SetDouble(index, value.x);
            access.SetDouble(index + 1, value.y);
            access.SetDouble(index + 2, value.z);
            access.SetDouble(index + 3, value.w);
        }

        public AVector4(LuaArrAccess a, int i) : base(a, i) {
        }
    }
}
