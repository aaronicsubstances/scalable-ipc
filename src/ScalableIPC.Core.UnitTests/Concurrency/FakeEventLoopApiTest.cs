using ScalableIPC.Core.Concurrency;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ScalableIPC.Core.UnitTests.Concurrency
{
    public class FakeEventLoopApiTest
    {
        [Fact]
        public void TestNormalUsage()
        {
            var instance = new FakeEventLoopApi();
            Assert.Equal(0, instance.CurrentTimestamp);

            var callbackLogs = new List<string>();

            callbackLogs.Clear();
            instance.AdvanceTimeBy(10);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.PostCallback(() => callbackLogs.Add("cac4e224-15b6-45af-8df4-0a4d43b2ae05"));
            instance.PostCallback(() => callbackLogs.Add("757d903d-376f-4e5f-accf-371fd5f06c3d"));
            instance.PostCallback(() => callbackLogs.Add("245bd145-a538-49b8-b7c8-733f77e5d245"));
            instance.AdvanceTimeBy(0);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "cac4e224-15b6-45af-8df4-0a4d43b2ae05", "757d903d-376f-4e5f-accf-371fd5f06c3d",
                "245bd145-a538-49b8-b7c8-733f77e5d245" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(0);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.ScheduleTimeout(5, () =>
                callbackLogs.Add("3978252e-188f-4f03-96e2-8036f13dfae2"));
            instance.ScheduleTimeout(6, () =>
                callbackLogs.Add("e1e039a0-c83a-43da-8f29-81725eb7147f"));
            var testTimeoutId = instance.ScheduleTimeout(11, () =>
                callbackLogs.Add("ebf9dd1d-7157-420a-ac16-00a3fde9bf4e"));
            instance.AdvanceTimeBy(4);
            Assert.Equal(14, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(1);
            Assert.Equal(15, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "3978252e-188f-4f03-96e2-8036f13dfae2" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(1);
            Assert.Equal(16, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "e1e039a0-c83a-43da-8f29-81725eb7147f" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(4);
            Assert.Equal(20, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            // test repeated cancellation of same id doesn't cause problems.
            instance.CancelTimeout(testTimeoutId);
            instance.PostCallback(() =>
                callbackLogs.Add("6d3a5586-b81d-4ca5-880b-2b711881a14e"));
            testTimeoutId = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add("8722d9a6-a7d4-47fe-a6d4-eee624fb0740"));
            instance.ScheduleTimeout(4, () =>
                callbackLogs.Add("2f7deeb1-f857-4f29-82de-b4168133f093"));
            var testTimeoutId2 = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add("42989f22-a6d1-48ff-a554-86f79e87321e"));
            instance.ScheduleTimeout(0, () =>
                callbackLogs.Add("9b463fec-6a9c-44cc-8165-e106080b18fc"));
            instance.PostCallback(() =>
                callbackLogs.Add("56805433-1f02-4327-b190-50862c0ba93e"));
            Assert.Equal(new List<string>(), callbackLogs);
            instance.AdvanceTimeBy(2);
            Assert.Equal(new List<string> {
                "6d3a5586-b81d-4ca5-880b-2b711881a14e",
                "9b463fec-6a9c-44cc-8165-e106080b18fc",
                "56805433-1f02-4327-b190-50862c0ba93e" }, callbackLogs);
            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            instance.AdvanceTimeBy(3);
            Assert.Equal(25, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "42989f22-a6d1-48ff-a554-86f79e87321e",
                "2f7deeb1-f857-4f29-82de-b4168133f093" }, callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            // test repeated cancellation of same id doesn't cause problems.
            instance.CancelTimeout(testTimeoutId);
            instance.PostCallback(() =>
                callbackLogs.Add("6d3a5586-b81d-4ca5-880b-2b711881a14e"));
            instance.ScheduleTimeout(3, () =>
                callbackLogs.Add("8722d9a6-a7d4-47fe-a6d4-eee624fb0740"));
            instance.ScheduleTimeout(4, () =>
                callbackLogs.Add("2f7deeb1-f857-4f29-82de-b4168133f093"));
            testTimeoutId = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add("42989f22-a6d1-48ff-a554-86f79e87321e"));
            instance.ScheduleTimeout(0, () =>
                callbackLogs.Add("9b463fec-6a9c-44cc-8165-e106080b18fc"));
            instance.PostCallback(() =>
                callbackLogs.Add("56805433-1f02-4327-b190-50862c0ba93e"));
            Assert.Equal(new List<string>(), callbackLogs);
            instance.AdvanceTimeBy(5);
            Assert.Equal(30, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "6d3a5586-b81d-4ca5-880b-2b711881a14e",
                "9b463fec-6a9c-44cc-8165-e106080b18fc",
                "56805433-1f02-4327-b190-50862c0ba93e",
                "8722d9a6-a7d4-47fe-a6d4-eee624fb0740",
                "42989f22-a6d1-48ff-a554-86f79e87321e",
                "2f7deeb1-f857-4f29-82de-b4168133f093" }, callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId); // test already used timeout cancellation isn't a problem.
            instance.CancelTimeout(testTimeoutId2); // test already used timeout isn't a problem.
            instance.CancelTimeout(null); // test unexpected doesn't cause problems.
            instance.CancelTimeout("jal"); // test unexpected doesn't cause problems.
            instance.AdvanceTimeBy(5);
            Assert.Equal(35, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);
        }

        [Fact]
        public void TestErrorUsage()
        {
            var instance = new FakeEventLoopApi();
            Assert.ThrowsAny<Exception>(() =>
            {
                instance.AdvanceTimeBy(-1);
            });
            Assert.ThrowsAny<Exception>(() =>
            {
                instance.ScheduleTimeout(-1, () => { });
            });
        }
    }
}
