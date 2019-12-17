﻿using System;
using System.Collections.Generic;
using System.Linq;
using AARC.Mesh.Interface;
using AARC.Mesh.Model;
using AARC.Model;
using AARC.Repository.Interfaces;
using Microsoft.Extensions.Logging;

namespace AARC.Mesh.Dataflow
{
    public class BiggestStocks : IMeshReactor<MeshMessage>
    {
        private readonly IStockRepository _stocksRepository;
        private readonly object _sync = new object();
        private readonly MeshObserver<List<Stock>> _observer;
        private readonly IList<IMeshObserver<List<Stock>>> _observers;
        private readonly ILogger<BiggestStocks> _logger;

        public BiggestStocks(ILogger<BiggestStocks> logger, IStockRepository stocksRepository)
        {
            _logger = logger;
            _stocksRepository = stocksRepository;
            _observer = new MeshObserver<List<Stock>>("biggeststocks");
            _observers = new List<IMeshObserver<List<Stock>>> { _observer };

            ChannelRouters = new List<IRouteRegister<MeshMessage>> { _observer as IRouteRegister<MeshMessage>};
        }

        public void UpdateBiggestStocks()
        {
            var stocks = _stocksRepository
                .GetStocksByWithOptionsMarketCap()
                .ToList();

            foreach (var observer in _observers)
                observer?.OnNext(stocks);
        }

        public string Name => "biggeststocks";

        public IList<IRouteRegister<MeshMessage>> ChannelRouters { get; private set; }
    }
}
