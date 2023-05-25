using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace LuaAccess {
    [LuaCallCSharp]
    public class AVector3 : ABase
    {
        public Vector3 Get() {
            return new((float)access.GetDouble(index), (float)access.GetDouble(index + 1), (float)access.GetDouble(index + 2));
        }

        public void Set(Vector3 value) {
            access.SetDouble(index, value.x);
            access.SetDouble(index + 1, value.y);
            access.SetDouble(index + 2, value.z);
        }

        public AVector3(LuaArrAccess a, int i) : base(a, i) {
        }
    }
}
