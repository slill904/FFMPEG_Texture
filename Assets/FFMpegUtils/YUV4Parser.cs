﻿using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;

namespace FFMpegUtils
{
    public static class ArraySegmentExtensions
    {
        public static T Get<T>(this ArraySegment<T> s, int index)
        {
            if (index >= s.Count) throw new IndexOutOfRangeException();
            return s.Array[s.Offset+index];
        }

        public static IEnumerable<T> ToE<T>(this ArraySegment<T> s)
        {
            return s.Array.Skip(s.Offset).Take(s.Count);
        }
    }       

    public enum YUVFormat
    {
        YUV420,
    }

    [Serializable]
    public class YUVHeader
    {
        public YUVFormat Format;
        public int Width;
        public int Height;

        public YUVHeader(int w, int h, YUVFormat format)
        {
            Width = w;
            Height = h;
            Format = format;
        }

        public override string ToString()
        {
            return String.Format("[{0}]{1}x{2}", Format, Width, Height);
        }

        public int BodyByteLength
        {
            get
            {
                return Width * Height * 3 / 2;
            }
        }

        public static YUVHeader Parse(String header)
        {
            int width = 0;
            int height = 0;
            var format = default(YUVFormat);
            foreach (var value in header.Split())
            {
                switch (value.FirstOrDefault())
                {
                    case 'W':
                        width = int.Parse(value.Substring(1));
                        break;

                    case 'H':
                        height = int.Parse(value.Substring(1));
                        break;

                    case 'C':
                        if (value.StartsWith("C420"))
                        {
                            format = YUVFormat.YUV420;
                        }
                        break;
                }
            }
            return new YUVHeader(width, height, format);
        }
    }

    public class YUVFrameReader
    {
        int m_frameNumber = -1;
        public int FrameNumber
        {
            get
            {
                return m_frameNumber;
            }
        }

        List<Byte> m_header = new List<byte>();

        int m_fill;
        Byte[] m_body;
        public Byte[] Body
        {
            get { return m_body; }
        }

        public bool IsFill
        {
            get
            {
                return m_fill >= m_body.Length;
            }
        }

        bool m_isFrameHeader = true;

        public YUVFrameReader(YUVHeader header)
        {
            m_body = new Byte[header.BodyByteLength];
        }

        public void Clear(int number)
        {
            m_isFrameHeader = true;
            m_header.Clear();
            m_fill = 0;
            m_frameNumber = number;
        }

        public int Push(ArraySegment<Byte> bytes, int i)
        {
            if (m_isFrameHeader)
            {
                for (; i < bytes.Count; ++i)
                {
                    if (bytes.Get(i) == 0x0a)
                    {
                        m_isFrameHeader = false;
                        ++i;
                        break;
                    }
                    m_header.Add(bytes.Get(i));
                }
            }

            for (; i < bytes.Count && m_fill < m_body.Length; ++i, ++m_fill)
            {
                m_body[m_fill] = bytes.Get(i);
            }

            return i;
        }
    }

    public class YUVFrame
    {
        public int FrameNumber;
        public Byte[] Bytes;

        public YUVFrame()
        {
            FrameNumber = -1;
            Bytes = null;
        }
    }

    [Serializable]
    public class YUVReader
    {
        public YUVHeader Header;

        public List<Byte> m_buffer = new List<byte>();

        Object m_currentLock = new object();
        YUVFrameReader m_current;
        YUVFrameReader m_next;

        YUVFrame m_frame = new YUVFrame();
        public YUVFrame GetFrame()
        {
            lock (m_current)
            {
                if (m_frame.FrameNumber!=m_current.FrameNumber)
                {
                    // copy
                    m_frame.FrameNumber = m_current.FrameNumber;
                    if(m_frame.Bytes== null)
                    {
                        m_frame.Bytes = m_current.Body.ToArray();
                    }
                    else
                    {
                        Array.Copy(m_current.Body, m_frame.Bytes, m_current.Body.Length);
                    }
                }
            }
            return m_frame;
        }

        public YUVReader()
        {
        }

        public void Push(ArraySegment<Byte> bytes)
        {
            if (Header == null)
            {
                m_buffer.AddRange(bytes.ToE());
                var index = m_buffer.IndexOf(0x0A);
                if (index == -1)
                {
                    return;
                }
                var tmp = m_buffer.Take(index).ToArray();
                Header = YUVHeader.Parse(Encoding.ASCII.GetString(tmp));
                m_current = new YUVFrameReader(Header);
                m_next = new YUVFrameReader(Header);
                PushBody(new ArraySegment<Byte>(m_buffer.Skip(index + 1).ToArray()));
                m_buffer.Clear();
            }
            else
            {
                 PushBody(bytes);
            }
        }

        public int m_frameNumber;

        bool PushBody(ArraySegment<Byte> bytes)
        { 
            bool hasNewFrame = false;

            var i = 0;
            while (i < bytes.Count)
            {
                i = m_next.Push(bytes, i);
                if (m_next.IsFill)
                {
                    YUVFrameReader tmp;
                    lock (m_currentLock)
                    {
                        tmp = m_current;
                        m_current = m_next;
                    }
                    m_next = tmp;
                    m_next.Clear(m_frameNumber++);
                    hasNewFrame = true;
                }
            }

            return hasNewFrame;
        }
    }
}
/*
readonly Byte[] frame_header = new[] { (byte)0x46, (byte)0x52, (byte)0x41, (byte)0x4D, (byte)0x45 };


bool IsHead(List<Byte> src)
{
    for (int i = 0; i < frame_header.Length; ++i)
    {
        if (src[i] != frame_header[i]) return false;
    }
    return true;
}
*/
