﻿/////////////////////////////////////////////////////////////////////////////////
//
// Photoshop PSD FileType Plugin for Paint.NET
// https://www.psdplugin.com/
//
// This software is provided under the MIT License:
//   Copyright (c) 2006-2007 Frank Blumenberg
//   Copyright (c) 2010-2013 Tao Yue
//
// See PsdReader/LICENSE.txt for complete licensing and attribution information.
//
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;

namespace PluginMaster
{
    /// <summary>
    /// The names of the alpha channels
    /// </summary>
    public class AlphaChannelNames : ImageResource
    {
        public override ResourceID ID
        {
            get { return ResourceID.AlphaChannelNames; }
        }

        private List<string> channelNames = new();
        public List<string> ChannelNames
        {
            get { return channelNames; }
        }

        public AlphaChannelNames() : base(String.Empty)
        {
        }

        public AlphaChannelNames(PsdBinaryReader reader, string name, int resourceDataLength)
          : base(name)
        {
            var endPosition = reader.BaseStream.Position + resourceDataLength;

            // Alpha channel names are Pascal strings, with no padding in-between.
            while (reader.BaseStream.Position < endPosition)
            {
                var channelName = reader.ReadPascalString(1);
                ChannelNames.Add(channelName);
            }
        }
    }
}
