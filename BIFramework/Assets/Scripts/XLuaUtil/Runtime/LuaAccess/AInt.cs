using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace LuaAccess {
    [LuaCallCSharp]
    public class AInt : ABase
    {
        public int Get() => access.GetInt(index);

        public void Set(int value) => access.SetInt(index, value);

        public AInt(LuaArrAccess a, int i) : base(a, i) {
        }
    }
}

