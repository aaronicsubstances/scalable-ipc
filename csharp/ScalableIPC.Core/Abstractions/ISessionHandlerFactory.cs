namespace ScalableIPC.Core.Abstractions
{
    public interface ISessionHandlerFactory
    {
        ISessionHandler Create(bool configureForInitialSend);
    }
}