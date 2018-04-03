using System.Collections;
using System.Collections.Generic;


// Very simple Fixed size ring buffer.
// It's the called responsability to keep where is their entry point
// this implementation will just write over when wrapping around
public class FixedRingBuffer<T>
{
    int _end;
    T[] _data;

    public int end => _end;
    public T[] data => _data;

    public FixedRingBuffer(int size)
    {
        _data = new T[size];
        _end = 0;
    }

    // return the index of the entry with offset to the end of array value
    // Remember : end point to the next writable point, so last value is a -1 offset
    // it take care of wrapping around (e.g. calling with offset = -3 and end = 1 return size - 2)
    public int GetIndex(int offset)
    {
        int corrected = _end + offset;

        while (corrected < 0)
            corrected += _data.Length;

        while (corrected >= _data.Length)
            corrected -= _data.Length;

        return corrected;
    }

    public void AddValue(T value)
    {
        _data[_end] = value;
        _end = (_end + 1) % _data.Length;
    }

    public void AddArray(T[] array, int count)
    {
        for (int i = 0; i < count; ++i)
            AddValue(array[i]);
    }

    // Put in result the content of the buffer from, start (included) to end (excluded) (in ring buffer space, so end can be smaller than start, it will wrap)
    // Return the count of data copied. If -1 is return, the result was too small to contain all.
    public int GetSubArray(ref T[] result, int start, int end)
    {
        int currentIdx = start;
        int resultIdx = 0;
        while(currentIdx != end)
        {
            if (resultIdx >= result.Length)
                return -1;

            result[resultIdx] = _data[currentIdx];

            resultIdx++;
            currentIdx = (currentIdx + 1) % _data.Length;
        }

        return resultIdx;
    }
}
