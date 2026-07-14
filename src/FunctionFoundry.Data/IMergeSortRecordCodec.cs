namespace FunctionFoundry.Data;

/// <summary>
/// Serializes and deserializes records for external merge sort temp runs.
/// </summary>
/// <typeparam name="T">Record type.</typeparam>
public interface IMergeSortRecordCodec<T>
{
    /// <summary>
    /// Gets the maximum serialized byte length for a single record used in memory budgeting.
    /// </summary>
    int MaximumSerializedBytes { get; }

    /// <summary>
    /// Writes a record to the output stream.
    /// </summary>
    /// <param name="record">Record to write.</param>
    /// <param name="output">Writable stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask WriteAsync(T record, Stream output, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the next record from the input stream.
    /// </summary>
    /// <param name="input">Readable stream positioned at a record boundary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> at end-of-stream.</returns>
    ValueTask<T?> ReadAsync(Stream input, CancellationToken cancellationToken);
}
