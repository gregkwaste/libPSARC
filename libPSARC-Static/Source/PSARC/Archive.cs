﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using libPSARC.Interop;

namespace libPSARC.PSARC {

    public class InvalidArchiveException : Exception {
        public InvalidArchiveException() : base( "Invalid archive." ) { }
    }

    public class Archive {

        public Header header;

        public FileList fileEntries;

        public BlockSizeList blockSizes;

        public List<string> filePaths;

        #region // Methods

        public Archive() {
            this.header = new Header();
            this.fileEntries = null;
            this.blockSizes = null;
            this.filePaths = null;
        }

        [Conditional( "DEBUG" )]
        private static void DebugLog( object obj ) => Debug.WriteLine( obj );

        [Conditional( "DEBUG" )]
        private static void DebugLogPosition( long position ) => DebugLog( String.Format( "\n0x{0:X8} ({0})\n", position ) );

        [Conditional( "DEBUG" )]
        private static void DebugLogFileEntries( FileList fileEntries, int count ) {
            count = Math.Min( fileEntries.Length, count );
            for ( int i = 0; i < count; i++ ) DebugLog( fileEntries[i].ToString( $"{typeof( FileEntry )}[{i}]" ) );
        }

        [Conditional( "DEBUG" )]
        private static void DebugLogBlockSizes( BlockSizeList blockSizes, int count ) {
            count = Math.Min( blockSizes.Length, count );
            for ( int i = 0; i < count; i++ ) DebugLog( $"blockSizes[{i}] = {blockSizes[i]}" );
        }

        public Archive( Stream streamIn ) {
            if ( streamIn.Length < Marshal.SizeOf<Header>() ) throw new InvalidArchiveException();

            header = new Header( streamIn );
            DebugLog( header );

            string magic = new string( header.magic );
            if ( magic != Header.MAGIC ) throw new InvalidArchiveException();

            DebugLogPosition( streamIn.Position );

            fileEntries = new FileList( streamIn, header.numFiles );
            DebugLogFileEntries( fileEntries, 5 );

            DebugLogPosition( streamIn.Position );

            int length = (int) (header.dataOffset - streamIn.Position);
            length -= (header.flags.HasFlag( ArchiveFlags.Encrypted ) ? 0x20 : 0x00);

            blockSizes = new BlockSizeList( streamIn, length, header.maxBlockSize );
            DebugLogBlockSizes( blockSizes, 5 );

            DebugLogPosition( streamIn.Position );

            filePaths = new List<string>();
            using ( var reader = new StreamReader( ExtractFile( streamIn, fileEntries[0] ) ) ) {
                while ( !reader.EndOfStream ) filePaths.Add( reader.ReadLine() );
            }
        }

        public int GetFileIndex( string filePath ) {
            return (filePaths.Contains( filePath )) ? filePaths.IndexOf( filePath ) + 1 : -1;
        }

        public Stream ExtractFile( Stream streamIn, string filePath, Stream streamOut = null ) {
            return ExtractFile( streamIn, GetFileIndex( filePath ), streamOut );
        }

        public Stream ExtractFile( Stream streamIn, int fileIndex, Stream streamOut = null ) {
            return ExtractFile( streamIn, fileEntries[fileIndex], streamOut );
        }

        public Stream ExtractFile( Stream streamIn, FileEntry fileEntry, Stream streamOut = null) {
            var buffer = new byte[header.maxBlockSize];

            int index = fileEntry.blockIndex;
            long size = (long) fileEntry.fileSize.Value;
            long total = 0;

            streamIn.Seek( (long) fileEntry.dataOffset.Value, SeekOrigin.Begin );

            streamOut = streamOut ?? new MemoryStream( (int) (UInt64) fileEntry.fileSize );
            long startPosition = streamOut.Position;

            // loop until all blocks have been read
            while (total < size) {
                uint blockSize = blockSizes[index];
                streamIn.Read( buffer, 0, (int) blockSize );
                var zOut = new zlib.ZOutputStream( streamOut );
                zOut.Write( buffer, 0, (int) blockSize );
                zOut.Flush();
                total += zOut.TotalOut;
                index++;
            }

            streamOut.Flush();
            streamOut.Position = startPosition;
            return streamOut;
        }

        public static bool IsValid( Stream streamIn ) => Header.IsValid( streamIn );

        #endregion
    }

}
