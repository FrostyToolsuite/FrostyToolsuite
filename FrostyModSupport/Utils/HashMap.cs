using System.Diagnostics;

namespace Frosty.ModSupport.Utils;

public class HashMap
{
    public static uint HashData(ReadOnlySpan<byte> data, uint initial = 0x00)
    {
       uint hash = (uint)((sbyte)data[0] ^ 0x50C5D1F);
       int start = 1;
       if (initial != 0x00)
       {
           hash = initial;
           start = 0;
       }

       for (int i = start; i < data.Length; i++)
       {
           uint b = (uint)((sbyte)data[i]);
           hash = b ^ 0x1000193 * hash;
       }

       return hash;
    }

    public static uint HashData2(ReadOnlySpan<byte> data, uint offset = 0x811c9dc5)
    {
        uint prime = 0x01000193;

        uint hash = offset;

        for (int i = 0; i < data.Length; i++)
        {
            hash = (hash * prime) ^ (uint)(sbyte)data[i];
        }

        return hash % prime;
    }

    public static int GetIndex(ReadOnlySpan<byte> inData, int inCount, uint inInitial = 0x811c9dc5)
    {
        return (int)(HashData2(inData, inInitial) % inCount);
    }

    public static List<int> CreateHashMap<T1>(ref List<T1> inItems, Func<T1, int, uint, int> getIndexFunc)
    {
        int l = 0;
        List<int> hashmap = new(inItems.Count);
        List<T1> sorted = new(inItems.Count);
        List<bool> slots = new(inItems.Count);
        Dictionary<int, List<T1>> buckets = new();

        foreach (T1 item in inItems)
        {
            hashmap.Add(-1);
            slots.Add(false);
            sorted.Add(default);
            int idx = getIndexFunc(item, inItems.Count, 0);

            Debug.Assert(idx < inItems.Count);

            buckets.TryAdd(idx, new List<T1>());
            buckets[idx].Add(item);
        }

        foreach (KeyValuePair<int, List<T1>> kv in buckets)
        {
            var entries = kv.Value;
            if (entries.Count <= 1)
            {
                continue;
            }

            uint j = 1;
            List<int> ids = new();

            while (true)
            {
                bool done = true;
                for (int k = 0; k < entries.Count; k++)
                {
                    int idx = getIndexFunc(entries[k], inItems.Count, j);
                    if (slots[idx] || ids.Contains(idx))
                    {
                        done = false;
                        break;
                    }
                    ids.Add(idx);
                }
                if (done)
                {
                    break;
                }

                j++;
                ids.Clear();
            }
            for (int k = 0; k < entries.Count; k++)
            {
                sorted[ids[k]] = entries[k];
                slots[ids[k]] = true;
            }
            hashmap[kv.Key] = (int)j;
        }

        foreach (KeyValuePair<int,List<T1>> kv in buckets)
        {
            if (kv.Value.Count == 1)
            {
                while (slots[l])
                {
                    l++;
                }

                hashmap[kv.Key] = -1 - l;
                sorted[l] = buckets[kv.Key][0];
                slots[l] = true;
            }
        }

        inItems = sorted;

        return hashmap;
    }

    public static List<int> CreateHashMapV2<T>(ref List<T> inItems, Func<T, int, uint, int> getIndexFunc)
    {
        int size = inItems.Count;

        var sortedItems = new T[size];

        // default value is -1, so it just returns the first element
        int[] hashMap = new int[size];
        for (int i = 0; i < size; i++)
        {
            hashMap[i] = -1;
        }

        // add all hashes
        List<T>[] hashDict = new List<T>[size];
        for (int i = 0; i < size; i++)
        {
            hashDict[i] = new List<T>();
        }
        foreach (var key in inItems)
        {
            hashDict[getIndexFunc(key, size, 0x811c9dc5)].Add(key);
        }

        // sort them so that the ones with the most duplicates are first
        Array.Sort(hashDict, (x, y) => y.Count.CompareTo(x.Count));

        bool[] used = new bool[size];

        List<int> indices;

        int hash = 0;
        // process hash conflicts
        for (; hash < size; hash++)
        {
            int duplicateCount = hashDict[hash].Count;

            if (hashDict[hash].Count <= 1)
            {
                break;
            }

            indices = new List<int>(duplicateCount);

            // find seed for which all hashes are unique and not already used
            uint seed = 1;
            int i = 0;
            while (i < duplicateCount)
            {
                int index = getIndexFunc(hashDict[hash][i], size, seed);
                if (used[index] || indices.FindIndex(k => k == index) != -1)
                {
                    seed++;
                    i = 0;
                    indices.Clear();
                }
                else
                {
                    indices.Add(index);
                    i++;
                }
            }

            // set seed as hashmap value
            hashMap[getIndexFunc(hashDict[hash][0], size, 0x811c9dc5) ] = (int)seed;

            // set output values and make indices used
            for (int j = 0; j < duplicateCount; j++)
            {
                int index = indices[j];
                sortedItems[index] = hashDict[hash][j];
                used[index] = true;
            }
        }

        // process unique hashes
        int idx = 0;
        for (; hash < size; hash++)
        {
            if (hashDict[hash].Count == 0)
            {
                break;
            }

            // find first free spot
            while (used[idx])
            {
                idx = (idx + 1) % size;
            }

            // set correct index for hash
            hashMap[getIndexFunc(hashDict[hash][0], size, 0x811c9dc5)] = (-1 * idx) - 1;
            sortedItems[idx] = hashDict[hash][0];

            // advance index
            idx = (idx + 1) % size;
        }

        inItems = sortedItems.ToList();

        return hashMap.ToList();
    }
}