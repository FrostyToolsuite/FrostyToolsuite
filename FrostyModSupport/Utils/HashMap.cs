namespace Frosty.ModSupport.Utils;

public static class HashMap
{
    private static uint HashData(ReadOnlySpan<byte> data, uint offset = 0x811c9dc5)
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
        return (int)(HashData(inData, inInitial) % inCount);
    }

    public static List<int> CreateHashMap<T>(ref List<T> inItems, Func<T, int, uint, int> getIndexFunc)
    {
        int size = inItems.Count;

        T[] sortedItems = new T[size];

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
        foreach (T key in inItems)
        {
            hashDict[getIndexFunc(key, size, 0x811c9dc5)].Add(key);
        }

        // sort them so that the ones with the most duplicates are first
        Array.Sort(hashDict, (x, y) => y.Count.CompareTo(x.Count));

        bool[] used = new bool[size];

        int hash = 0;
        // process hash conflicts
        for (; hash < size; hash++)
        {
            int duplicateCount = hashDict[hash].Count;

            if (hashDict[hash].Count <= 1)
            {
                break;
            }

            List<int> indices = new(duplicateCount);

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