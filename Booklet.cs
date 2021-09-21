
using System;

namespace MappableFileStream
{
    public readonly struct Booklet
    {
        public readonly int Index;
        public readonly int AccessCount;
        public readonly DateTime LastAccess;

        public Booklet(int index)
        {
            Index = index;
            AccessCount = 0;
            LastAccess = DateTime.Now;
        }

        public Booklet(int index, int accessCount, DateTime dt)
        {
            Index = index;
            AccessCount = accessCount;
            LastAccess = DateTime.Now;
        }

        public Booklet Touch() => new Booklet(Index, AccessCount + 1, DateTime.Now);
    }
}
