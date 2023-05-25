using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using LuaAPI = XLua.LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;

namespace XLua
{
    public partial class StaticLuaCallbacks
    {
        public static string LogFormatting(string s)
        {
            var regexPath = new Regex(@"\[string "".*(?<path>Assets.*)""]*:(?<line>\d+):|<\[string "".*(?<path>Assets.*)""]:(?<line>\d+)>|\S*(?<path>Assets.*):(?<line>\d+):|<\S*(?<path>Assets.*):(?<line>\d+)>");
            return regexPath.Replace(s, "${path}:${line}");
        }
        /// <summary>
        /// 在lua中调用print打印出lua文件名和行号
        /// </summary>
        /// <returns>返回200是成功</returns>
        public static int PrintWithLua(RealStatePtr L, string s)
        {
            LuaAPI.xlua_getglobal(L, "debug");
            if (!LuaAPI.lua_istable(L, -1))
            {
                LuaAPI.lua_pop(L, 1);
                return 1;
            }

            LuaAPI.xlua_pushasciistring(L, "traceback");
            if (0 != LuaAPI.xlua_pgettable(L, -2))
            {
                return 1;
            }

            if (0 != LuaAPI.lua_pcall(L, 0, 1, 0))
            {
                return LuaAPI.lua_error(L);
            }

            string traceback = LuaAPI.lua_tostring(L, -1);
            s += "\n" + traceback;
            LuaAPI.lua_pop(L, 1); /* pop result */
            try
            {
                Debug.Log("LUA: " + LogFormatting(s));
            }
            catch (Exception)
            {
                //UnityEngine.Debug.LogError(ex.Message);
                Debug.Log("LUA: " + s);
            }
            
            return 200;
        }
    }
}