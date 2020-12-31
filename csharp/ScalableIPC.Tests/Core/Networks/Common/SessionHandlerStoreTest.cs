using ScalableIPC.Core;
using ScalableIPC.Core.Networks.Common;
using ScalableIPC.Core.Session;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Tests.Core.Networks.Common
{
    public class SessionHandlerStoreTest
    {
        [Fact]
        public void TestNormalUsage()
        {
            // create variables to be used for testing.
            var remoteEndpoint1 = new GenericNetworkIdentifier { HostName = "A" };
            var remoteEndpoint2 = new GenericNetworkIdentifier { HostName = "B" };
            var remoteEndpoint3 = new GenericNetworkIdentifier { HostName = "C" };
            string sessionId11 = "11";
            string sessionId21 = "21", sessionId22 = "22";
            string sessionId31 = "31", sessionId32 = "32", sessionId33 = "33";
            SessionHandlerWrapper sessionWrapper11 = new SessionHandlerWrapper(new DefaultSessionHandler());
            SessionHandlerWrapper sessionWrapper21 = new SessionHandlerWrapper(new DefaultSessionHandler());
            SessionHandlerWrapper sessionWrapper22 = new SessionHandlerWrapper(new DefaultSessionHandler());
            SessionHandlerWrapper sessionWrapper31 = new SessionHandlerWrapper(new DefaultSessionHandler());
            SessionHandlerWrapper sessionWrapper32 = new SessionHandlerWrapper(new DefaultSessionHandler());
            SessionHandlerWrapper sessionWrapper33 = new SessionHandlerWrapper(new DefaultSessionHandler());
            var instance = new SessionHandlerStore();

            // assert initial conditions on empty store.
            Assert.Equal(0, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier>(), instance.GetEndpoints());
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId31));

            // assert trying to remove from empty store doesn't result in error.
            Assert.False(instance.Remove(remoteEndpoint1, sessionId33));

            // make first addition to store.
            instance.Add(remoteEndpoint1, sessionId11, sessionWrapper11);

            Assert.Equal(1, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> { remoteEndpoint1 }, instance.GetEndpoints());
            Assert.Equal(new List<string> { sessionId11 }, instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper> { sessionWrapper11 }, instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(1, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint3));
            Assert.Equal(sessionWrapper11, instance.Get(remoteEndpoint1, sessionId11));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId21));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId31));

            // reset store to initial empty state by removal operations.
            Assert.False(instance.Remove(remoteEndpoint3, null));
            Assert.True(instance.RemoveAll(remoteEndpoint1));

            Assert.Equal(0, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier>(), instance.GetEndpoints());
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, sessionId11));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId21));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId31));

            // fill up store with all values created for it in this test case.
            instance.Add(remoteEndpoint1, sessionId11, sessionWrapper11);
            instance.Add(remoteEndpoint2, sessionId21, sessionWrapper21);
            instance.Add(remoteEndpoint2, sessionId22, sessionWrapper22);
            instance.Add(remoteEndpoint3, sessionId31, sessionWrapper31);
            instance.Add(remoteEndpoint3, sessionId32, sessionWrapper32);
            instance.Add(remoteEndpoint3, sessionId33, sessionWrapper33);

            Assert.Equal(3, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> { 
                remoteEndpoint1, remoteEndpoint2, remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string> { sessionId11 }, 
                instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string> { sessionId21, sessionId22 }, 
                instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string> { sessionId31, sessionId32, sessionId33 }, 
                instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper> { sessionWrapper11 }, 
                instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper> { 
                sessionWrapper21, sessionWrapper22 }, instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper> { 
                sessionWrapper31, sessionWrapper32, sessionWrapper33 }, 
                instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(1, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(2, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(3, instance.GetSessionCount(remoteEndpoint3));
            Assert.Equal(sessionWrapper11, instance.Get(remoteEndpoint1, sessionId11));
            Assert.Equal(sessionWrapper21, instance.Get(remoteEndpoint2, sessionId21));
            Assert.Equal(sessionWrapper22, instance.Get(remoteEndpoint2, sessionId22));
            Assert.Equal(sessionWrapper31, instance.Get(remoteEndpoint3, sessionId31));
            Assert.Equal(sessionWrapper32, instance.Get(remoteEndpoint3, sessionId32));
            Assert.Equal(sessionWrapper33, instance.Get(remoteEndpoint3, sessionId33));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId31));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId21));

            // test remove method, that it drops endpoint count if and only if
            // there are no sessions for that endpoint.
            Assert.True(instance.Remove(remoteEndpoint1, sessionId11));
            Assert.True(instance.Remove(remoteEndpoint2, sessionId21));
            Assert.False(instance.Remove(remoteEndpoint1, sessionId11));

            Assert.Equal(2, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> {
                remoteEndpoint2, remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string>(),
                instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string> { sessionId22 },
                instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string> { sessionId31, sessionId32, sessionId33 },
                instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper>(),
                instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper> {
                sessionWrapper22 }, instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper> {
                sessionWrapper31, sessionWrapper32, sessionWrapper33 },
                instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(1, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(3, instance.GetSessionCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, sessionId11));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId21));
            Assert.Equal(sessionWrapper22, instance.Get(remoteEndpoint2, sessionId22));
            Assert.Equal(sessionWrapper31, instance.Get(remoteEndpoint3, sessionId31));
            Assert.Equal(sessionWrapper32, instance.Get(remoteEndpoint3, sessionId32));
            Assert.Equal(sessionWrapper33, instance.Get(remoteEndpoint3, sessionId33));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId31));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId21));

            // continue test of remove method.
            Assert.False(instance.Remove(remoteEndpoint1, sessionId11));
            Assert.True(instance.Remove(remoteEndpoint2, sessionId22));
            Assert.True(instance.Remove(remoteEndpoint3, sessionId31));
            Assert.False(instance.Remove(remoteEndpoint3, sessionId31));

            Assert.Equal(1, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> {
                remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string>(),
                instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string>(),
                instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string> { sessionId32, sessionId33 },
                instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper>(),
                instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper>(),
                instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper> {
                sessionWrapper32, sessionWrapper33 },
                instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(2, instance.GetSessionCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, sessionId11));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId21));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId22));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId31));
            Assert.Equal(sessionWrapper32, instance.Get(remoteEndpoint3, sessionId32));
            Assert.Equal(sessionWrapper33, instance.Get(remoteEndpoint3, sessionId33));

            // test that additions after removals work fine.
            Assert.False(instance.Remove(remoteEndpoint2, sessionId22));
            Assert.True(instance.Remove(remoteEndpoint3, sessionId32));
            instance.Add(remoteEndpoint1, sessionId11, sessionWrapper11);
            instance.Add(remoteEndpoint3, sessionId31, sessionWrapper31);

            Assert.Equal(2, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> {
                remoteEndpoint1, remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string> { sessionId11 },
                instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string>(),
                instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string> { sessionId31, sessionId33 },
                instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper> { sessionWrapper11 },
                instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper>(),
                instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper> {
                sessionWrapper31, sessionWrapper33 },
                instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(1, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(2, instance.GetSessionCount(remoteEndpoint3));
            Assert.Equal(sessionWrapper11, instance.Get(remoteEndpoint1, sessionId11));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId21));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId22));
            Assert.Equal(sessionWrapper31, instance.Get(remoteEndpoint3, sessionId31));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId32));
            Assert.Equal(sessionWrapper33, instance.Get(remoteEndpoint3, sessionId33));

            // test remove all method.
            Assert.True(instance.RemoveAll(remoteEndpoint1));
            Assert.False(instance.RemoveAll(remoteEndpoint2));
            Assert.True(instance.RemoveAll(remoteEndpoint3));
            Assert.False(instance.RemoveAll(remoteEndpoint3));

            Assert.Equal(0, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier>(), instance.GetEndpoints());
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper>(),
                instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper>(),
                instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper>(),
                instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, sessionId11));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId21));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId22));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId31));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId32));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId33));

            // test that addition after remove all works as expected.
            instance.Add(remoteEndpoint1, sessionId11, sessionWrapper11);

            Assert.Equal(1, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> { remoteEndpoint1 }, instance.GetEndpoints());
            Assert.Equal(new List<string> { sessionId11 }, instance.GetSessionIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetSessionIds(remoteEndpoint3));
            Assert.Equal(new List<SessionHandlerWrapper> { sessionWrapper11 }, instance.GetSessionHandlers(remoteEndpoint1));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint2));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(remoteEndpoint3));
            Assert.Equal(1, instance.GetSessionCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetSessionCount(remoteEndpoint3));
            Assert.Equal(sessionWrapper11, instance.Get(remoteEndpoint1, sessionId11));
            Assert.Null(instance.Get(remoteEndpoint2, sessionId21));
            Assert.Null(instance.Get(remoteEndpoint3, sessionId31));
        }

        [Fact]
        public void TestUnexpectedUsage()
        {
            // create variables to be used for testing.
            var remoteEndpoint1 = new GenericNetworkIdentifier { HostName = "A" };
            string sessionId11 = "11";
            SessionHandlerWrapper sessionWrapper11 = new SessionHandlerWrapper(new DefaultSessionHandler());
            var instance = new SessionHandlerStore();

            Assert.False(instance.RemoveAll(null));
            Assert.False(instance.Remove(remoteEndpoint1, null));

            instance.Add(remoteEndpoint1, sessionId11, sessionWrapper11);
            Assert.False(instance.Remove(remoteEndpoint1, null));
            Assert.Null(instance.Get(remoteEndpoint1, null));

            Assert.Null(instance.Get(null, sessionId11));
            Assert.Equal(0, instance.GetSessionCount(null));
            Assert.Equal(new List<string>(), instance.GetSessionIds(null));
            Assert.Equal(new List<SessionHandlerWrapper>(), instance.GetSessionHandlers(null));
        }

        [Fact]
        public void TestErrorUsage()
        {
            // create variables to be used for testing.
            var remoteEndpoint1 = new GenericNetworkIdentifier { HostName = "A" };
            var remoteEndpoint2 = new GenericNetworkIdentifier { HostName = "B" };
            string sessionId11 = "11";
            string sessionId21 = "21";
            SessionHandlerWrapper sessionWrapper11 = new SessionHandlerWrapper(new DefaultSessionHandler());
            SessionHandlerWrapper sessionWrapper21 = new SessionHandlerWrapper(new DefaultSessionHandler());
            var instance = new SessionHandlerStore();

            // test error cases of addition using existing endpoint and session id
            Assert.ThrowsAny<Exception>(() => instance.Add(null, sessionId11, sessionWrapper11));
            Assert.ThrowsAny<Exception>(() => instance.Add(remoteEndpoint1, null, sessionWrapper11));
            Assert.ThrowsAny<Exception>(() => instance.Add(remoteEndpoint2, sessionId21, null));
            instance.Add(remoteEndpoint1, sessionId11, sessionWrapper11);
            Assert.ThrowsAny<Exception>(() => 
                instance.Add(remoteEndpoint1, sessionId11, sessionWrapper11));
            Assert.ThrowsAny<Exception>(() =>
                instance.Add(remoteEndpoint1, sessionId11, sessionWrapper21));
        }
    }
}
