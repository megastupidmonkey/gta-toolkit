﻿/*
    Copyright(c) 2016 Neodymium

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
*/

using RageLib.Data;
using RageLib.GTA5.ResourceWrappers.PC.Meta.Types;
using RageLib.Resources.Common;
using RageLib.Resources.GTA5;
using RageLib.Resources.GTA5.PC.Meta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RageLib.GTA5.ResourceWrappers.PC.Meta
{
    public class MetaReader
    {

        public IMetaValue Read(string fileName)
        {
            using (var fileStream = new FileStream(fileName, FileMode.Open))
            {
                return Read(fileStream);
            }
        }

        public IMetaValue Read(Stream fileStream)
        {
            var resource = new ResourceFile_GTA5_pc<Meta_GTA5_pc>();
            resource.Load(fileStream);
            return Parse(resource.ResourceData);
        }

        private IMetaValue Parse(Meta_GTA5_pc meta)
        {
            List<uint> blockKeys = new List<uint>();
            List<List<IMetaValue>> blocks = new List<List<IMetaValue>>();

            //////////////////////////////////////////////////
            // first step: flat conversion
            //////////////////////////////////////////////////

            foreach (var block in meta.DataBlocks)
            {
                blockKeys.Add(block.StructureKey);
                switch (block.StructureKey)
                {
                    case 0x00000007:
                        blocks.Add(ReadBlock(block, () => new MetaGeneric()));
                        break;
                    case 0x00000010:
                        blocks.Add(ReadBlock(block, () => new MetaByte_A()));
                        break;
                    case 0x00000011:
                        blocks.Add(ReadBlock(block, () => new MetaByte_B()));
                        break;
                    case 0x00000013:
                        blocks.Add(ReadBlock(block, () => new MetaInt16_B()));
                        break;
                    case 0x00000015:
                        blocks.Add(ReadBlock(block, () => new MetaInt32_B()));
                        break;
                    case 0x00000021:
                        blocks.Add(ReadBlock(block, () => new MetaFloat()));
                        break;
                    case 0x00000033:
                        blocks.Add(ReadBlock(block, () => new MetaFloat4_XYZ()));
                        break;
                    case 0x0000004A:
                        blocks.Add(ReadBlock(block, () => new MetaInt32_Hash()));
                        break;
                    default:
                        blocks.Add(ReadBlock(block, () => new MetaStructure(meta, GetInfo(meta, block.StructureKey))));
                        break;
                }
            }

            //////////////////////////////////////////////////
            // second step: map references
            //////////////////////////////////////////////////

            var referenced = new HashSet<IMetaValue>();
            var stack = new Stack<IMetaValue>();
            foreach (var block in blocks)
            {
                foreach (var entry in block)
                {
                    stack.Push(entry);
                }
            }
            while (stack.Count > 0)
            {
                var entry = stack.Pop();
                if (entry is MetaArray)
                {
                    var arrayEntry = entry as MetaArray;
                    arrayEntry.Entries = new List<IMetaValue>();
                    var realBlockIndex = arrayEntry.BlockIndex - 1;
                    if (realBlockIndex >= 0)
                    {
                        var realEntryIndex = arrayEntry.Offset / GetSize(meta, blockKeys[realBlockIndex]);
                        for (int i = 0; i < arrayEntry.NumberOfEntries; i++)
                        {
                            var x = blocks[realBlockIndex][realEntryIndex + i];
                            arrayEntry.Entries.Add(x);
                            referenced.Add(x);
                        }
                    }
                }
                if (entry is MetaCharPointer)
                {
                    var charPointerEntry = entry as MetaCharPointer;
                    string value = "";
                    for (int i = 0; i < charPointerEntry.Length; i++)
                    {
                        var x = (MetaByte_A)blocks[charPointerEntry.DataBlockIndex - 1][i + charPointerEntry.DataOffset];
                        value += (char)x.Value;
                    }
                    charPointerEntry.Value = value;
                }
                if (entry is MetaGeneric)
                {
                    var genericEntry = entry as MetaGeneric;
                    var realBlockIndex = genericEntry.BlockIndex - 1;

                    if (realBlockIndex < 0)
                    { }

                    var realEntryIndex = genericEntry.Offset * 16 / GetSize(meta, blockKeys[realBlockIndex]);
                    var x = blocks[realBlockIndex][realEntryIndex];
                    genericEntry.Value = x;
                    referenced.Add(x);
                }
                if (entry is MetaStructure)
                {
                    var structureEntry = entry as MetaStructure;
                    foreach (var x in structureEntry.Values)
                    {
                        stack.Push(x.Value);
                    }
                }
            }

            //////////////////////////////////////////////////
            // second step: find root
            //////////////////////////////////////////////////

            var rootSet = new HashSet<IMetaValue>();
            foreach (var x in blocks)
            {
                foreach (var y in x)
                {
                    if (y is MetaStructure && !referenced.Contains(y))
                    {
                        rootSet.Add(y);
                    }
                }
            }

            return rootSet.First();
        }

        private List<IMetaValue> ReadBlock(DataBlock_GTA5_pc block, CreateMetaValueDelegate CreateMetaValue)
        {
            var result = new List<IMetaValue>();
            var reader = new DataReader(new MemoryStream(ToBytes(block.Data)));
            while (reader.Position < reader.Length)
            {
                var value = CreateMetaValue();
                value.Read(reader);
                result.Add(value);
            }
            return result;
        }

        private byte[] ToBytes(ResourceSimpleArray<byte_r> data)
        {
            var result = new byte[data.Count];
            for (int i = 0; i < data.Count; i++)
                result[i] = data[i].Value;
            return result;
        }

        public static StructureInfo_GTA5_pc GetInfo(Meta_GTA5_pc meta, uint structureKey)
        {
            StructureInfo_GTA5_pc info = null;
            foreach (var x in meta.StructureInfos)
                if (x.StructureKey == structureKey)
                    info = x;
            return info;
        }

        public int GetSize(Meta_GTA5_pc meta, uint typeKey)
        {
            switch (typeKey)
            {
                case 0x00000007:
                    return 8;
                case 0x00000010:
                    return 1;
                case 0x00000011:
                    return 1;
                case 0x00000013:
                    return 2;
                case 0x00000015:
                    return 4;
                case 0x00000021:
                    return 4;
                case 0x00000033:
                    return 16;
                case 0x0000004A:
                    return 4;
                default:
                    return (int)GetInfo(meta, typeKey).StructureLength;
            }
        }
    }
}