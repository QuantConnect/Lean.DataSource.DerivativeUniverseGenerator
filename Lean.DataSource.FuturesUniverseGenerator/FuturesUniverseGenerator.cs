/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Interfaces;
using QuantConnect.Util;

namespace QuantConnect.DataSource.FuturesUniverseGenerator
{
    /// <summary>
    /// Futures Universe generator
    /// </summary>
    public class FuturesUniverseGenerator : DerivativeUniverseGenerator.DerivativeUniverseGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FuturesUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="market">Market of data to process</param>
        /// <param name="symbolsToProcess">Symbols to process.
        /// If null or empty, all symbols found will be processed</param>
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        /// <param name="dataProvider">The data provider to use</param>
        /// <param name="dataCacheProvider">The data cache provider to use</param>
        /// <param name="historyProvider">The history provider to use</param>
        public FuturesUniverseGenerator(DateTime processingDate, string market, string[] symbolsToProcess,
            string dataFolderRoot, string outputFolderRoot, IDataProvider dataProvider,
            IDataCacheProvider dataCacheProvider, IHistoryProvider historyProvider)
            : base(processingDate, SecurityType.Future, market, symbolsToProcess, dataFolderRoot, outputFolderRoot, dataProvider,
                  dataCacheProvider, historyProvider)
        {
        }

        protected override IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol)
        {
            return new FutureUniverseEntry(symbol);
        }

        protected override Dictionary<Symbol, List<Symbol>> GetSymbols()
        {
            var symbolChainProvider = new FutureChainSymbolProvider(_dataCacheProvider, _processingDate, _securityType, _market, _dataFolderRoot);
            return symbolChainProvider.GetSymbols();
        }

        protected override bool NeedsUnderlyingData()
        {
            return false;
        }

        protected override Dictionary<Symbol, List<Symbol>> FilterSymbols(Dictionary<Symbol, List<Symbol>> symbols,
            string[] symbolsToProcess)
        {
            if (symbolsToProcess.IsNullOrEmpty())
            {
                return symbols;
            }

            return symbols.Where(kvp => symbolsToProcess.Contains(kvp.Key.Value.Replace("/", ""))).ToDictionary();
        }
    }
}