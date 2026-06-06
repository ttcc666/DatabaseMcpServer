namespace DatabaseMcpServer.Interfaces;

public interface IJsonResultSerializer
{
    string Serialize(object data);
}
