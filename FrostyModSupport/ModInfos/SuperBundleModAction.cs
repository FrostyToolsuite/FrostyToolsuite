﻿using System;
using System.Collections.Generic;

namespace Frosty.ModSupport.ModInfos;

public class SuperBundleModAction
{
    public Dictionary<int, BundleModInfo> Bundles = new();
    public HashSet<Guid> Chunks = new();
}