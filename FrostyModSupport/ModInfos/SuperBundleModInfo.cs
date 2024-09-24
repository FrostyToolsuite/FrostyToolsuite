﻿using System.Collections.Generic;
using Frosty.Sdk;

namespace Frosty.ModSupport.ModInfos;

public class SuperBundleModInfo
{
    public SuperBundleModAction Added = new();
    public SuperBundleModAction Removed = new();
    public SuperBundleModAction Modified = new();
    public HashSet<Sha1> Data = new();
}