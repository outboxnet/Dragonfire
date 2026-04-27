namespace OutboxNet.Interfaces;

public interface IMessageSerializer
{
    string Serialize(object payload);
    T? Deserialize<T>(string payload);
}
