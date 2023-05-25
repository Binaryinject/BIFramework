﻿using System;
using System.Runtime.InteropServices;
using XLua;
using XLua.LuaDLL;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;


// TValue from lua source lobject.h, 32 bit version
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct LuaTValue32
{
    // GCObject*
    [FieldOffset(0)]
    public IntPtr gc;

    // bool
    [FieldOffset(0)]
    public int b;

    //lua_CFunction
    [FieldOffset(0)]
    public IntPtr f;

    // number
    [FieldOffset(0)]
    public float n;

    // integer value
    [FieldOffset(0)]
    public int i;

    // lu_byte tt_
    [FieldOffset(4)]
    public byte tt_;
}


// 64 bit version
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct LuaTValue64
{
    // uint64
    [FieldOffset(0)]
    public UInt64 u64;

    // number
    [FieldOffset(0)]
    public double n;

    // integer value
    [FieldOffset(0)]
    public long i;

    // lu_byte tt_
    [FieldOffset(8)]
    public byte tt_;
}


// Table from lua source lobject.h
[StructLayout(LayoutKind.Sequential)]
public struct LuaTableRawDef
{
    public IntPtr next;

    // lu_byte tt; lu_byte marked; lu_byte flags; lu_byte lsizenode;
    public uint bytes;

    // unsigned int sizearray
    public uint sizearray;

    // TValue* alimit
    public IntPtr alimit;

    // Node* node
    public IntPtr node;

    // Node* lastfree
    public IntPtr lastfree;

    // Table* metatable
    public IntPtr metatable;

    // GCObejct* gclist
    public IntPtr gclist;
}



public static class LuaEnvValues
{
    // only set false if using Lua5.3 and LUA_32BITS is enabled, see luaconf.h.in
    public const bool Is64Bit = true;

    // only set true if using Luajit and LJ_GC64 is enabled
    public const bool IsGC64 = false;

    public const byte LUA_TNUMBER = 3;
    public const byte LUA_TTABLE = 5;

    //lua 5.4 is change
    public const byte LUA_TNUMINT = LUA_TNUMBER | (0 << 4);
    public const byte LUA_TNUMFLT = LUA_TNUMBER | (1 << 4);
}


public class LuaAdapterException : System.Exception
{
    public LuaAdapterException(string message) : base(message)
    {

    }

    public static string GenMessage(bool isNull, int index, uint arrSize) {
        return isNull ? "ptr is null" : $"index error {index} {arrSize}";
    }

    public static void ThrowIfNeeded(bool isNull, int index, uint arrSize) {
        if (isNull) return;
        throw new LuaAdapterException($"index error {index} {arrSize}");
    }
}



[LuaCallCSharp]
public unsafe class LuaArrAccessAPI
{
    public static bool IsLuajit = false;

    public static void RegisterPinFunc(System.IntPtr L)
    {
        string name = "lua_safe_pin_bind";
        Lua.lua_pushstdcallcfunction(L, PinFunction);
        if (0 != Lua.xlua_setglobal(L, name))
        {
            throw new Exception("call xlua_setglobal fail!");
        }
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    public static int PinFunction(IntPtr L)
    {
        // set lua table ptr
        ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
        IntPtr TablePtr = Lua.lua_topointer(L, 1);
        LuaArrAccess gen_to_be_invoked = (LuaArrAccess)translator.FastGetCSObj(L, 2);
        if (TablePtr != IntPtr.Zero && Lua.lua_istable(L, 1))
        {
            gen_to_be_invoked.OnPin(TablePtr);
        }

        return 0;
    }

    public static void Init(bool IsJit)
    {
        IsLuajit = IsJit;
    }

    public static LuaArrAccess CreateLuaShareAccess() {
        if (IsLuajit)
        {
            return new LuaJitArrAccess();
        }

        if(LuaEnvValues.Is64Bit)
        {
            return new LuaArrAccess64();
        }
    }
}

[LuaCallCSharp]
public unsafe class LuaArrAccess
{
    public override string ToString()
    {
        return "LuaArrAccess Unknown Type";
    }

    public virtual void OnPin(IntPtr TablePtr)
    {

    }

    public virtual void OnGC()
    {
    }

    public virtual void AutoDetectArch()
    {

    }

    public virtual bool IsValid()
    {
        return false;
    }

