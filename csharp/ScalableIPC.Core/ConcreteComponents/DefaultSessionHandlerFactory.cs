using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core.ConcreteComponents
{
    public class DefaultSessionHandlerFactory: ISessionHandlerFactory
    {
        public DefaultSessionHandlerFactory(Type sessionHandlerType)
        {
            SessionHandlerType = sessionHandlerType;
        }

        public Type SessionHandlerType { get; }

        public ISessionHandler Create()
        {
            var sessionHandler = (ISessionHandler) Activator.CreateInstance(SessionHandlerType);
            return sessionHandler;
        }
    }
}
