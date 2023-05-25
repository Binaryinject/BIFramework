using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace LuaAccess {
    [LuaCallCSharp]
    public class AFloat : ABase
    {
        public float Get() => (float)access.GetDouble(index);

        public void Set(float value) => access.SetDouble(index, value);

        public AFloat(LuaArrAccess a, int i) : base(a, i) {
        }
    }
}
