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
        public void TestAdvanceTimeBy()
        {
            var instance = new FakeEventLoopApi();
            Assert.Equal(0, instance.CurrentTimestamp);

            var callbackLogs = new List<string>();

            callbackLogs.Clear();
            instance.AdvanceTimeBy(10);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:cac4e224-15b6-45af-8df4-0a4d43b2ae05"));
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:757d903d-376f-4e5f-accf-371fd5f06c3d"));
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:245bd145-a538-49b8-b7c8-733f77e5d245"));
            instance.AdvanceTimeBy(0);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "10:cac4e224-15b6-45af-8df4-0a4d43b2ae05", "10:757d903d-376f-4e5f-accf-371fd5f06c3d",
                "10:245bd145-a538-49b8-b7c8-733f77e5d245" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(0);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.ScheduleTimeout(5, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:3978252e-188f-4f03-96e2-8036f13dfae2"));
            instance.ScheduleTimeout(6, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:e1e039a0-c83a-43da-8f29-81725eb7147f"));
            var testTimeoutId = instance.ScheduleTimeout(11, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:ebf9dd1d-7157-420a-ac16-00a3fde9bf4e"));
            instance.AdvanceTimeBy(4);
            Assert.Equal(14, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(1);
            Assert.Equal(15, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "15:3978252e-188f-4f03-96e2-8036f13dfae2" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(1);
            Assert.Equal(16, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "16:e1e039a0-c83a-43da-8f29-81725eb7147f" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeBy(4);
            Assert.Equal(20, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            // test repeated cancellation of same id doesn't cause problems.
            instance.CancelTimeout(testTimeoutId);
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:6d3a5586-b81d-4ca5-880b-2b711881a14e"));
            testTimeoutId = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:8722d9a6-a7d4-47fe-a6d4-eee624fb0740"));
            instance.ScheduleTimeout(4, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:2f7deeb1-f857-4f29-82de-b4168133f093"));
            var testTimeoutId2 = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:42989f22-a6d1-48ff-a554-86f79e87321e"));
            instance.ScheduleTimeout(0, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:9b463fec-6a9c-44cc-8165-e106080b18fc"));
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:56805433-1f02-4327-b190-50862c0ba93e"));
            Assert.Equal(new List<string>(), callbackLogs);
            instance.AdvanceTimeBy(2);
            Assert.Equal(new List<string> {
                "20:6d3a5586-b81d-4ca5-880b-2b711881a14e",
                "20:9b463fec-6a9c-44cc-8165-e106080b18fc",
                "20:56805433-1f02-4327-b190-50862c0ba93e" }, callbackLogs);
            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            instance.AdvanceTimeBy(3);
            Assert.Equal(25, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "23:42989f22-a6d1-48ff-a554-86f79e87321e",
                "24:2f7deeb1-f857-4f29-82de-b4168133f093" }, callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            // test repeated cancellation of same id doesn't cause problems.
            instance.CancelTimeout(testTimeoutId);
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:6d3a5586-b81d-4ca5-880b-2b711881a14e"));
            instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:8722d9a6-a7d4-47fe-a6d4-eee624fb0740"));
            instance.ScheduleTimeout(4, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:2f7deeb1-f857-4f29-82de-b4168133f093"));
            testTimeoutId = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:42989f22-a6d1-48ff-a554-86f79e87321e"));
            instance.ScheduleTimeout(0, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:9b463fec-6a9c-44cc-8165-e106080b18fc"));
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:56805433-1f02-4327-b190-50862c0ba93e"));
            Assert.Equal(new List<string>(), callbackLogs);
            instance.AdvanceTimeBy(5);
            Assert.Equal(30, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "25:6d3a5586-b81d-4ca5-880b-2b711881a14e",
                "25:9b463fec-6a9c-44cc-8165-e106080b18fc",
                "25:56805433-1f02-4327-b190-50862c0ba93e",
                "28:8722d9a6-a7d4-47fe-a6d4-eee624fb0740",
                "28:42989f22-a6d1-48ff-a554-86f79e87321e",
                "29:2f7deeb1-f857-4f29-82de-b4168133f093" }, callbackLogs);

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
        public void TestAdvanceTimeTo()
        {
            var instance = new FakeEventLoopApi();
            Assert.Equal(0, instance.CurrentTimestamp);

            var callbackLogs = new List<string>();

            callbackLogs.Clear();
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 10);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:cac4e224-15b6-45af-8df4-0a4d43b2ae05"));
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:757d903d-376f-4e5f-accf-371fd5f06c3d"));
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:245bd145-a538-49b8-b7c8-733f77e5d245"));
            instance.AdvanceTimeTo(instance.CurrentTimestamp);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "10:cac4e224-15b6-45af-8df4-0a4d43b2ae05", "10:757d903d-376f-4e5f-accf-371fd5f06c3d",
                "10:245bd145-a538-49b8-b7c8-733f77e5d245" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeTo(instance.CurrentTimestamp);
            Assert.Equal(10, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.ScheduleTimeout(5, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:3978252e-188f-4f03-96e2-8036f13dfae2"));
            instance.ScheduleTimeout(6, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:e1e039a0-c83a-43da-8f29-81725eb7147f"));
            var testTimeoutId = instance.ScheduleTimeout(11, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:ebf9dd1d-7157-420a-ac16-00a3fde9bf4e"));
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 4);
            Assert.Equal(14, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 1);
            Assert.Equal(15, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "15:3978252e-188f-4f03-96e2-8036f13dfae2" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 1);
            Assert.Equal(16, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "16:e1e039a0-c83a-43da-8f29-81725eb7147f" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 4);
            Assert.Equal(20, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            // test repeated cancellation of same id doesn't cause problems.
            instance.CancelTimeout(testTimeoutId);
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:6d3a5586-b81d-4ca5-880b-2b711881a14e"));
            testTimeoutId = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:8722d9a6-a7d4-47fe-a6d4-eee624fb0740"));
            instance.ScheduleTimeout(4, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:2f7deeb1-f857-4f29-82de-b4168133f093"));
            var testTimeoutId2 = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:42989f22-a6d1-48ff-a554-86f79e87321e"));
            instance.ScheduleTimeout(0, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:9b463fec-6a9c-44cc-8165-e106080b18fc"));
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:56805433-1f02-4327-b190-50862c0ba93e"));
            Assert.Equal(new List<string>(), callbackLogs);
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 2);
            Assert.Equal(new List<string> {
                "20:6d3a5586-b81d-4ca5-880b-2b711881a14e",
                "20:9b463fec-6a9c-44cc-8165-e106080b18fc",
                "20:56805433-1f02-4327-b190-50862c0ba93e" }, callbackLogs);
            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 3);
            Assert.Equal(25, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "23:42989f22-a6d1-48ff-a554-86f79e87321e",
                "24:2f7deeb1-f857-4f29-82de-b4168133f093" }, callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId);
            // test repeated cancellation of same id doesn't cause problems.
            instance.CancelTimeout(testTimeoutId);
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:6d3a5586-b81d-4ca5-880b-2b711881a14e"));
            instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:8722d9a6-a7d4-47fe-a6d4-eee624fb0740"));
            instance.ScheduleTimeout(4, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:2f7deeb1-f857-4f29-82de-b4168133f093"));
            testTimeoutId = instance.ScheduleTimeout(3, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:42989f22-a6d1-48ff-a554-86f79e87321e"));
            instance.ScheduleTimeout(0, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:9b463fec-6a9c-44cc-8165-e106080b18fc"));
            instance.PostCallback(() =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:56805433-1f02-4327-b190-50862c0ba93e"));
            Assert.Equal(new List<string>(), callbackLogs);
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 5);
            Assert.Equal(30, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "25:6d3a5586-b81d-4ca5-880b-2b711881a14e",
                "25:9b463fec-6a9c-44cc-8165-e106080b18fc",
                "25:56805433-1f02-4327-b190-50862c0ba93e",
                "28:8722d9a6-a7d4-47fe-a6d4-eee624fb0740",
                "28:42989f22-a6d1-48ff-a554-86f79e87321e",
                "29:2f7deeb1-f857-4f29-82de-b4168133f093" }, callbackLogs);

            callbackLogs.Clear();
            instance.CancelTimeout(testTimeoutId); // test already used timeout cancellation isn't a problem.
            instance.CancelTimeout(testTimeoutId2); // test already used timeout isn't a problem.
            instance.CancelTimeout(null); // test unexpected doesn't cause problems.
            instance.CancelTimeout("jal"); // test unexpected doesn't cause problems.
            instance.AdvanceTimeTo(instance.CurrentTimestamp + 5);
            Assert.Equal(35, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);
        }

        [Fact]
        public void TestAdvanceTimeIndefinitely()
        {
            var instance = new FakeEventLoopApi();
            Assert.Equal(0, instance.CurrentTimestamp);

            var callbackLogs = new List<string>();

            callbackLogs.Clear();
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:cac4e224-15b6-45af-8df4-0a4d43b2ae05"));
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:757d903d-376f-4e5f-accf-371fd5f06c3d"));
            instance.PostCallback(() => callbackLogs.Add($"{instance.CurrentTimestamp}:245bd145-a538-49b8-b7c8-733f77e5d245"));
            instance.AdvanceTimeIndefinitely();
            Assert.Equal(0, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "0:cac4e224-15b6-45af-8df4-0a4d43b2ae05", "0:757d903d-376f-4e5f-accf-371fd5f06c3d",
                "0:245bd145-a538-49b8-b7c8-733f77e5d245" }, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeIndefinitely();
            Assert.Equal(0, instance.CurrentTimestamp);
            Assert.Equal(new List<string>(), callbackLogs);

            callbackLogs.Clear();
            instance.ScheduleTimeout(5, () =>
            {
                callbackLogs.Add($"{instance.CurrentTimestamp}:3978252e-188f-4f03-96e2-8036f13dfae2");
                instance.ScheduleTimeout(4, () =>
                    callbackLogs.Add($"{instance.CurrentTimestamp}:240fbcc0-9930-4e96-9b62-356458ee0a9f"));
            });
            instance.ScheduleTimeout(6, () =>
                callbackLogs.Add($"{instance.CurrentTimestamp}:e1e039a0-c83a-43da-8f29-81725eb7147f"));
            var testTimeoutId = instance.ScheduleTimeout(11, () =>
            {
                callbackLogs.Add($"{instance.CurrentTimestamp}:ebf9dd1d-7157-420a-ac16-00a3fde9bf4e");
                instance.ScheduleTimeout(3, () =>
                    callbackLogs.Add($"{instance.CurrentTimestamp}:b180111d-3179-4c50-9006-4a7591f05640"));
            });
            instance.AdvanceTimeIndefinitely();
            Assert.Equal(14, instance.CurrentTimestamp);
            Assert.Equal(new List<string> {
                "5:3978252e-188f-4f03-96e2-8036f13dfae2",
                "6:e1e039a0-c83a-43da-8f29-81725eb7147f",
                "9:240fbcc0-9930-4e96-9b62-356458ee0a9f",
                "11:ebf9dd1d-7157-420a-ac16-00a3fde9bf4e",
                "14:b180111d-3179-4c50-9006-4a7591f05640"}, callbackLogs);

            callbackLogs.Clear();
            instance.AdvanceTimeIndefinitely();
            Assert.Equal(14, instance.CurrentTimestamp);
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
                instance.AdvanceTimeTo(-1);
            });
            Assert.ThrowsAny<Exception>(() =>
            {
                instance.ScheduleTimeout(-1, () => { });
            });
        }
    }
}
