namespace ProcurementRadar.Domain.Settings;

/// <summary>
/// A setting as it is right now, rather than as it was when the worker started.
/// </summary>
/// <remarks>
/// The difference is the whole point of the settings screen. A class that takes
/// KeywordRules in its constructor holds whatever the file said at startup, and
/// keeps holding it after somebody changes the threshold from their phone. One
/// that takes ICurrent&lt;KeywordRules&gt; and reads Value per call picks the
/// change up on the next notice, with no restart.
///
/// Only for things that genuinely change while running. Wrapping a connection
/// string in this would be noise.
/// </remarks>
public interface ICurrent<out T>
{
    T Value { get; }
}

/// <summary>
/// A value that never changes. For tests, and for hosts with no database.
/// </summary>
public sealed class Fixed<T> : ICurrent<T>
{
    public Fixed(T value)
    {
        Value = value;
    }

    public T Value { get; }
}
