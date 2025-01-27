﻿using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RitsukageGif.Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CursorInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public Point ptScreenPos;
    }
}
