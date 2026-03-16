namespace Shared
{
    public enum LogCategory : byte
    {
        Combat = 0,
        Movement = 1,
        Network = 2,
        Economy = 3,
        Wave = 4,
        System = 5,
        Construction = 6
    }

    public enum LogWorld : byte
    {
        Server = 0,
        Client = 1
    }
}
