namespace FunctionFoundry.Storage.Internal;

/// <summary>
/// 32-bit Rabin fingerprint rolling hash used for content-defined chunk boundaries.
/// </summary>
internal sealed class RabinRollingHash
{
    private const uint Polynomial = 0x0810_4225;
    private readonly int _windowSize;
    private readonly byte[] _window;
    private readonly uint[] _table = new uint[256];
    private int _filled;
    private int _index;
    private uint _hash;

    public RabinRollingHash(int windowSize)
    {
        if (windowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be positive.");
        }

        _windowSize = windowSize;
        _window = new byte[windowSize];
        InitializeTable();
    }

    public uint Push(byte value)
    {
        if (_filled < _windowSize)
        {
            _window[_index] = value;
            _hash = (_hash << 1) ^ _table[value];
            _filled++;
            _index = (_index + 1) % _windowSize;
            return _hash;
        }

        byte outgoing = _window[_index];
        _window[_index] = value;
        _index = (_index + 1) % _windowSize;
        _hash ^= _table[outgoing];
        _hash = (_hash << 1) ^ _table[value];
        return _hash;
    }

    public int Filled => _filled;

    private void InitializeTable()
    {
        for (int i = 0; i < 256; i++)
        {
            uint value = (uint)i;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((value & 1U) == 1U)
                {
                    value = (value >> 1) ^ Polynomial;
                }
                else
                {
                    value >>= 1;
                }
            }

            _table[i] = value;
        }
    }
}
