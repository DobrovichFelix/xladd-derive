﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AARC.Mesh.SubService
{
    using System.Collections.Generic;
    using AARC.Mesh.Interface;
    using AARC.Mesh.Model;

    /// <summary>
    /// An adaptor proxy between the business processinmg code and the transport
    /// through input and output qs
    /// </summary>
    public class MeshServiceManager : IObserver<MeshMessage>
    {
        protected readonly DiscoveryServiceStateMachine<MeshMessage> _dssmc;
        protected readonly ILogger _logger;

        private IMeshTransport<MeshMessage> _transportServer;
        private DiscoveryMonitor<DiscoveryMessage> _discoveryMonitor;

        public delegate void MeshMessagePublisherDelegate(string transportId, MeshMessage message);

        public MeshMessagePublisherDelegate ServicePublisher { get; set; }

        public int ListeningPort { get { return _dssmc.Port; } set { _dssmc.Port = value; } }

        public Task DiscoveryService { get; private set; }
        public Task ListenService { get; private set; }
        public Task PublishService { get; private set; }

        public MeshServiceManager(ILogger<MeshServiceManager> logger, DiscoveryServiceStateMachine<MeshMessage> discoveryServiceState, DiscoveryMonitor<DiscoveryMessage> discoveryMonitor, IMeshTransport<MeshMessage> meshTransport)
        {
            _dssmc = discoveryServiceState;
            _logger = logger;
            _discoveryMonitor = discoveryMonitor;
            _discoveryMonitor.DiscoveryReceive = _dssmc.Receive;
            _discoveryMonitor.DiscoverySend = _dssmc.Send;

            _transportServer = meshTransport;

            // MeshMessages from transportserver
            // Todo: But this should really be any transportservice {socketservice}
            _transportServer.Subscribe(this);
        }

        protected ConcurrentDictionary<string, IList<IRouteRegister<MeshMessage>>> routeLookup = new ConcurrentDictionary<string, IList<IRouteRegister<MeshMessage>>>();
        protected ConcurrentDictionary<string, IList<string>> serviceLookup = new ConcurrentDictionary<string, IList<string>>();


        /// <summary>
        /// Register action and q required
        /// </summary>
        /// <param name="route"></param>
        public void RegisterChannels(IRouteRegister<MeshMessage> route)
        {
            route.RegisterReceiverChannels(_dssmc.inputChannels);
            route.RegistePublisherChannels(_dssmc.LocalOutputChannels);

            route.PublishChannel += OnNext;
        }

        public ManualResetEvent RegistrationComplition { get { return _dssmc.RegistrationComplete;  } }
        /// <summary>
        /// We are looking to see if there is an input route for our output for'channel'
        /// </summary>
        /// <param name="channel">output channel</param>
        /// <param name="message">return message/result</param>
        private void OnNext(string channel, MeshMessage message)
        {
            // Check this is one of our channels
            if (_dssmc.LocalOutputChannels.ContainsKey(channel))
            {
                // Is it for external consumption?
                if (_dssmc.ExternalSubscriberChannels.ContainsKey(channel))
                {
                    // Todo: This causes feedback what do I want to do here?
                    //_dssmc.outputChannels[channel].Add(message);
                    message.Service = _transportServer.Url;
                    message.Channel = channel;

                    // Find the external routes
                    var routes = _dssmc.ExternalSubscriberChannels[channel];
                    if (routes == null || !routes.Any())
                        _logger.LogWarning($"NO ROUTE Message GraphId={message.GraphId}, Xid={message.XId}, Channel={channel}");
                    else
                    {
                        if (message.Routes == null)
                            message.Routes = routes;
                        else
                        {
                            var intersection = message.Routes.Intersect(routes);
                            if (intersection.Any())
                                _logger.LogWarning($"ROUTE CONFIRMED Message GraphId={message.GraphId}, Xid={message.XId}, Channel={channel} Routes={string.Join(",",intersection)}");
                            else
                                _logger.LogWarning($"ROUTE NOT FOUND Message GraphId={message.GraphId}, Xid={message.XId}, Channel={channel} Routes={string.Join(",", message.Routes)}");
                        }
                         _transportServer.OnNext(message);
                    }
                }
                else
                {
                    // Todo: No destinations available? Keep on the Q? How to process later?
                    _logger.LogInformation($"NO ROUTE for {channel}");
                }
            }
            else
                // Todo: Unknown destination?
                _logger.LogInformation($"UNKOWN ROUTE for {channel}");
        }

        /// <summary>
        /// When a new MeshMessage arrives check for a match of the action against the input q.
        /// </summary>
        /// <param name="message"></param>
        public void ServiceSubscriber(MeshMessage message)
        {
            // Todo: Has the message come from a known external source? If not what to do?
            var known = _dssmc.RegisteredInputSource(message.Channel, message.Service);

            if (_dssmc.inputChannels.ContainsKey(message.Channel))
            {
                _dssmc.inputChannels[message.Channel].Add(message);
            }
        }

        public Task StartService(Uri discoveryUrl, CancellationToken token)
        {
            DiscoveryService = StartDiscoveryServices(discoveryUrl, token);
            ListenService = StartListeningServices(token);
            PublishService = StartPublisherConnections(token);
            return Task.WhenAll(DiscoveryService, ListenService, PublishService);
        }

        public async Task StartDiscoveryServices(Uri serviceDetails, CancellationToken cancellationToken) => await _discoveryMonitor.StartListeningServices(serviceDetails, cancellationToken);

        /// <summary>
        /// down stream services will lookup the output queues from the DS and find out IP address and connect.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StartListeningServices(CancellationToken cancellationToken)
        {
            RegistrationComplition.WaitOne();
            _logger.LogInformation($"Listener Enabled on {_dssmc.Port}");
            await _transportServer.StartListeningServices(_dssmc.Port, cancellationToken);
        }

        public async Task StartPublisherConnections(CancellationToken cancellationToken)
        {
            await Task.Factory.StartNew(() =>
            {
                RegistrationComplition.WaitOne();
                _logger.LogInformation("Publisher Enabled");
                while (!cancellationToken.IsCancellationRequested)
                {
                    var routableInputQs = _dssmc.RoutableInputChannels();
                    foreach (var routes in _dssmc.InputQRoutes)
                    {
                        _logger?.LogInformation($"ROUTE(i) [{string.Join(",", routes.Item2)}]=>{routes.Item1}=>[{_transportServer.Url}]");
                    }

                    foreach (var routes in _dssmc.OutputQRoutes)
                    {
                        _logger?.LogInformation($"ROUTE(o) [{_transportServer.Url}]=>{routes.Item1}=>[{string.Join(",", routes.Item2)}]");
                    }

                    var channelTransports = _dssmc.OutputQRoutes.SelectMany(r => r.Item2).Distinct();
                    foreach (var transportUrl in channelTransports)
                        try
                        {
                            if (_transportServer.Url != transportUrl)
                            {
                                // If new connection and connected
                                if (_transportServer.ServiceConnect(transportUrl, cancellationToken))
                                {
                                    // Need Channel Names
                                    //_dssmc.OutputChannelRoutes[]
                                    var channels = _dssmc.OutputQRoutes.Where(r => r.Item2.Contains(transportUrl)).Select(r => r.Item1);
                                    _logger?.LogInformation($"{transportUrl} OnConnect {string.Join(",", channels)}");
                                    foreach(var c in channels)
                                    {
                                        if (_dssmc.LocalOutputChannels.ContainsKey(c))
                                        {
                                            var o = _dssmc.LocalOutputChannels[c];
                                            o.OnConnect(transportUrl);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            _logger?.LogError(e, "MSM General Error with connections");
                        }
                        finally
                        {
                            Task.Delay(1000).Wait();
                        }
                    Task.Delay(30000).Wait();
                }
            }, cancellationToken);
        }

        public async Task Cancel() => await _transportServer.Cancel();

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Task.WaitAll(DiscoveryService, ListenService, PublishService);

                    _discoveryMonitor.Dispose();
                    _discoveryMonitor = null;

                    _transportServer.Dispose();
                    _transportServer = null;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~MeshServiceManager()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }

        public void OnCompleted()
        {
            throw new NotImplementedException();
        }

        public void OnError(Exception error)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Messages from transportserver need to be sent to MeshObservables
        /// </summary>
        /// <param name="value"></param>
        public void OnNext(MeshMessage value)
        {
            _dssmc.inputChannels[value.Channel].Add(value);
        }
        #endregion
    }
}
