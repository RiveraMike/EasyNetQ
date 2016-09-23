using System.Reflection;
using Autofac;
using EasyNetQ;
using EasyNetQ.ConnectionString;

namespace EasyNetQ.Hosepipe.Retry
{
    /// <summary>
    /// Registers EasyNetQ types, including logging and serialization.
    /// 
    /// You need to set ConnectionString upon creation.
    /// </summary>
    public class RabbitModule : Autofac.Module
    {
        // Dependency injected
        // ReSharper disable MemberCanBePrivate.Global
        public string ConnectionString { get; set; }
        public string HeartbeatTimeoutInSeconds { get; set; }
        public string TimeoutInSeconds { get; set; }
        public string Prefetch { get; set; }
        // ReSharper restore MemberCanBePrivate.Global

        // Create a configuraiton object from the connection string, then set the properties we care about
        private ConnectionConfiguration Configuration
        {
            get
            {
                var cc = new ConnectionStringParser().Parse(ConnectionString);

                // EasyNetQ defaults to 10s
                if (HeartbeatTimeoutInSeconds != null)
                {
                    cc.RequestedHeartbeat = ushort.Parse(HeartbeatTimeoutInSeconds);
                }

                // EasyNetQ defaults to 10s
                if (TimeoutInSeconds != null)
                {
                    cc.Timeout = ushort.Parse(TimeoutInSeconds);
                }

                // EasyNetQ defaults to 50
                if (Prefetch != null)
                {
                    cc.PrefetchCount = ushort.Parse(Prefetch);
                }

                return cc;
            }
        }

        protected override void Load(ContainerBuilder builder)
        {
            // Register the custom serializer
            // because the EasyNetQ serializer doesn't work well with JObject.
            // Eventually it'd be nice if that was resolved.
            // See https://ngpvan.atlassian.net/browse/DIGITAL-4912 for more.
            builder.RegisterType<EasyNetQJsonDotNetSerializer.JsonSerializer>().As<ISerializer>();
            builder.RegisterType<TypeNameSerializer>().As<ITypeNameSerializer>();

            // Register the IBus factory, which itself registers the above types with EasyNetQ's own mechanism
            builder.Register(c => RabbitHutch.CreateBus(Configuration,
                serviceRegister =>
                {
                    // If an easyNetQ logger is registered with autofac, use it in EasyNetQ
                    if (c.IsRegistered<IEasyNetQLogger>())
                    {
                        serviceRegister.Register(_ => c.Resolve<IEasyNetQLogger>());
                    }

                    serviceRegister.Register(_ => c.Resolve<ISerializer>());
                })
            )
            .As<IBus>().SingleInstance().AutoActivate();
        }
    }
}
