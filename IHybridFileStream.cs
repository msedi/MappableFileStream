namespace MemoryMapFile
{
    public interface IHybridFileStream
    {
        void Flush();

        ulong GetMemory();

        int GetItemCount();


    }
}
