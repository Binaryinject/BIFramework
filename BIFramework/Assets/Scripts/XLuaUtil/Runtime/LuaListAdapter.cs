using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Com.TheFallenGames.OSA.Core;
using XLua;

[LuaCallCSharp]
public class LuaListAdapter : OSA<BaseParams, LuaItemViewsHolder> {
    public delegate GameObject CreateViewsHolderDelegate(int itemIndex);

    public delegate float CollectItemsSizesDelegate(int itemIndex);

    public CreateViewsHolderDelegate createViewsHolder = null;
    public CollectItemsSizesDelegate collectItemsSizes = null;
    public Action<LuaItemViewsHolder> updateViewsHolder = null;
    public Action<LuaItemViewsHolder> onRootCreated = null;
    public Action<LuaItemViewsHolder, int> onBeforeRecycleOrDisableViewsHolder = null;

    #region OSA implementation

    protected override LuaItemViewsHolder CreateViewsHolder(int itemIndex) {
        var instance = new LuaItemViewsHolder();
        instance.onRootCreated = onRootCreated;
        instance.Init(createViewsHolder(itemIndex), _Params.Content, itemIndex);
        return instance;
    }

    protected override void UpdateViewsHolder(LuaItemViewsHolder newOrRecycled) {
        updateViewsHolder?.Invoke(newOrRecycled);
        newOrRecycled.onRootCreated?.Invoke(newOrRecycled);
    }

    protected override void CollectItemsSizes(ItemCountChangeMode changeMode, int count, int indexIfInsertingOrRemoving, ItemsDescriptor itemsDesc) {
        base.CollectItemsSizes(changeMode, count, indexIfInsertingOrRemoving, itemsDesc);

        if (collectItemsSizes == null || changeMode == ItemCountChangeMode.REMOVE || count == 0)
            return;

        var indexOfFirstItemThatWillChangeSize = changeMode == ItemCountChangeMode.RESET ? 0 : indexIfInsertingOrRemoving;

        int end = indexOfFirstItemThatWillChangeSize + count;

        itemsDesc.BeginChangingItemsSizes(indexOfFirstItemThatWillChangeSize);
        for (int i = indexOfFirstItemThatWillChangeSize; i < end; ++i) {
            itemsDesc[i] = collectItemsSizes(i);
        }

        itemsDesc.EndChangingItemsSizes();
    }
    protected override void OnBeforeRecycleOrDisableViewsHolder(LuaItemViewsHolder inRecycleBinOrVisible, int newItemIndex)
    {
        onBeforeRecycleOrDisableViewsHolder?.Invoke(inRecycleBinOrVisible, newItemIndex);
        base.OnBeforeRecycleOrDisableViewsHolder(inRecycleBinOrVisible, newItemIndex);
    }

    #endregion
}


public class LuaItemViewsHolder : BaseItemViewsHolder {
    public Action<LuaItemViewsHolder> onRootCreated = null;
    protected override void OnRootCreated(int itemIndex, bool activateRootGameObject = true, bool callCollectViews = true) {
        base.OnRootCreated(itemIndex, activateRootGameObject, callCollectViews);
        onRootCreated?.Invoke(this);
    }
}