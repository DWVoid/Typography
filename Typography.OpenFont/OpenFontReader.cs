//Apache2, 2017-present, WinterDev
//Apache2, 2014-2016, Samuel Carlsson, WinterDev

using System;
using System.IO;
using Typography.OpenFont.IO;
using Typography.OpenFont.Tables;
namespace Typography.OpenFont
{
    [Flags]
    public enum ReadFlags
    {
        Full = 0,
        Name = 1,
        Matrix = 1 << 2,
        AdvancedLayout = 1 << 3,
        Variation = 1 << 4
    }


    public class PreviewFontInfo
    {
        public readonly string Name;
        public readonly string SubFamilyName;
        public readonly Extensions.TranslatedOS2FontStyle OS2TranslatedStyle;
        public readonly ushort Weight;
        PreviewFontInfo[] _ttcfMembers;

        public PreviewFontInfo(string fontName, string fontSubFam, ushort weight,
            Extensions.TranslatedOS2FontStyle os2TranslatedStyle = Extensions.TranslatedOS2FontStyle.UNSET)
        {
            Name = fontName;
            SubFamilyName = fontSubFam;
            Weight = weight;
            OS2TranslatedStyle = os2TranslatedStyle;
        }
        public PreviewFontInfo(string fontName, PreviewFontInfo[] ttcfMembers)
        {
            Name = fontName;
            SubFamilyName = "";
            _ttcfMembers = ttcfMembers;
        }
        public int ActualStreamOffset { get; internal set; }
        public bool IsWebFont { get; internal set; }
        public bool IsFontCollection => _ttcfMembers != null;

        /// <summary>
        /// get font collection's member count
        /// </summary>
        public int MemberCount => _ttcfMembers.Length;
        /// <summary>
        /// get font collection's member
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public PreviewFontInfo GetMember(int index) => _ttcfMembers[index];
#if DEBUG
        public override string ToString()
        {
            return (IsFontCollection) ? Name : Name + ", " + SubFamilyName + ", " + OS2TranslatedStyle;
        }
#endif
    }


    static class KnownFontFiles
    {
        public static bool IsTtcf(ushort u1, ushort u2)
        {
            //https://docs.microsoft.com/en-us/typography/opentype/spec/otff#ttc-header
            //check if 1st 4 bytes is ttcf or not  
            return (((u1 >> 8) & 0xff) == (byte)'t') &&
                   (((u1) & 0xff) == (byte)'t') &&
                   (((u2 >> 8) & 0xff) == (byte)'c') &&
                   (((u2) & 0xff) == (byte)'f');
        }
        public static bool IsWoff(ushort u1, ushort u2)
        {
            return (((u1 >> 8) & 0xff) == (byte)'w') && //0x77
                  (((u1) & 0xff) == (byte)'O') && //0x4f 
                  (((u2 >> 8) & 0xff) == (byte)'F') && // 0x46
                  (((u2) & 0xff) == (byte)'F'); //0x46 
        }
        public static bool IsWoff2(ushort u1, ushort u2)
        {
            return (((u1 >> 8) & 0xff) == (byte)'w') &&//0x77
            (((u1) & 0xff) == (byte)'O') &&  //0x4f 
            (((u2 >> 8) & 0xff) == (byte)'F') && //0x46
            (((u2) & 0xff) == (byte)'2'); //0x32 
        }
    }





    public class OpenFontReader
    {
        class FontCollectionHeader
        {
            public ushort majorVersion;
            public ushort minorVersion;
            public uint numFonts;
            public int[] offsetTables;
            //
            //if version 2
            public uint dsigTag;
            public uint dsigLength;
            public uint dsigOffset;
        }

        static string BuildTtcfName(PreviewFontInfo[] members)
        {
            //THIS IS MY CONVENTION for TrueType collection font name
            //you can change this to fit your need.

            var stbuilder = new System.Text.StringBuilder();
            stbuilder.Append("TTCF: " + members.Length);
            var uniqueNames = new System.Collections.Generic.Dictionary<string, bool>();
            for (uint i = 0; i < members.Length; ++i)
            {
                var member = members[i];
                if (!uniqueNames.ContainsKey(member.Name))
                {
                    uniqueNames.Add(member.Name, true);
                    stbuilder.Append("," + member.Name);
                }
            }
            return stbuilder.ToString();
        }


