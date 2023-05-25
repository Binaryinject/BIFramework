﻿/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// http://psdplugin.codeplex.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2013 Tao Yue
//
// See PsdReader/LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

namespace PluginMaster
{
    public class LayerUnicodeName : LayerInfo
    {
        public override string Key
        {
            get { return "luni"; }
        }

        public string Name { get; set; }

        public LayerUnicodeName(string name)
        {
            Name = name;
        }

        public LayerUnicodeName(PsdBinaryReader reader)
        {
            Name = reader.ReadUnicodeString();
        }
    }
}
