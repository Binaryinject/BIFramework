using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace LuaAccess {
    [LuaCallCSharp]
    public class ADouble : ABase {
        public double Get() => access.GetDouble(index);

        public void Set(double value) => access.SetDouble(index, value);

        public ADouble(LuaArrAccess a, int i) : base(a, i) {
        }
    }
}