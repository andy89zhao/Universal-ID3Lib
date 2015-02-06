using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ID3Library
{
    // for the version ID3v2.4.0
    public enum TextEncoding
    {
        Ascii = 0,          // [ISO-8859-1] $20 - $FF terminated with $00
        Utf16Bom = 1,       // Byter Order Mark must be present, terminated with $00 00
        Utf16Be = 2,        // Big endian, no BOM, terminated with $00 00
        Utf8 = 3            // Utf-8, terminated with $00
    }

    public class ID3
    {
        const int HEADER_SIZE = 10;

        private List<Frame> _frames = new List<Frame>();    // sequential list of frames

        private TagHeader _tagHeader = null;        // ID3v2 tag header
        StorageFile _file = null;                   // current file to read/write tags

        bool _supported = true;                 // set to false if the version not supported

        public ID3() { }

        public bool IsSupported()
        {
            return _supported;
        }

        #region public properties

        #region text properties

        public string Album
        {
            get { return GetTextValue("TALB"); }
            set { SetTextValue("TALB", value); }
        }

        public string Composer
        {
            get { return GetTextValue("TCOM"); }
            set { SetTextValue("TCOM", value); }
        }

        // Content type/Genre, e.g. "Bollywood Music" or something cryptic "(0)"
        public string Genre
        {
            get { return GetTextValue("TCON"); }
            set { SetTextValue("TCON", value); }
        }

        // for the writer of the text or lyrics in the recording
        public string Lyricist
        {
            get { return GetTextValue("TEXT"); }
            set { SetTextValue("TEXT", value); }
        }

        // actual name
        public string Title
        {
            get { return GetTextValue("TIT2"); }
            set { SetTextValue("TIT2", value); }
        }

        // subtitle or description
        public string Subtitle
        {
            get { return GetTextValue("TIT3"); }
            set { SetTextValue("TIT3", value); }
        }

        // original artist (in case of cover of a previously released song)
        public string OriginalArtist
        {
            get { return GetTextValue("TOPE"); }
            set { SetTextValue("TOPE", value); }
        }

        // lead artist(s) or performer(s)
        // confirmed with WP API
        public string Artist
        {
            get { return GetTextValue("TPE1"); }
            set { SetTextValue("TPE1", value); }
        }

        // confirmed with WP API
        // all the artists (contributors)
        public string AlbumArtist
        {
            get { return GetTextValue("TPE2"); }
            set { SetTextValue("TPE2", value); }
        }

        // track number e.g. "4/9"
        // Numeric string as ASCII
        public string Track
        {
            get { return GetTextValue("TRCK"); }
            set { SetTextValue("TRCK", value, TextEncoding.Ascii); }
        }

        // Note: stored as numeric string (store in ASCII, otherwise other players may not recognize it)
        public string Year
        {
            get { return GetTextValue("TYER"); }
            set { SetTextValue("TYER", value, TextEncoding.Ascii); }
        }

        private string GetTextValue(string frameId)
        {
            Frame frame = GetFrame(frameId);
            if (frame == null)
                return String.Empty;
            return frame.GetTextValue();
        }

        private void SetTextValue(string frameId, string value, TextEncoding textEncoding = TextEncoding.Utf16Bom)
        {
            value = value.Trim();       // TRIM IT
            if(String.IsNullOrWhiteSpace(value))
            {
                Frame existingFrame = GetFrame(frameId);
                if (existingFrame != null)
                    _frames.Remove(existingFrame);
            }
            else
            {
                Frame frame = GetOrCreateFrame(frameId);
                // frame.SetTextValue(value, textEncoding);
                frame.SetTextValue(value);
            }
        }

        #endregion

        #region Rating (Popularity)
        /*
        * POPM
        *  Email to user   <text string> $00    (ASCII)
        *  Rating          $xx
        *  Counter         $xx xx xx xx (xx ...)
        */
        // 0 to 5
        public int Rating
        {
            get
            {
                Frame frame = GetFrame("POPM");
                if (frame == null)
                    return 0;

                int rating = 0;

                try
                {
                    byte popularity = GetPopularity(frame);
                    rating = GetRatingFromPopularity(popularity);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception: " + ex.Message);
                }
                
                return rating;
            }
            set
            {
                byte popularity = GetPopularityFromRating(value);
                Frame frame = GetOrCreateFrame("POPM");
                string email = "Sangeet Manager";
                byte[] emailBytes = Encoding.UTF8.GetBytes(email);
                frame.Data = new byte[emailBytes.Length + 1 + 1];   // 1 for \0
                Array.Copy(emailBytes, frame.Data, emailBytes.Length);
                frame.Data[emailBytes.Length] = 0;  // null terminated
                frame.Data[emailBytes.Length + 1] = popularity;  // popularity
            }
        }

        private static byte GetPopularity(Frame frame)
        {
            byte[] data = frame.Data;
            string email = Functions.GetString(data, 0, data.Length - 1, TextEncoding.Ascii); // 1 byte for rating
            byte popularity = data[email.Length + 1];

            int bytesForCounter = data.Length - email.Length - 1 - 1;
            if (bytesForCounter > 0)
            {
                Debug.WriteLine("bytes for counter = " + bytesForCounter);
            }
            return popularity;
            // counter ignored
        }

        // convert from 0-255 to 0-5
        private static int GetRatingFromPopularity(byte popularity)
        {
            if (popularity == 0)
                return 0;
            return (popularity - 1) / 51 + 1;
        }

        private static byte GetPopularityFromRating(int rating)
        {
            byte popularity = 0;
            switch (rating)
            {
                case 1:
                    popularity = 1;
                    break;

                case 2:
                    popularity = 64;
                    break;

                case 3:
                    popularity = 128;
                    break;

                case 4:
                    popularity = 196;
                    break;

                case 5:
                    popularity = 255;
                    break;
                default:
                    popularity = 0;
                    break;
            }
            return popularity;
        }

        #endregion

        #region Album Art

        /*
         * Text encoding      $xx     
         * MIME type          <text string> $00     
         * Picture type       $xx     
         * Description        <text string according to encoding> $00 (00)     
         * Picture data       <binary data>
         */
        public async Task<BitmapImage> GetAlbumArtAsync()
        {
            BitmapImage image = null;

            Frame frame = GetFrame("APIC");
            if (frame == null)
                return image;

            try
            {
                byte[] data = frame.Data;

                int i = 0;
                byte textEncodingByte = data[i];
                TextEncoding textEncoding = (TextEncoding)textEncodingByte;
                i++;

                string mimeType = Functions.GetString(data, i, data.Length - i, TextEncoding.Ascii);    // always ASCII
                i += mimeType.Length + 1;

                byte pictureType = data[i];
                if (pictureType != 3)
                    Debug.WriteLine("picture type: " + pictureType);
                i++;

                // string description = Functions.GetString(data, i, data.Length - i, textEncoding);
                // Debug.WriteLine("Description: " + description);
                // unicode strings may contain BOM
                i += Functions.GetStringBytesCount(data, i, data.Length - i, (TextEncoding)textEncoding);

                if (i == data.Length)    // no picture data
                {
                    return null;
                }

                using (InMemoryRandomAccessStream ms = new InMemoryRandomAccessStream())
                {
                    // Writes the image byte array in an InMemoryRandomAccessStream
                    // that is needed to set the source of BitmapImage.
                    IBuffer buffer = data.AsBuffer(i, data.Length - i);     // copy the remaining array in a buffer
                    await ms.WriteAsync(buffer);
                    ms.Seek(0);

                    image = new BitmapImage();
                    await image.SetSourceAsync(ms);
                }
            }
            catch (Exception)
            {
                image = null;
            }
            return image;
        }

        public async Task SetThumbnailAsync(StorageFile file)
        {
            if (file == null)
                return;

            string mimeType = file.ContentType;
            Stream stream = await file.OpenStreamForReadAsync();
            await SetThumbnailAsync(stream, mimeType);
        }

        /*
         * Text encoding      $xx     
         * MIME type          <text string> $00     
         * Picture type       $xx     
         * Description        <text string according to encoding> $00 (00)     
         * Picture data       <binary data>
         */
        public async Task SetThumbnailAsync(Stream stream, string mimeType = "image/jpeg")
        {
            if(stream == null)
            {
                Debug.WriteLine("stream is null");
                return;
            }

            Frame frame = GetOrCreateFrame("APIC");
            
            byte[] mimeTypeBytes = Encoding.UTF8.GetBytes(mimeType);
            string description = "Album Art";
            byte pictureType = 3;
            byte textEncoding = (byte)TextEncoding.Utf16Bom;
            byte[] descriptionBytes = Encoding.Unicode.GetBytes(description);

            byte[] metaDataBytes = new byte[1 + mimeTypeBytes.Length + 1 + 1 + descriptionBytes.Length + 2];
            int index = 0;
            metaDataBytes[index] = textEncoding;
            index++;

            // mime type
            Array.Copy(mimeTypeBytes, 0, metaDataBytes, index, mimeTypeBytes.Length);
            index += mimeTypeBytes.Length + 1;
            Debug.Assert(metaDataBytes[index - 1] == 0);

            // picture type
            metaDataBytes[index] = pictureType;
            index++;

            // description
            Array.Copy(descriptionBytes, 0, metaDataBytes, index, descriptionBytes.Length);
            index += descriptionBytes.Length + 2;

            byte[] frameData = new byte[metaDataBytes.Length + stream.Length];
            Array.Copy(metaDataBytes, frameData, metaDataBytes.Length);

            // read complete file
            int bytesRead = await stream.ReadAsync(frameData, metaDataBytes.Length, (int)stream.Length);
            Debug.Assert(bytesRead == stream.Length);

            frame.Data = frameData;
        }

        private Frame GetFrame(string frameId)
        {
            return _frames.Where(x => x.Id == frameId).FirstOrDefault();
        }

        /// <summary>
        /// Returns an existing frame or creates one
        /// </summary>
        /// <param name="frameId"></param>
        /// <returns>Returns an existing frame or creates one</returns>
        private Frame GetOrCreateFrame(string frameId)
        {
            Frame frame = GetFrame(frameId);
            if (frame == null)
            {
                frame = new Frame(frameId);
                _frames.Add(frame);     // Add it to the list, it should be written
            }
            return frame;
        }

        #endregion Album Art

        #endregion

        #region public functions

        /*
         * Used while displaying on mainpage, editing tags, or displaying all songs list
         */
        public async Task GetMusicPropertiesAsync(StorageFile file)
        {
            _file = file;
            if (file.FileType.ToLower() != ".mp3")
            {
                // not supported
                return;
            }

            try
            {
                IInputStream inputStream = await file.OpenSequentialReadAsync();
                ReadFrames(inputStream);
            }
            catch(FileNotFoundException ex)
            {
                Debug.WriteLine("File not found: " + ex.Message);
            }
            catch (Exception ex)
            {
                // Don't display any info
                Debug.WriteLine("Unknown exception: " + ex.Message);
            }
        }

        public async Task SaveMusicPropertiesAsync()
        {
            if (_file == null)
                return;

            if(!_supported)
            {
                Debug.WriteLine("Unsupported format");  //todo: ask for overwriting (adding in front)
                throw new InvalidOperationException("Unsupported version");
            }

            if (_frames.Count == 0)
            {
                Debug.WriteLine("Shouldn't be empty");
                return;
            }

            int oldTagSizeIncludingHeader = _tagHeader.Size + HEADER_SIZE;

            // Compute new tag size
            int newTagSizeIncludingHeader = HEADER_SIZE;  // initialize with tag header size
            foreach (Frame frame in _frames)
            {
                newTagSizeIncludingHeader += frame.Size + HEADER_SIZE;
            }

            //todo: if something fails, original file should not be corrupted

            #region Save in memory

            // Save in memory when we need to write more data (overwrite)
            Windows.Storage.Streams.Buffer savedDataBuffer = null;
            int paddingBytes = 0;       // number of 0x00 bytes to write

            if (newTagSizeIncludingHeader > oldTagSizeIncludingHeader)     // avoid overwrite
            {
                paddingBytes = 100;         // so that when we edit again, we don't have to overwrite it
                using (var stream = await _file.OpenReadAsync())
                {
                    int savedDataSize = (int)stream.Size - oldTagSizeIncludingHeader;
                    savedDataBuffer = new Windows.Storage.Streams.Buffer((uint)savedDataSize);

                    stream.Seek((ulong)oldTagSizeIncludingHeader);
                    await stream.ReadAsync(savedDataBuffer, savedDataBuffer.Capacity, InputStreamOptions.None);
                }
            }
            else
            {
                paddingBytes = oldTagSizeIncludingHeader - newTagSizeIncludingHeader;     // make sure to make old tag's bytes 0
            }

            #endregion

            #region Write to disk

            using (var stream = await _file.OpenStreamForWriteAsync())
            {
                var outputStream = stream.AsOutputStream();

                // Write Tag
                DataWriter dataWriter = new DataWriter(outputStream);

                _tagHeader.Size = newTagSizeIncludingHeader - HEADER_SIZE + paddingBytes;
                if (newTagSizeIncludingHeader <= oldTagSizeIncludingHeader)
                {
                    Debug.Assert(_tagHeader.Size + HEADER_SIZE == oldTagSizeIncludingHeader);
                }
                WriteTagHeader(_tagHeader, dataWriter);

                WriteFrames(dataWriter);

                // write padding
                for (int i = 0; i < paddingBytes; i++)
                {
                    dataWriter.WriteByte(0);
                }

                if (newTagSizeIncludingHeader > oldTagSizeIncludingHeader)
                {
                    // Write content
                    dataWriter.WriteBuffer(savedDataBuffer);
                }

                await dataWriter.StoreAsync();
                dataWriter.DetachStream();
                // todo: test dispose
            }
            #endregion
        }


        public async Task ResetTagHeader(StorageFile sf)
        {
            using (var stream = await sf.OpenStreamForWriteAsync())
            {
                var outputStream = stream.AsOutputStream();

                // Write Tag
                DataWriter dataWriter = new DataWriter(outputStream);

                WriteTagHeader(new TagHeader() { Id = "ID3", Version = new byte[] { 3, 0 }, Flags = 0, Size = 0 }, dataWriter);

                await dataWriter.StoreAsync();
                dataWriter.DetachStream();

            }
        }

        #endregion

        #region Tag Header
        // returns the size of id3 tag
        private TagHeader ReadTagHeader(BinaryReader binaryReader)
        {
            var idBytes = binaryReader.ReadBytes(3);
            string id3v2TagId = Encoding.UTF8.GetString(idBytes, 0, 3);
            if (id3v2TagId != "ID3")
            {
                Debug.WriteLine("ID3 tag not found in the very begining");
                return null;
            }

            TagHeader tagHeader = new TagHeader();
            tagHeader.Id = id3v2TagId;
            tagHeader.Version = binaryReader.ReadBytes(2);
            tagHeader.Flags = binaryReader.ReadByte();

            byte[] tagSizeBytes = binaryReader.ReadBytes(4);
            int tagSize = tagSizeBytes[0] * 128 * 128 * 128 + tagSizeBytes[1] * 128 * 128 + tagSizeBytes[2] * 128 + tagSizeBytes[3];
            tagHeader.Size = tagSize;
            return tagHeader;
        }

        private static void WriteTagHeader(TagHeader tagHeader, DataWriter dataWriter)
        {
            // Write "ID3"
            byte[] headerIdBytes = Encoding.UTF8.GetBytes(tagHeader.Id);
            Debug.Assert(tagHeader.Id == "ID3");
            Debug.Assert(headerIdBytes.Length == 3);

            dataWriter.WriteBytes(headerIdBytes);
            dataWriter.WriteBytes(tagHeader.Version);
            dataWriter.WriteByte(tagHeader.Flags);

            // Size
            int size = tagHeader.Size;
            byte[] sizeBytes = new byte[4];
            sizeBytes[3] = (byte)(size % 128);  // base 128
            size = size / 128;
            sizeBytes[2] = (byte)(size % 128);
            size = size / 128;
            sizeBytes[1] = (byte)(size % 128);
            size = size / 128;
            sizeBytes[0] = (byte)(size % 128);

            dataWriter.WriteBytes(sizeBytes);
        }
        #endregion

        #region Frames
        private void ReadFrames(IInputStream inputStream)
        {
            Stream binaryStream = inputStream.AsStreamForRead();    // don't dispose twice
            // default encoding is utf-8 (we have to read only id as 4 chars)
            using (BinaryReader binaryReader = new BinaryReader(binaryStream))
            {
                _tagHeader = ReadTagHeader(binaryReader);

                if (_tagHeader == null)  // tag not found in the begining
                {
                    // create a new one
                    _tagHeader = new TagHeader() { Id = "ID3", Version = new byte[]{3, 0}, Flags = 0, Size = 0 };
                    return;         // don't read anything else
                }

                if (_tagHeader.MajorVersion != 3)   // unsupported version
                {
                    Debug.WriteLine("Major version is not 3, but " + _tagHeader.MajorVersion);
                    _supported = false;
                    return;
                }

                if (_tagHeader.Flags != 0)
                {
                    Debug.WriteLine("Unexpected situation: flags not handled");
                    return;
                }

                int tagDataRead = 0;
                while (binaryStream.Position < 10 + _tagHeader.Size)
                {
                    Frame frame = Frame.ReadFrame(binaryReader);

                    // padding reached
                    if (frame == null)
                    {
                        break;
                    }

                    _frames.Add(frame);     // add to the list, we also need to write them

                    tagDataRead += frame.Size + HEADER_SIZE;

                    if (tagDataRead >= _tagHeader.Size)
                        break;
                }
            }
        }

        private void WriteFrames(DataWriter dataWriter)
        {
            foreach (Frame frame in _frames)
            {
                Frame.WriteFrame(frame, dataWriter);
            }
        }

        #endregion
    }
}
/*
 * private List<string> _textFrameIds = new List<string>{ "TALB", "TBPM", "TCOM", "TCON", "TCOP", 
            "TDAT", "TDLY", "TENC", "TEXT", "TFLT", "TIME", "TIT1", "TIT2", "TIT3", "TKEY", "TLAN", "TOAL", 
            "TOFN", "TOLY", "TOPE", "TORY", "TOWN", "TPE1", "TPE2", "TPE3", "TPE4", "TPOS", "TPUB", "TRCK", 
            "TRDA", "TRSN", "TRSO", "TSIZ", "TSRC", "TSSE", "TYER"};
 * //// if this is text frame
                        //if (_textFrameIds.Contains(frame.Id))
                        //{
                        //    _keyValuePairs.Add(frame.Id, frame.GetTextValue());
                        //}
 foreach (var pair in _keyValuePairs)
            {
                Debug.WriteLine(pair.Key + ": " + pair.Value);
            }
 * //Stream stream = new MemoryStream(data, i, data.Length - i);
            //BitmapImage image = new BitmapImage();
            //IRandomAccessStream ras = stream.AsRandomAccessStream();
            //image.SetSource(ras);
*/