    public virtual uint GetArrayCapacity()
    {
        return 0;
    }

    public virtual int GetInt(int index)
    {
        return 0;
    }

    public virtual void SetInt(int index, int Value)
    {
    }

    public virtual double GetDouble(int index)
    {
        return 0;
    }

    public virtual void SetDouble(int index, double Value)
    {
    }
}


[LuaCallCSharp]
public unsafe class LuaArrAccess32 : LuaArrAccess
{
    LuaTableRawDef* TableRawPtr;

    public override string ToString()
    {
        IntPtr Ptr = (IntPtr)TableRawPtr;
        return "LuaTablePin32 " + Ptr;
    }

    public override void OnPin(IntPtr TablePtr)
    {
        TableRawPtr = (LuaTableRawDef*)TablePtr;
    }

    public override void OnGC()
    {
        TableRawPtr = null;
    }

    public override bool IsValid()
    {
        return TableRawPtr != null;
    }

    public override uint GetArrayCapacity() {
        if (TableRawPtr != null)
        {
            return TableRawPtr->sizearray;
        }

        return 0;
    }

    public override int GetInt(int index) {
        if(TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var tv = (LuaTValue32*)TableRawPtr->alimit + index;
            return tv->tt_ == LuaEnvValues.LUA_TNUMINT ? tv->i : (int) tv->n;
        }

        LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        return 0;
    }

    public override void SetInt(int index, int Value)
    {
        if (TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var v = (LuaTValue32*)TableRawPtr->alimit + index;
            v->i = Value;
            v->tt_ = LuaEnvValues.LUA_TNUMINT;
        }
        else
        {
            LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        }

    }

    public override double GetDouble(int index) {
        if (TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var tv = (LuaTValue32*)TableRawPtr->alimit + index;
            if (tv->tt_ == LuaEnvValues.LUA_TNUMINT)
            {
                return tv->i;
            }

            return tv->n;
        }

        LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        return 0;
    }

    public override void SetDouble(int index, double Value)
    {
        if (TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var v = (LuaTValue32*)TableRawPtr->alimit + index;
            v->n = (float)Value;
            v->tt_ = LuaEnvValues.LUA_TNUMFLT;
        }
        else
        {
            LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        }
    }
}



[LuaCallCSharp]
public unsafe class LuaArrAccess64 : LuaArrAccess
{
    LuaTableRawDef* TableRawPtr;

    public override string ToString()
    {
        IntPtr Ptr = (IntPtr)TableRawPtr;
        return "LuaTablePin64 " + Ptr;
    }

    public override void OnPin(IntPtr TablePtr)
    {
        TableRawPtr = (LuaTableRawDef*)TablePtr;
    }

    public override void OnGC()
    {
        TableRawPtr = null;
    }

    public override bool IsValid()
    {
        return TableRawPtr != null;
    }

    public override uint GetArrayCapacity() {
        if (TableRawPtr != null)
        {
            return TableRawPtr->sizearray;
        }

        return 0;
    }

    public override int GetInt(int index) {
        if (TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var tv = (LuaTValue64*)TableRawPtr->alimit + index;
            if (tv->tt_ == LuaEnvValues.LUA_TNUMINT)
            {
                return (int)tv->i;
            }

            return (int)tv->n;
        }

        LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        return 0;
    }

    public override void SetInt(int index, int Value)
    {
        if (TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var v = (LuaTValue64*)TableRawPtr->alimit + index;
            v->i = Value;
            v->tt_ = LuaEnvValues.LUA_TNUMINT;
        }
        else
        {
            LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        }
    }

    public override double GetDouble(int index) {
        if (TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var tv = (LuaTValue64*)TableRawPtr->alimit + index;
            if (tv->tt_ == LuaEnvValues.LUA_TNUMINT)
            {
                return tv->i;
            }

            return tv->n;
        }

        LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        return 0;
    }

    public override void SetDouble(int index, double Value)
    {
        if (TableRawPtr != null && index > 0 && index <= TableRawPtr->sizearray)
        {
            index -= 1;
            var v = (LuaTValue64*)TableRawPtr->alimit + index;
            v->n = Value;
            v->tt_ = LuaEnvValues.LUA_TNUMFLT;
        }
        else
        {
            LuaAdapterException.ThrowIfNeeded(TableRawPtr == null, index, GetArrayCapacity());
        }
    }
}