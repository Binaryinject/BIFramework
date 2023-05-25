using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace LuaAccess {
    [LuaCallCSharp]
    public class ABool : ABase
    {
        public bool Get() => access.GetInt(index) != 0;

        public void Set(bool value) => access.SetInt(index, value ? 1 : 0);

        public ABool(LuaArrAccess a, int i) : base(a, i) {
        }
    }
}

