using System.Buffers.Binary;
using System.Text;

namespace FunctionFoundry.Data;

/// <summary>
/// UTF-8 length-prefixed string codec for external merge sort.
/// </summary>
public sealed class Utf8StringMergeSortCodec : IMergeSortRecordCodec<string>
{
    private readonly int _maximumSerializedBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8StringMergeSortCodec"/> class.
    /// </summary>
    /// <param name="maximumUtf8Bytes">Maximum UTF-8 byte length per record. Must be positive.</param>
    public Utf8StringMergeSortCodec(int maximumUtf8Bytes = 16 * 1024)
    {
        if (maximumUtf8Bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUtf8Bytes), "Maximum UTF-8 bytes must be positive.");
        }

        _maximumSerializedBytes = sizeof(int) + maximumUtf8Bytes;
    }

    /// <inheritdoc />
    public int MaximumSerializedBytes => _maximumSerializedBytes;

    /// <inheritdoc />
    public async ValueTask WriteAsync(string record, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = Encoding.UTF8.GetBytes(record);
        if (bytes.Length > _maximumSerializedBytes - sizeof(int))
        {
            throw new ArgumentOutOfRangeException(nameof(record), "Record exceeds configured UTF-8 byte limit.");
        }

        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] header = new byte[sizeof(int)];
        int read = await ReadExactAsync(input, header, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read != header.Length)
        {
            throw new InvalidDataException("Truncated merge-sort record length prefix.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > _maximumSerializedBytes - sizeof(int))
        {
            throw new InvalidDataException("Invalid merge-sort record length.");
        }

        byte[] payload = new byte[length];
        read = await ReadExactAsync(input, payload, cancellationToken).ConfigureAwait(false);
        if (read != payload.Length)
        {
            throw new InvalidDataException("Truncated merge-sort record payload.");
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async ValueTask<int> ReadExactAsync(Stream input, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await input.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset;
            }

            offset += read;
        }

        return offset;
    }
}
