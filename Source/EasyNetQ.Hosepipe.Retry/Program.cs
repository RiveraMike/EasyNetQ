using System;
using System.Configuration;
using System.IO;
using Autofac;
using EasyNetQ.Consumer;
using EasyNetQ.SystemMessages;
using EasyNetQ.Topology;
using log4net;
using log4net.Config;
using RabbitMQ.Client.Exceptions;

namespace EasyNetQ.Hosepipe.Retry
{
    class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));
        private static IBus _bus;
        private static Autofac.IContainer _container;
        static void Main(string[] args)
        {
            ConfigureLog4Net();
            BusExtensionMethods.AdaptivePropertyManager.SetGlobal("ContextId", "global");
            var settings =
                ConfigurationManager.ConnectionStrings["AMQPConnectionString"];
            var amqpConnectionString = settings.ConnectionString;

            var heartbeatTimeoutInSeconds = ConfigurationManager.AppSettings["AmqpHeartbeatTimeout"];
            var prefetch = ConfigurationManager.AppSettings["AmqpPipelinePrefetch"];
            var subscriptionDegreeOfParallelism = int.Parse(ConfigurationManager.AppSettings["AmqpPipelineDop"]);
            var builder = new ContainerBuilder();
            builder.RegisterModule(new RabbitModule
            {
                ConnectionString = amqpConnectionString,
                HeartbeatTimeoutInSeconds = heartbeatTimeoutInSeconds,
                Prefetch = prefetch
            });

            Console.WriteLine(@"starting to BUS");
            _container = builder.Build();
            using (_bus = _container.Resolve<IBus>())
            {
                _bus.SubscribeWithWrapper<Error>("EasyNetQ_Default_Error_Queue", HandleMessage, subscriptionDegreeOfParallelism);
                Console.WriteLine(@"Retry started. Listening for messages. Hit <return> to quit.");
                Console.ReadLine();
            }
        }

        static void HandleMessage(Error msg)
        {
            Console.WriteLine(@"Message received" + msg);
            Log.DebugFormat(msg.ToString());

            //At this point we have received an error message and may want to requeue it
            var queueParameters = new QueueParameters();
            queueParameters.HostName = "localhost";
            queueParameters.Username = "guest";
            queueParameters.Password = "guest";
            RepublishError(msg,queueParameters);
        }

        private static void RepublishError(Error error, QueueParameters parameters)
        {
            
                using (var connection = HosepipeConnection.FromParamters(parameters))
                using (var model = connection.CreateModel())
                {
                    try
                    {
                        if (error.Exchange != string.Empty)
                        {
                            model.ExchangeDeclarePassive(error.Exchange);
                        }
                        
                        var properties = model.CreateBasicProperties();
                        error.BasicProperties.CopyTo(properties);

                        var body = new DefaultErrorMessageSerializer().Deserialize(error.Message);


                        //TODO the exchange in here is the error exchange, but to hosepipe it back it needs to have the real exchange of the failed message.
                        //try{
                            _bus.Advanced.Publish(new Exchange(error.Exchange), error.RoutingKey, false, new MessageProperties(properties), body);
                        //model.BasicPublish(error.Exchange, error.RoutingKey, properties, body);
                                    
                        
                    }
                    catch (OperationInterruptedException)
                    {
                        Console.WriteLine(
                            "The exchange, '{0}', described in the error message does not exist on '{1}', '{2}'",
                            error.Exchange, parameters.HostName, parameters.VHost);
                    }
                }

        }

        private static void ConfigureLog4Net(string configPath = "log4net.config")
        {
            var codebase = new Uri(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase).LocalPath;
            var path = Path.GetDirectoryName(codebase);
            //TODO: Throw exception if path is amazingly null
            var combined = Path.Combine(path, configPath);

            var info = new FileInfo(combined);
            XmlConfigurator.ConfigureAndWatch(info);
            Log.Info("Logging initialized");
        }
    }
}
