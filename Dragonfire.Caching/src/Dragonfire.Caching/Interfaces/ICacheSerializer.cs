namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// Serializes and deserializes cache values to/from byte arrays.
/// The default implementation uses System.Text.Json.
/// </summary>
public interface ICacheSerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] bytes);
}
