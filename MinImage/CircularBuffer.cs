using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinImage;

/// <summary>
/// I stole the main part of this code from the <see cref="Microsoft.VisualStudio.Utilities"/> package and added new functionality that fit my use case.
/// </summary>
/// <remarks>Implementing this was absolutely umnecessary and it tooks ages.</remarks>
/// <typeparam name="T"></typeparam>
public class CircularBuffer<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable
{
    private readonly T[] _buffer;

    private int _start;
    private int _end;
    private int _searchIndex; // Tracks the index the client is searching the buffer with
    private readonly T _defaultValue;

    public int Capacity => _buffer.Length;
    public bool IsFull => Count == Capacity;
    public bool IsEmpty => Count == 0;
    public int Count { get; private set; }
    public bool IsReadOnly { get; }

    public bool IsFixedSize { get; } = true;
    public object SyncRoot { get; } = new object();
    public bool IsSynchronized { get; }

    public void ResetSearchIndex()
    {
        _searchIndex = _end;
    }

    /// <summary>
    /// Get's the previous object that was less recently added (unless the last one was already the least recent, the it returns it again).
    /// </summary>
    /// <returns><see cref="T"/> object that was less recently added than the last returned object</returns>
    public T GetPrevious()
    {
        if (_searchIndex == _start)
        {
            return _buffer[_start];
        }
        Decrement(ref _searchIndex);
        return _buffer[_searchIndex];
    }

    /// <summary>
    /// Get's the next object that was more recently addded (unless the last one was already the most recent, the it returns it again).
    /// </summary>
    /// <returns><see cref="T"/> object that was more recently addred than the last returned object</returns>
    public T GetNext()
    {
        // NOTE: This should cover even edge cases. yay :D
        Increment(ref _searchIndex);
        if (_searchIndex < _end)
        {
            return _buffer[_searchIndex];
        }
        // Now we make room for an empty command
        _searchIndex = _end;
        if (Count == 0)
        {
            return _defaultValue;
        }
        Decrement(ref _searchIndex);
        if (_buffer[_searchIndex]!.Equals(_defaultValue))
            return _defaultValue;
        Add(_defaultValue);
        Increment(ref _searchIndex);
        return _buffer[_searchIndex];
    }

    public void ReplaceSearched(T item)
    {
        if (_searchIndex == _end)
        {
            Add(item);
        }
        else if (!_buffer[_searchIndex]!.Equals(item)) // Never null because _searchIndex != _end
        {
            _buffer[_searchIndex] = item;
        }
    }

    public T this[int index]
    {
        get
        {
            return _buffer[InternalIndex(index)];
        }
        set
        {
            _buffer[InternalIndex(index)] = value;
        }
    }

    public CircularBuffer(int capacity, T defaultVal)
        : this(capacity, Array.Empty<T>(), defaultVal)
    {
    }

    public CircularBuffer(int capacity, T[] items, T defaultVal)
    {
        if (capacity < 1)
        {
            throw new ArgumentException("Circular buffer must have a capacity greater than 0.", "capacity");
        }

        if (items == null)
        {
            throw new ArgumentNullException("items");
        }

        if (items.Length > capacity)
        {
            throw new ArgumentException("Too many items to fit circular buffer", "items");
        }

        _buffer = new T[capacity];
        Array.Copy(items, _buffer, items.Length);
        Count = items.Length;
        _start = 0;
        _end = ((Count != capacity) ? Count : 0);
        _defaultValue = defaultVal;
    }

    public int IndexOf(T item)
    {
        for (int i = 0; i < Count; i++)
        {
            if (object.Equals(this[i], item))
            {
                return i;
            }
        }
        return -1;
    }

    public void Insert(int index, T item)
    {
        throw new NotImplementedException();
    }

    public void RemoveAt(int index)
    {
        throw new NotImplementedException();
    }

    public bool Remove(T item)
    {
        throw new NotImplementedException();
    }

    public void Add(T item)
    {
        if (IsFull)
        {
            Decrement(ref _end);
            if (!_buffer[_end]!.Equals(_defaultValue))
            {
                Increment(ref _end);
            }
            _buffer[_end] = item;
            Increment(ref _end);
            _start = _end;
        }
        else
        {
            if (!IsEmpty)
            {
                Decrement(ref _end);
                if (!_buffer[_end]!.Equals(_defaultValue))
                {
                    Increment(ref _end);
                }
            }
            _buffer[_end] = item;
            Increment(ref _end);
            int count = Count + 1;
            Count = count;
        }
    }

    public void Clear()
    {
        Count = 0;
        _start = 0;
        _end = 0;
    }

    public bool Contains(T item)
    {
        return IndexOf(item) != -1;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("Array does not contain enough space for items");
        }

        for (int i = 0; i < Count; i++)
        {
            array[i + arrayIndex] = this[i];
        }
    }

    public T[] ToArray()
    {
        if (IsEmpty)
        {
            return Array.Empty<T>();
        }

        T[] array = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            array[i] = this[i];
        }

        return array;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private int InternalIndex(int index)
    {
        if (IsEmpty)
        {
            throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer is empty");
        }

        if (index >= Count)
        {
            throw new IndexOutOfRangeException($"Cannot access index {index}. Buffer size is {Count}");
        }

        return (_start + index) % Capacity;
    }

    private void Increment(ref int index)
    {
        if (++index >= Capacity)
        {
            index = 0;
        }
    }

    private void Decrement(ref int index)
    {
        if (index <= 0)
        {
            index = Capacity - 1;
        }
        index--;
    }
}
