using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Schedulers;
using log4net;
using System.Web;
using EasyNetQ.SystemMessages;


namespace EasyNetQ.Hosepipe.Retry
{
    public static class BusExtensionMethods
    {
        /// <summary>
        /// Set the ContextId to a random Guid as each message is received
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bus"></param>
        /// <param name="subscriptionId"></param>
        /// <param name="action"></param>
        /// <param name="degreeOfParallelism"></param>
        public static void SubscribeWithWrapper<T>(this IBus bus, string subscriptionId, Action<Error> action, int degreeOfParallelism = 2) where T : class
        {
            var ts = new LimitedConcurrencyLevelTaskScheduler(degreeOfParallelism);
            var taskFactory = new TaskFactory(ts);

            // When a message comes in, start a new task with the limited concurrency task scheduler.
            // The task will set the contextid and then call through to the actual message handler
            Func<Error, Task> task = message => taskFactory.StartNew(
                () =>
                {
                    var previous = GlobalContext.Properties["ContextId"] == null ? null : GlobalContext.Properties["ContextId"].ToString();
                    AdaptivePropertyManager.SetContextProperty("ContextId", Guid.NewGuid().ToString());
                    try
                    {
                        action(message);
                    }
                    finally
                    {
                        AdaptivePropertyManager.SetContextProperty("ContextId", previous);
                    }
                },
                TaskCreationOptions.LongRunning
            ).ContinueWith(taskCompletion =>
            {
                if (taskCompletion.IsCompleted && !taskCompletion.IsFaulted)
                {

                }
                else
                {
                    throw new EasyNetQException("Message processing exception - check the default error queue", taskCompletion.Exception);
                }
            });
            
            // Run the task above when a message comes in 
            bus.SubscribeAsyncError<Error>(subscriptionId, task);
        }



        /// <summary>
        /// Log4Net's use of ThreadContext runs into problems with asp.net's thread agility.
        /// This class works around these issues by storing properties on the HttpContext when it exists.
        /// 
        /// A thorough description of the issue can be found here: http://blog.marekstoj.com/2011/12/log4net-contextual-properties-and.html
        /// </summary>
        public static class AdaptivePropertyManager
        {
            public static void SetContextProperty(string key, string value)
            {
                if (!(GlobalContext.Properties[key] is AdaptiveProperty))
                {
                    SetGlobal(key);
                }
                if (HttpContext.Current != null)
                {
                    HttpContext.Current.Items[key] = value;
                }
                else
                {
                    LogicalThreadContext.Properties[key] = value;
                }

            }

            public static void SetGlobal(string key, string globalValue = null)
            {
                GlobalContext.Properties[key] = new AdaptiveProperty(key, globalValue);
            }
        }

        // Returns the property value in the HttpContext if it exists
        // Falls back to the Log4Net context otherwise
        internal class AdaptiveProperty
        {
            private readonly string _propertyName;
            private readonly string _globalValue;

            public AdaptiveProperty(string propertyName, string globalValue = null)
            {
                _propertyName = propertyName;
                _globalValue = globalValue;
            }

            // Log4Net calls the ToString method when printing a property, 
            // so utilize that to return objects persisted on HttpContext.Current.Items
            public override string ToString()
            {
                if (HttpContext.Current != null && HttpContext.Current.Items[_propertyName] != null)
                {
                    return HttpContext.Current.Items[_propertyName].ToString();
                }
                // If there's no http context or property of that name, use the log4net contexts
                return (
                    LogicalThreadContext.Properties[_propertyName]
                    ?? log4net.ThreadContext.Properties[_propertyName]
                    ?? _globalValue
                ).ToString();
                return "";
            }
        }

    }
}
