﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using AARC.Mesh.Model;
using AARC.Mesh.SubService;
using AARC.Mesh.TCP;
using AARC.Model;
using System.Linq;

namespace AARC.Mesh.Client
{
    /// <summary>
    /// Draft Service
    /// Service has a name
    /// Services has a number of methods
    /// </summary>
    class Program
    {
        public static void Main(string[] args)
        {
            ManualResetEvent dsConnectEvent = new ManualResetEvent(false);
            log4net.GlobalContext.Properties["LogFileName"] = $"MeshTestClient";

            var msm = new MeshClient(args);

            var logger = msm.ServiceProvider.GetService<ILoggerFactory>()
                .CreateLogger<Program>();

            logger.LogDebug("Starting application");
            try
            {
                var nasdaqTickers = msm.CreateObservable<TickerPrices>("nasdaqtestout");
                var nasdaqUpdater = msm.CreateObserver<List<string>>("nasdaqtestin");

                nasdaqTickers.Subscribe((tickerprices) =>
                {
                    logger.LogInformation($"{tickerprices.Ticker} Updated {tickerprices.Dates.Max()}-{tickerprices.Dates.Min()}");
                    dsConnectEvent.Set();
                });

                Task.Delay(30000).Wait();
                for (; ; )
                {
                    dsConnectEvent.Reset();
                    logger.LogInformation("Sending Ticker update");
                    var tickers = new List<string> { "AAPL" };
                    nasdaqUpdater.OnNext(tickers);
                    dsConnectEvent.WaitOne();
                }
            }
            finally
            {
                msm.Stop();
                logger.LogInformation("Waiting for death");
            }
            logger.LogDebug("All done!");
        }
    }
}