        /// <summary>
        /// read only name entry
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public PreviewFontInfo ReadPreview(Stream stream)
        {
            //var little = BitConverter.IsLittleEndian;
            using (var input = new ByteOrderSwappingBinaryReader(stream))
            {
                var majorVersion = input.ReadUInt16();
                var minorVersion = input.ReadUInt16();

                if (KnownFontFiles.IsTtcf(majorVersion, minorVersion))
                {
                    //this font stream is 'The Font Collection'
                    var ttcHeader = ReadTTCHeader(input);
                    var members = new PreviewFontInfo[ttcHeader.numFonts];
                    for (uint i = 0; i < ttcHeader.numFonts; ++i)
                    {
                        input.BaseStream.Seek(ttcHeader.offsetTables[i], SeekOrigin.Begin);
                        var member = members[i] = ReadActualFontPreview(input, false);
                        member.ActualStreamOffset = ttcHeader.offsetTables[i];
                    }
                    return new PreviewFontInfo(BuildTtcfName(members), members);
                }
                else
                {
                    return ReadActualFontPreview(input, true);//skip version data (majorVersion, minorVersion)
                }
            }
        }
        FontCollectionHeader ReadTTCHeader(ByteOrderSwappingBinaryReader input)
        {
            //https://docs.microsoft.com/en-us/typography/opentype/spec/otff#ttc-header
            //TTC Header Version 1.0:
            //Type 	    Name 	        Description
            //TAG 	    ttcTag 	        Font Collection ID string: 'ttcf' (used for fonts with CFF or CFF2 outlines as well as TrueType outlines)
            //uint16 	majorVersion 	Major version of the TTC Header, = 1.
            //uint16 	minorVersion 	Minor version of the TTC Header, = 0.
            //uint32 	numFonts 	    Number of fonts in TTC
            //Offset32 	offsetTable[numFonts] 	Array of offsets to the OffsetTable for each font from the beginning of the file

            //TTC Header Version 2.0:
            //Type 	    Name 	        Description
            //TAG 	    ttcTag 	        Font Collection ID string: 'ttcf'
            //uint16 	majorVersion 	Major version of the TTC Header, = 2.
            //uint16 	minorVersion 	Minor version of the TTC Header, = 0.
            //uint32 	numFonts 	    Number of fonts in TTC
            //Offset32 	offsetTable[numFonts] 	Array of offsets to the OffsetTable for each font from the beginning of the file
            //uint32 	dsigTag 	    Tag indicating that a DSIG table exists, 0x44534947 ('DSIG') (null if no signature)
            //uint32 	dsigLength 	    The length (in bytes) of the DSIG table (null if no signature)
            //uint32 	dsigOffset 	    The offset (in bytes) of the DSIG table from the beginning of the TTC file (null if no signature)

            var ttcHeader = new FontCollectionHeader();

            ttcHeader.majorVersion = input.ReadUInt16();
            ttcHeader.minorVersion = input.ReadUInt16();
            var numFonts = input.ReadUInt32();
            var offsetTables = new int[numFonts];
            for (uint i = 0; i < numFonts; ++i)
            {
                offsetTables[i] = input.ReadInt32();
            }

            ttcHeader.numFonts = numFonts;
            ttcHeader.offsetTables = offsetTables;
            //
            if (ttcHeader.majorVersion == 2)
            {
                ttcHeader.dsigTag = input.ReadUInt32();
                ttcHeader.dsigLength = input.ReadUInt32();
                ttcHeader.dsigOffset = input.ReadUInt32();

                if (ttcHeader.dsigTag == 0x44534947)
                {
                    //Tag indicating that a DSIG table exists
                    //TODO: goto DSIG add read signature
                }
            }
            return ttcHeader;
        }
        PreviewFontInfo ReadActualFontPreview(ByteOrderSwappingBinaryReader input, bool skipVersionData)
        {
            if (!skipVersionData)
            {
                var majorVersion = input.ReadUInt16();
                var minorVersion = input.ReadUInt16();
            }

            var tableCount = input.ReadUInt16();
            var searchRange = input.ReadUInt16();
            var entrySelector = input.ReadUInt16();
            var rangeShift = input.ReadUInt16();

            var tables = new TableEntryCollection();
            for (var i = 0; i < tableCount; i++)
            {
                tables.AddEntry(new UnreadTableEntry(ReadTableHeader(input)));
            }
            return ReadPreviewFontInfo(tables, input);
        }
        public Typeface Read(Stream stream, int streamStartOffset = 0, ReadFlags readFlags = ReadFlags.Full)
        {
            //bool little = BitConverter.IsLittleEndian; 

            if (streamStartOffset > 0)
            {
                //eg. for ttc
                stream.Seek(streamStartOffset, SeekOrigin.Begin);
            }
            using (var input = new ByteOrderSwappingBinaryReader(stream))
            {
                var majorVersion = input.ReadUInt16();
                var minorVersion = input.ReadUInt16();

                if (KnownFontFiles.IsTtcf(majorVersion, minorVersion))
                {
                    //this font stream is 'The Font Collection'                    
                    //To read content of ttc=> one must specific the offset
                    //so use read preview first=> you will know that what are inside the ttc.                    

                    return null;
                }
                //-----------------------------------------------------------------


                var tableCount = input.ReadUInt16();
                var searchRange = input.ReadUInt16();
                var entrySelector = input.ReadUInt16();
                var rangeShift = input.ReadUInt16();
                //------------------------------------------------------------------ 
                var tables = new TableEntryCollection();
                for (var i = 0; i < tableCount; i++)
                {
                    tables.AddEntry(new UnreadTableEntry(ReadTableHeader(input)));
                }
                //------------------------------------------------------------------ 
                return ReadTableEntryCollection(tables, input);
            }
        }

