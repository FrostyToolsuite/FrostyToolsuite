﻿namespace Frosty.Sdk.IO.PartitionEbx;

internal enum EbxVersion
{
    Version2 = 0x0FB2D1CE,
    Version4 = 0x0FB4D1CE,
    Version6 = 0x46464952 // RIFF LE
}