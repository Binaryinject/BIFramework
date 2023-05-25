using System;
using Com.TheFallenGames.OSA.CustomAdapters.GridView;
using UnityEngine;
using XLua;

[LuaCallCSharp]
public class LuaGridAdapter : GridAdapter<GridParams, LuaCellViewHolder> {
    public Action<LuaCellViewHolder> updateCellViewsHolder = null;
    protected override void UpdateCellViewsHolder(LuaCellViewHolder viewsHolder) {
        updateCellViewsHolder?.Invoke(viewsHolder);
    }
}

[LuaCallCSharp]
public class LuaCellViewHolder : CellViewsHolder {
}