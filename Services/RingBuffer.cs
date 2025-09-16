namespace HouseholdMS.Services
{
    public class RingBuffer<T>
    {
        private readonly T[] _buf;
        private int _head, _count;
        public RingBuffer(int capacity) { _buf = new T[capacity]; }
        public int Count { get { return _count; } }
        public void Add(T item)
        {
            _buf[_head] = item;
            _head = (_head + 1) % _buf.Length;
            if (_count < _buf.Length) _count++;
        }
        public T[] Snapshot()
        {
            T[] arr = new T[_count];
            int idx = (_head - _count + _buf.Length) % _buf.Length;
            for (int i = 0; i < _count; i++) arr[i] = _buf[(idx + i) % _buf.Length];
            return arr;
        }
        public void Clear() { _head = 0; _count = 0; }
    }
}
