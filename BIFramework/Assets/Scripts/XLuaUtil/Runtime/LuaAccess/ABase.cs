using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BIFramework;
using BIFramework.Singleton;
using XLua;

namespace LuaAccess {
    [LuaCallCSharp]
    public class ABase {
        public int index = -1;
        public LuaArrAccess access;
        public ABase(LuaArrAccess a, int i) {
            access = a;
            index = i;
        }
    }
    
}