        internal PreviewFontInfo ReadPreviewFontInfo(TableEntryCollection tables, BinaryReader input)
        {
            var nameEntry = ReadTableIfExists(tables, input, new NameEntry());
            var os2Table = ReadTableIfExists(tables, input, new OS2Table());

            return new PreviewFontInfo(
                nameEntry.FontName,
                nameEntry.FontSubFamily,
                os2Table.usWeightClass,
                Extensions.TypefaceExtensions.TranslatedOS2FontStyle(os2Table)
                );
        }
        internal Typeface ReadTableEntryCollection(TableEntryCollection tables, BinaryReader input)
        {

            var os2Table = ReadTableIfExists(tables, input, new OS2Table());
            var nameEntry = ReadTableIfExists(tables, input, new NameEntry());

            var header = ReadTableIfExists(tables, input, new Head());
            var maximumProfile = ReadTableIfExists(tables, input, new MaxProfile());
            var horizontalHeader = ReadTableIfExists(tables, input, new HorizontalHeader());
            var horizontalMetrics = ReadTableIfExists(tables, input, new HorizontalMetrics(horizontalHeader.HorizontalMetricsCount, maximumProfile.GlyphCount));

            //---
            var postTable = ReadTableIfExists(tables, input, new PostTable());
            var ccf = ReadTableIfExists(tables, input, new CFFTable());

            //--------------
            var cmaps = ReadTableIfExists(tables, input, new Cmap());
            var glyphLocations = ReadTableIfExists(tables, input, new GlyphLocations(maximumProfile.GlyphCount, header.WideGlyphLocations));

            var glyf = ReadTableIfExists(tables, input, new Glyf(glyphLocations));
            //--------------
            var gaspTable = ReadTableIfExists(tables, input, new Gasp());
            var vdmx = ReadTableIfExists(tables, input, new VerticalDeviceMetrics());
            //--------------


            var kern = ReadTableIfExists(tables, input, new Kern());
            //--------------
            //advanced typography
            var gdef = ReadTableIfExists(tables, input, new GDEF());
            var gsub = ReadTableIfExists(tables, input, new GSUB());
            var gpos = ReadTableIfExists(tables, input, new GPOS());
            var baseTable = ReadTableIfExists(tables, input, new BASE());
            var colr = ReadTableIfExists(tables, input, new COLR());
            var cpal = ReadTableIfExists(tables, input, new CPAL());
            var vhea = ReadTableIfExists(tables, input, new VerticalHeader());
            if (vhea != null)
            {
                var vmtx = ReadTableIfExists(tables, input, new VerticalMetrics(vhea.NumOfLongVerMetrics));
            }



            //test math table
            var mathtable = ReadTableIfExists(tables, input, new MathTable());
            var fontBmpTable = ReadTableIfExists(tables, input, new EBLCTable());
            //---------------------------------------------
            //about truetype instruction init 

            //--------------------------------------------- 
            Typeface typeface = null;
            var isPostScriptOutline = false;
            if (glyf == null)
            {
                //check if this is cff table ?
                if (ccf == null)
                {
                    //TODO: review here
                    throw new NotSupportedException();
                }
                //...  
                //PostScript outline font 
                isPostScriptOutline = true;
                typeface = new Typeface(
                      nameEntry,
                      header.Bounds,
                      header.UnitsPerEm,
                      ccf,
                      horizontalMetrics,
                      os2Table);


            }
            else
            {
                typeface = new Typeface(
                    nameEntry,
                    header.Bounds,
                    header.UnitsPerEm,
                    glyf.Glyphs,
                    horizontalMetrics,
                    os2Table);
            }

            //----------------------------
            typeface.CmapTable = cmaps;
            typeface.KernTable = kern;
            typeface.GaspTable = gaspTable;
            typeface.MaxProfile = maximumProfile;
            typeface.HheaTable = horizontalHeader;
            //----------------------------

            if (!isPostScriptOutline)
            {
                var fpgmTable = ReadTableIfExists(tables, input, new FpgmTable());
                //control values table
                var cvtTable = ReadTableIfExists(tables, input, new CvtTable());
                if (cvtTable != null)
                {
                    typeface.ControlValues = cvtTable._controlValues;
                }
                if (fpgmTable != null)
                {
                    typeface.FpgmProgramBuffer = fpgmTable._programBuffer;
                }
                var propProgramTable = ReadTableIfExists(tables, input, new PrepTable());
                if (propProgramTable != null)
                {
                    typeface.PrepProgramBuffer = propProgramTable._programBuffer;
                }
            }
            //-------------------------
            typeface.LoadOpenFontLayoutInfo(
                gdef,
                gsub,
                gpos,
                baseTable,
                colr,
                cpal);

            //------------


            //test
            {
                var svgTable = ReadTableIfExists(tables, input, new SvgTable());
                if (svgTable != null)
                {
                    typeface._svgTable = svgTable;
                }
            }

            typeface.PostTable = postTable;
            if (mathtable != null)
            {
                var mathGlyphLoader = new MathGlyphLoader();
                mathGlyphLoader.LoadMathGlyph(typeface, mathtable);

            }
#if DEBUG
            //test
            //int found = typeface.GetGlyphIndexByName("Uacute");
            if (typeface.IsCffFont)
            {
                //optional
                typeface.UpdateAllCffGlyphBounds();
            }
#endif
            return typeface;
        }


        static TableHeader ReadTableHeader(BinaryReader input)
        {
            return new TableHeader(
                input.ReadUInt32(),
                input.ReadUInt32(),
                input.ReadUInt32(),
                input.ReadUInt32());
        }
        static T ReadTableIfExists<T>(TableEntryCollection tables, BinaryReader reader, T resultTable)
            where T : TableEntry
        {

            TableEntry found;
            if (tables.TryGetTable(resultTable.Name, out found))
            {
                //found table name
                //check if we have read this table or not
                if (found is UnreadTableEntry)
                {
                    var unreadTableEntry = found as UnreadTableEntry;

                    //set header before actal read
                    resultTable.Header = found.Header;
                    if (unreadTableEntry.HasCustomContentReader)
                    {
                        resultTable = unreadTableEntry.CreateTableEntry(reader, resultTable);
                    }
                    else
                    {
                        resultTable.LoadDataFrom(reader);
                    }
                    //then replace
                    tables.ReplaceTable(resultTable);
                    return resultTable;
                }
                else
                {
                    //we have read this table
                    throw new NotSupportedException();
                }
            }
            //not found
            return null;
        }


    }

}
