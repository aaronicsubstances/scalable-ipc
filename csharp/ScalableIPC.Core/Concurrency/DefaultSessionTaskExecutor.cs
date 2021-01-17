using ScalableIPC.Core.Abstractions;
using ScalableIPC.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ScalableIPC.Core.Helpers.CustomLogEvent;

namespace ScalableIPC.Core.Concurrency
{
    public class DefaultSessionTaskExecutor : ISessionTaskExecutor
    {
        private readonly LimitedConcurrencyLevelTaskScheduler _throttledTaskScheduler;

        // Even when degree of parallelism is limited to 1, more than 1 pool thread
        // can still take turns to process callbacks.
        // So use lock to guarantee memory consistency (and also to definitely eliminate thread interference errors
        // in the case where degree of parallelism is more than 1).

        // locking also has another useful side-effect in combination with throttled task scheduler's task queue:
        // it guarantees that callbacks posted during processing of a given callback will only get executed after 
        // the current processing is finished, even when degree of parallelism is more than 1.
        private readonly bool _runCallbacksUnderMutex;

        // limit parallelism to one to guarantee that callbacks posted from same thread
        // are executed in order of submission.
        public DefaultSessionTaskExecutor(string sessionId, ISessionTaskExecutorGroup executorGroup):
            this(sessionId, executorGroup, 1, true)
        { }

        // for subclasses, to avoid creation of task scheduler if not needed, or use more degrees of
        // parallelism (e.g. for testing).
        protected internal DefaultSessionTaskExecutor(string sessionId,
            ISessionTaskExecutorGroup executorGroup,
            int maxDegreeOfParallelism, bool runCallbacksUnderMutex)
        {
            SessionId = sessionId;
            _runCallbacksUnderMutex = runCallbacksUnderMutex;
            if (maxDegreeOfParallelism > 0)
            {
                _throttledTaskScheduler = new LimitedConcurrencyLevelTaskScheduler(maxDegreeOfParallelism,
                    executorGroup);
            }
            else
            {
                _throttledTaskScheduler = null;
            }
        }

        public string SessionId { get; }

        public virtual void PostCallback(Action cb)
        {
            var callbackExecutionId = GenerateAndRecordCallbackExecutionId();
            Task.Factory.StartNew(() => {
                try
                {
                    if (_runCallbacksUnderMutex)
                    {
                        lock (this)
                        {
                            cb();
                        }
                    }
                    else
                    {
                        cb();
                    }
                }
                catch (Exception ex)
                {
                    RecordCallbackException(callbackExecutionId,
                        "1e934595-0dcb-423a-966c-5786d1925e3d", ex);
                }
                finally
                {
                    RecordEndOfCallbackExecution(callbackExecutionId);
                }
            }, CancellationToken.None, TaskCreationOptions.None, _throttledTaskScheduler);
        }

        public virtual object ScheduleTimeout(int millis, Action cb)
        {
            var callbackExecutionId = GenerateAndRecordCallbackExecutionId();
            var cts = new CancellationTokenSource();
            Task.Delay(millis, cts.Token).ContinueWith(t =>
            {
                Task.Factory.StartNew(() => {
                    try
                    {
                        if (_runCallbacksUnderMutex)
                        {
                            lock (this)
                            {
                                cb();
                            }
                        }
                        else
                        {
                            cb();
                        }
                    }
                    catch (Exception ex)
                    {
                        RecordCallbackException(callbackExecutionId,
                            "5394ab18-fb91-4ea3-b07a-e9a1aa150dd6", ex);
                    }
                    finally
                    {
                        RecordEndOfCallbackExecution(callbackExecutionId);
                    }
                }, cts.Token, TaskCreationOptions.None, _throttledTaskScheduler);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return cts;
        }

        public virtual void CancelTimeout(object id)
        {
            if (id is CancellationTokenSource source)
            {
                source.Cancel();
            }
        }

        private Guid GenerateAndRecordCallbackExecutionId()
        {
            var callbackExecutionId = Guid.NewGuid();
            CustomLoggerFacade.TestLog(() =>
            {
                var logEvent = new CustomLogEvent(GetType(), "Session task is to be scheduled for execution")
                    .AddProperty(LogDataKeyNewSessionTaskId, callbackExecutionId)
                    .AddProperty(LogDataKeySessionId, SessionId);
                return logEvent;
            });
            return callbackExecutionId;
        }

        private void RecordEndOfCallbackExecution(Guid callbackExecutionId)
        {
            CustomLoggerFacade.TestLog(() =>
            {
                var logEvent = new CustomLogEvent(GetType(), "Session task has finished executing")
                    .AddProperty(LogDataKeyEndingSessionTaskExecutionId, callbackExecutionId)
                    .AddProperty(LogDataKeySessionId, SessionId);
                return logEvent;
            });
        }

        private void RecordCallbackException(Guid callbackExecutionId, string logPosition,
            Exception ex)
        {
            CustomLoggerFacade.Log(() => new CustomLogEvent(GetType(), 
                "Error occured on session task executor during callback processing", ex)
                   .AddProperty(LogDataKeySessionTaskExecutionId, callbackExecutionId)
                   .AddProperty(LogDataKeyLogPositionId, logPosition)
                   .AddProperty(LogDataKeySessionId, SessionId));
        }
    }

    public class DefaultSessionTaskExecutorGroup: ISessionTaskExecutorGroup
    {
        private int _delegatesQueuedOrRunning = 0;
        private readonly int _maxDegreeOfParallelism;

        public DefaultSessionTaskExecutorGroup(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism < 1) throw new ArgumentOutOfRangeException("maxDegreeOfParallelism");
            _maxDegreeOfParallelism = maxDegreeOfParallelism;
        }
        
        public bool ConfirmAddWorker()
        {
            lock (this)
            {
                if (_delegatesQueuedOrRunning < _maxDegreeOfParallelism)
                {
                    _delegatesQueuedOrRunning++;
                    return true;
                }
                return false;
            }
        }

        public void OnWorkerFinished()
        {
            lock (this)
            {
                _delegatesQueuedOrRunning--;
            }
        }
    }
}
