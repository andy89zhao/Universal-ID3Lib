using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace ID3Library
{
    // version 2.3.0
    /* 
    * Frame ID   $xx xx xx xx  (four characters)     
    * Size       $xx xx xx xx  (Big endian)
    * Flags      $xx xx
    */
    public class Frame
    {
        public Frame(string frameId)
        {
            this.Id = frameId;
            this.Flags = new byte[2] { 0, 0 };
            this.Data = new byte[0];    // should never be null, because Size property depends on it
        }

        #region Properties

        public string Id { get; set; }
        public byte[] Flags { get; set; }
        public byte[] Data { get; set; }
        public int Size { get { return Data.Length; } }

        #endregion

        #region Get/Set text value

        /*
             * <Header for 'Text information frame', ID: "T000" - "TZZZ",     
             * excluding "TXXX" described in 4.2.2.>
             * Text encoding                $xx     
             * Information                  <text string according to encoding>
             */
        // by default store it in Unicode with Byter Order Mark (little endian)
        // except for TYER
        public void SetTextValue(string value)
        {
            byte[] valueBytes = null;
            if (this.Id == "TYER")
            {
                // Standard for TYER frame
                // The 'Year' frame is a numeric string with a year of the recording.
                // This frames is always four characters long (until the year 10000).
                // All numeric strings and URLs [URL] are always encoded as ISO-8859-1.

                try
                {
                    int year = Int32.Parse(value);
                    value = year.ToString();
                    while (value.Length < 4)
                    {
                        value = "0" + value;
                    }
                }
                catch (FormatException ex)
                {
                    Debug.WriteLine("Exception: " + ex.Message);
                    value = "0000";
                }

                valueBytes = Encoding.UTF8.GetBytes(value);
                Debug.Assert(valueBytes.Length == 4);
                Data = new byte[valueBytes.Length + 1];
                Array.Copy(valueBytes, 0, Data, 1, valueBytes.Length);
                Data[0] = (byte)TextEncoding.Ascii;     // Write as ASCII only, but read gracefully
            }
            else
            {
                valueBytes = Utf16String.GetBytes(value, false, true);
                Data = new byte[valueBytes.Length + 1];
                Array.Copy(valueBytes, 0, Data, 1, valueBytes.Length);
                Data[0] = (byte)TextEncoding.Utf16Bom;
            }
        }

        public string GetTextValue()
        {
            string textString = string.Empty;
            try
            {
                // 1 byte text encoding
                byte textEncoding = Data[0];    // A frame must be at least 1 byte big, excluding the header.
                textString = Functions.GetString(Data, 1, Data.Length - 1, (TextEncoding)textEncoding);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex);
            }
            
            return textString;
        }

        #endregion

        /*
         *  Frame ID   $xx xx xx xx  (four characters)     
         *  Size       $xx xx xx xx     
         *  Flags      $xx xx
         */
        public static Frame ReadFrame(BinaryReader binaryReader)
        {
            string frameId = string.Empty;

            try
            {
                char[] frameIdChars = binaryReader.ReadChars(4);    // utf-8

                // check if padding reached
                if (frameIdChars[0] != 0)
                {
                    frameId = new String(frameIdChars);
                }
            }
            catch (Exception)
            {
                // System.Text.Encoding.ThrowCharsOverflow exception is thrown
            }

            if (String.IsNullOrEmpty(frameId))
                return null;

            byte[] headerSizeBytes = binaryReader.ReadBytes(4);
            int frameSize = Functions.BigEndianToInt(headerSizeBytes);

            byte[] flags = binaryReader.ReadBytes(2);

            if(flags[0] != 0 || flags[1] != 0)
            {
                Debug.WriteLine("Frame Flags are not zero");
            }

            Frame frame = new Frame(frameId);
            frame.Flags = flags;
            frame.Data = binaryReader.ReadBytes(frameSize);

            return frame;
        }

        public static void WriteFrame(Frame frame, DataWriter dataWriter)
        {
            // 4 bytes id
            byte[] frameIdBytes = Encoding.UTF8.GetBytes(frame.Id);
            Debug.Assert(frameIdBytes.Length == 4);
            dataWriter.WriteBytes(frameIdBytes);

            // 4 bytes size in big endian
            byte[] frameSizeBytes = Functions.IntToBigEndianBytes(frame.Size);
            dataWriter.WriteBytes(frameSizeBytes);

            // 2 bytes flags
            dataWriter.WriteBytes(frame.Flags);

            // data
            dataWriter.WriteBytes(frame.Data);
        }
    }

    /*
     * ID3v2/file identifier      "ID3"     
     * ID3v2 version              $03 00     
     * ID3v2 flags                %abc00000     
     * ID3v2 size             4 * %0xxxxxxx
     */
    public class TagHeader
    {
        public string Id { get; set; }
        public byte[] Version { get; set; }
        public byte Flags { get; set; }
        public int Size { get; set; }

        public byte MajorVersion { get { return Version[0]; } }
    }
}
