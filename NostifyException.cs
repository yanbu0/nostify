using System;

/// <summary>
/// Represents an exception in the nostify library
/// </summary>
public class NostifyException : Exception
{
    /// <summary>
    /// Constructor for NostifyException. May pop up in UI depending on implementation.
    /// </summary>
    /// <param name="message">Human readable error message.</param>

    public NostifyException(string message) : base(message)
    {

    }
}