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
using QuantConnect.DataSource.DerivativeUniverseGenerator;

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
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        public FuturesUniverseGenerator(DateTime processingDate, string market, string dataFolderRoot, string outputFolderRoot)
            : base(processingDate, SecurityType.Future, market, dataFolderRoot, outputFolderRoot)
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
    }
}