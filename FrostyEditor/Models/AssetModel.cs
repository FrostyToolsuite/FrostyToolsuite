using System;
using System.Collections.Generic;
using Frosty.Sdk.Managers.Entries;

namespace FrostyEditor.Models;

public class AssetModel
{
    public string? Name => Entry?.Filename;
    public string? Type => Entry?.Type;

    public AssetEntry? Entry { get; }

    public AssetModel(AssetEntry inEntry)
    {
        Entry = inEntry;
    }

    public static Comparison<AssetModel?> SortAscending<T>(Func<AssetModel, T> selector)
    {
        return (x, y) =>
        {
            if (x is null && y is null)
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            return Comparer<T>.Default.Compare(selector(x), selector(y));
        };
    }

    public static Comparison<AssetModel?> SortDescending<T>(Func<AssetModel, T> selector)
    {
        return (x, y) =>
        {
            if (x is null && y is null)
                return 0;
            if (x is null)
                return 1;
            if (y is null)
                return -1;
            return Comparer<T>.Default.Compare(selector(y), selector(x));
        };
    }
}