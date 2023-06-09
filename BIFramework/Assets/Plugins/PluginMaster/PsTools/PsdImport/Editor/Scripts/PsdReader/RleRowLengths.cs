/////////////////////////////////////////////////////////////////////////////////
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

using System.Linq;

namespace PluginMaster
{
    public class RleRowLengths
    {
        public int[] Values { get; private set; }

        public int Total
        {
            get { return Values.Sum(); }
        }

        public int this[int i]
        {
            get { return Values[i]; }
            set { Values[i] = value; }
        }

        public RleRowLengths(int rowCount)
        {
            Values = new int[rowCount];
        }

        public RleRowLengths(PsdBinaryReader reader, int rowCount)
          : this(rowCount)
        {
            for (int i = 0; i < rowCount; i++)
            {
                Values[i] = reader.ReadUInt16();
            }
        }
    }

}
