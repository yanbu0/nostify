using System;

namespace nostify;

/// <summary>
/// Represents a sequence for generating sequential numbers within a partition.
/// Each sequence is uniquely identified by its name within a partition key.
/// </summary>
public class Sequence
{
    /// <summary>
    /// The unique identifier for this sequence document.
    /// Derived from "{partitionKey}_{name}" for deterministic lookups.
    /// </summary>
    public string id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the sequence within the partition.
    /// </summary>
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// The current value of the sequence. Incremented atomically via patch operations.
    /// </summary>
    public long currentValue { get; set; }

    /// <summary>
    /// The partition key value for this sequence.
    /// </summary>
    public string partitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Parameterless constructor for JSON deserialization.
    /// </summary>
    public Sequence()
    {
    }

    /// <summary>
    /// Creates a new sequence with the specified parameters.
    /// </summary>
    /// <param name="name">The name of the sequence.</param>
    /// <param name="partitionKeyValue">The partition key value.</param>
    /// <param name="startingValue">The starting value for the sequence.</param>
    public Sequence(string name, string partitionKeyValue, long startingValue = 0)
    {
        this.name = name;
        this.partitionKey = partitionKeyValue;
        this.currentValue = startingValue;
        this.id = GenerateId(partitionKeyValue, name);
    }

    /// <summary>
    /// Generates a deterministic document ID from the partition key and sequence name.
    /// </summary>
    /// <param name="partitionKeyValue">The partition key value.</param>
    /// <param name="sequenceName">The sequence name.</param>
    /// <returns>A deterministic ID string.</returns>
    public static string GenerateId(string partitionKeyValue, string sequenceName)
    {
        return $"{partitionKeyValue}_{sequenceName}";
    }
}

/// <summary>
/// Represents a range of sequence values reserved atomically.
/// Used for bulk sequence generation to reserve multiple values in a single operation.
/// </summary>
public readonly struct SequenceRange
{
    /// <summary>
    /// The first value in the reserved range (inclusive).
    /// </summary>
    public long StartValue { get; }

    /// <summary>
    /// The last value in the reserved range (inclusive).
    /// </summary>
    public long EndValue { get; }

    /// <summary>
    /// The number of values in this range.
    /// </summary>
    public int Count => (int)(EndValue - StartValue + 1);

    /// <summary>
    /// Creates a new SequenceRange with the specified start and end values.
    /// </summary>
    /// <param name="startValue">The first value in the range (inclusive).</param>
    /// <param name="endValue">The last value in the range (inclusive).</param>
    public SequenceRange(long startValue, long endValue)
    {
        StartValue = startValue;
        EndValue = endValue;
    }

    /// <summary>
    /// Returns an enumerable of all values in the range.
    /// </summary>
    /// <returns>An enumerable of sequential long values from StartValue to EndValue.</returns>
    public System.Collections.Generic.IEnumerable<long> ToEnumerable()
    {
        for (long i = StartValue; i <= EndValue; i++)
        {
            yield return i;
        }
    }

    /// <summary>
    /// Returns an array of all values in the range.
    /// </summary>
    /// <returns>An array of sequential long values from StartValue to EndValue.</returns>
    public long[] ToArray()
    {
        var result = new long[Count];
        for (int i = 0; i < Count; i++)
        {
            result[i] = StartValue + i;
        }
        return result;
    }

    /// <summary>
    /// Checks if a value is within this range.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is within the range (inclusive), false otherwise.</returns>
    public bool Contains(long value)
    {
        return value >= StartValue && value <= EndValue;
    }

    /// <summary>
    /// Returns a string representation of the range.
    /// </summary>
    public override string ToString()
    {
        return $"[{StartValue}..{EndValue}] (Count: {Count})";
    }
}
