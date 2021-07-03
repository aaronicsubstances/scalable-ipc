using ScalableIPC.Core.ProtocolOperation;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Core.UnitTests.ProtocolOperation
{
    public class EndpointStructuredDatastoreTest
    {
        [Fact]
        public void TestNormalUsage()
        {
            // create variables to be used for testing.
            var remoteEndpoint1 = new GenericNetworkIdentifier { HostName = "A" };
            var remoteEndpoint2 = new GenericNetworkIdentifier { HostName = "B" };
            var remoteEndpoint3 = new GenericNetworkIdentifier { HostName = "C" };
            string msgId11 = "11";
            string msgId21 = "21", msgId22 = "22";
            string msgId31 = "31", msgId32 = "32", msgId33 = "33";
            object value11 = new object();
            object value21 = new object();
            object value22 = new object();
            object value31 = new object();
            object value32 = new object();
            object value33 = new object();
            var instance = new EndpointStructuredDatastore<object>();

            // assert initial conditions on empty store.
            Assert.Equal(0, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier>(), instance.GetEndpoints());
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint2));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint3, msgId31, null));

            // assert trying to remove from empty store doesn't result in error.
            Assert.False(instance.Remove(remoteEndpoint1, msgId33));

            // make first addition to store.
            Assert.True(instance.Add(remoteEndpoint1, msgId11, value11));

            Assert.Equal(1, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> { remoteEndpoint1 }, instance.GetEndpoints());
            Assert.Equal(new List<string> { msgId11 }, instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object> { value11 }, instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint3));
            Assert.Equal(1, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint3));
            Assert.Equal(value11, instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId31, null));

            // reset store to initial empty state by removal operations.
            Assert.False(instance.Remove(remoteEndpoint3, null));
            Assert.True(instance.RemoveAll(remoteEndpoint1));

            Assert.Equal(0, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier>(), instance.GetEndpoints());
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint3));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId31, null));

            // fill up store with all values created for it in this test case.
            Assert.True(instance.Add(remoteEndpoint1, msgId11, value11));
            Assert.True(instance.Add(remoteEndpoint2, msgId21, value21));
            Assert.True(instance.Add(remoteEndpoint2, msgId22, value22));
            Assert.True(instance.Add(remoteEndpoint3, msgId31, value31));
            Assert.True(instance.Add(remoteEndpoint3, msgId32, value32));
            Assert.True(instance.Add(remoteEndpoint3, msgId33, value33));

            Assert.Equal(3, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> {
                remoteEndpoint1, remoteEndpoint2, remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string> { msgId11 },
                instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string> { msgId21, msgId22 },
                instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string> { msgId31, msgId32, msgId33 },
                instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object> { value11 },
                instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object> {
                value21, value22 }, instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object> {
                value31, value32, value33 },
                instance.GetValues(remoteEndpoint3));
            Assert.Equal(1, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(2, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(3, instance.GetValueCount(remoteEndpoint3));
            Assert.Equal(value11, instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Equal(value21, instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Equal(value22, instance.Get(remoteEndpoint2, msgId22, null));
            Assert.Equal(value31, instance.Get(remoteEndpoint3, msgId31, null));
            Assert.Equal(value32, instance.Get(remoteEndpoint3, msgId32, null));
            Assert.Equal(value33, instance.Get(remoteEndpoint3, msgId33, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId31, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId21, null));

            // test remove method, that it drops endpoint count if and only if
            // there are no messages for that endpoint.
            Assert.True(instance.Remove(remoteEndpoint1, msgId11));
            Assert.True(instance.Remove(remoteEndpoint2, msgId21));
            Assert.False(instance.Remove(remoteEndpoint1, msgId11));

            Assert.Equal(2, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> {
                remoteEndpoint2, remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string>(),
                instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string> { msgId22 },
                instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string> { msgId31, msgId32, msgId33 },
                instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object>(),
                instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object> {
                value22 }, instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object> {
                value31, value32, value33 },
                instance.GetValues(remoteEndpoint3));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(1, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(3, instance.GetValueCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Equal(value22, instance.Get(remoteEndpoint2, msgId22, null));
            Assert.Equal(value31, instance.Get(remoteEndpoint3, msgId31, null));
            Assert.Equal(value32, instance.Get(remoteEndpoint3, msgId32, null));
            Assert.Equal(value33, instance.Get(remoteEndpoint3, msgId33, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId31, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId21, null));

            // continue test of remove method.
            Assert.False(instance.Remove(remoteEndpoint1, msgId11));
            Assert.True(instance.Remove(remoteEndpoint2, msgId22));
            Assert.True(instance.Remove(remoteEndpoint3, msgId31));
            Assert.False(instance.Remove(remoteEndpoint3, msgId31));

            Assert.Equal(1, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> {
                remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string>(),
                instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string>(),
                instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string> { msgId32, msgId33 },
                instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object>(),
                instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object>(),
                instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object> {
                value32, value33 },
                instance.GetValues(remoteEndpoint3));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(2, instance.GetValueCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId22, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId31, null));
            Assert.Equal(value32, instance.Get(remoteEndpoint3, msgId32, null));
            Assert.Equal(value33, instance.Get(remoteEndpoint3, msgId33, null));

            // test that additions after removals work fine.
            Assert.False(instance.Remove(remoteEndpoint2, msgId22));
            Assert.True(instance.Remove(remoteEndpoint3, msgId32));
            Assert.True(instance.Add(remoteEndpoint1, msgId11, value11));
            Assert.True(instance.Add(remoteEndpoint3, msgId31, value31));

            Assert.Equal(2, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> {
                remoteEndpoint1, remoteEndpoint3, }, instance.GetEndpoints());
            Assert.Equal(new List<string> { msgId11 },
                instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string>(),
                instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string> { msgId31, msgId33 },
                instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object> { value11 },
                instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object>(),
                instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object> {
                value31, value33 },
                instance.GetValues(remoteEndpoint3));
            Assert.Equal(1, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(2, instance.GetValueCount(remoteEndpoint3));
            Assert.Equal(value11, instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId22, null));
            Assert.Equal(value31, instance.Get(remoteEndpoint3, msgId31, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId32, null));
            Assert.Equal(value33, instance.Get(remoteEndpoint3, msgId33, null));

            // test remove all method.
            Assert.True(instance.RemoveAll(remoteEndpoint1));
            Assert.False(instance.RemoveAll(remoteEndpoint2));
            Assert.True(instance.RemoveAll(remoteEndpoint3));
            Assert.False(instance.RemoveAll(remoteEndpoint3));

            Assert.Equal(0, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier>(), instance.GetEndpoints());
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object>(),
                instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object>(),
                instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object>(),
                instance.GetValues(remoteEndpoint3));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint3));
            Assert.Null(instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId22, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId31, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId32, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId33, null));

            // test that addition after remove all works as expected.
            Assert.True(instance.Add(remoteEndpoint1, msgId11, value11));

            Assert.Equal(1, instance.GetEndpointCount());
            Assert.Equal(new List<GenericNetworkIdentifier> { remoteEndpoint1 }, instance.GetEndpoints());
            Assert.Equal(new List<string> { msgId11 }, instance.GetMessageIds(remoteEndpoint1));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint2));
            Assert.Equal(new List<string>(), instance.GetMessageIds(remoteEndpoint3));
            Assert.Equal(new List<object> { value11 }, instance.GetValues(remoteEndpoint1));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint2));
            Assert.Equal(new List<object>(), instance.GetValues(remoteEndpoint3));
            Assert.Equal(1, instance.GetValueCount(remoteEndpoint1));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint2));
            Assert.Equal(0, instance.GetValueCount(remoteEndpoint3));
            Assert.Equal(value11, instance.Get(remoteEndpoint1, msgId11, null));
            Assert.Null(instance.Get(remoteEndpoint2, msgId21, null));
            Assert.Null(instance.Get(remoteEndpoint3, msgId31, null));
        }

        [Fact]
        public void TestUnexpectedUsage()
        {
            // create variables to be used for testing.
            var remoteEndpoint1 = new GenericNetworkIdentifier { HostName = "A" };
            string msgId11 = "11";
            object value11 = new object();
            var instance = new EndpointStructuredDatastore<object>();

            Assert.False(instance.RemoveAll(null));
            Assert.False(instance.Remove(remoteEndpoint1, null));

            Assert.True(instance.Add(remoteEndpoint1, msgId11, value11));
            Assert.False(instance.Remove(remoteEndpoint1, null));
            Assert.Null(instance.Get(remoteEndpoint1, null, null));

            Assert.Null(instance.Get(null, msgId11, null));
            Assert.Equal(0, instance.GetValueCount(null));
            Assert.Equal(new List<string>(), instance.GetMessageIds(null));
            Assert.Equal(new List<object>(), instance.GetValues(null));
        }

        [Fact]
        public void TestErrorUsage()
        {
            // create variables to be used for testing.
            var remoteEndpoint1 = new GenericNetworkIdentifier { HostName = "A" };
            var remoteEndpoint2 = new GenericNetworkIdentifier { HostName = "B" };
            string msgId11 = "11";
            string msgId21 = "21";
            object value11 = new object();
            object value21 = new object();
            var instance = new EndpointStructuredDatastore<object>();

            // test error cases of addition using existing endpoint and msg id
            Assert.ThrowsAny<Exception>(() => instance.Add(null, msgId11, value11));
            Assert.ThrowsAny<Exception>(() => instance.Add(remoteEndpoint1, null, value11));
            Assert.True(instance.Add(remoteEndpoint1, msgId11, value11));
            Assert.False(instance.Add(remoteEndpoint1, msgId11, value11));
            Assert.True(instance.Add(remoteEndpoint1, msgId21, value21));
            Assert.False(instance.Add(remoteEndpoint1, msgId21, value21));
        }
    }
}
