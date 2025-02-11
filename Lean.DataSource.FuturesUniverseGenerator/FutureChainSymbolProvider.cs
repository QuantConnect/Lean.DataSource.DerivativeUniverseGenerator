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

using QuantConnect.Configuration;
using QuantConnect.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Securities.Future;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuantConnect.DataSource.FuturesUniverseGenerator
{
    /// <summary>
    /// Futures chain symbol provider used for fetching the futures chain from data file names
    /// </summary>
    public class FutureChainSymbolProvider : ChainSymbolProvider
    {
        private readonly IFutureChainProvider _futuresChainProvider;
        private readonly string _market;

        /// <summary>
        /// Initializes a new instance of the <see cref="FutureChainSymbolProvider"/> class
        /// </summary>
        public FutureChainSymbolProvider(IDataCacheProvider dataCacheProvider, DateTime processingDate, SecurityType securityType,
            string market, string dataFolderRoot)
            : base(dataCacheProvider, processingDate, securityType, market, dataFolderRoot)
        {
            _market = market;

            if (Config.TryGetValue<string>("futures-chain-provider", out var futuresChainProviderStr) &&
                !string.IsNullOrEmpty(futuresChainProviderStr))
            {
                _futuresChainProvider = Composer.Instance.GetExportedValueByTypeName<IFutureChainProvider>(futuresChainProviderStr);
            }
        }

        /// <summary>
        /// Gets all the available symbols keyed by the canonical symbol from the available price data in the data folder.
        /// </summary>
        public override Dictionary<Symbol, List<Symbol>> GetSymbols()
        {
            if (_futuresChainProvider == null)
            {
                return base.GetSymbols();
            }

            var chains = FuturesExpiryFunctions.FuturesExpiryDictionary.Keys
                .Where(symbol => symbol.ID.Market == _market)
                .Select(symbol =>
                {
                    var futureChain = _futuresChainProvider.GetFutureContractList(symbol, _processingDate)?.ToList();
                    return KeyValuePair.Create(symbol, futureChain);
                })
                .ToList();

            if (chains.Any(kvp => kvp.Value == null))
            {
                // The custom chain provider failed for some symbols, fallback to the default chain provider for those
                var baseSymbols = base.GetSymbols();
                if (chains.All(kvp => kvp.Value == null))
                {
                    return baseSymbols;
                }

                return chains
                    .Select(kvp =>
                    {
                        if (kvp.Value == null && baseSymbols.TryGetValue(kvp.Key, out var chain))
                        {
                            return KeyValuePair.Create(kvp.Key, chain);
                        }
                        return kvp;
                    })
                    .Where(kvp => kvp.Value != null)
                    .ToDictionary();
            }

            return new Dictionary<Symbol, List<Symbol>>(chains);
        }

        protected override IEnumerable<string> GetZipFileNames(DateTime date, Resolution resolution)
        {
            var tickTypesLower = _symbolsDataTickTypes.Select(tickType => tickType.TickTypeToLower()).ToArray();

            if (resolution == Resolution.Minute)
            {
                var basePath = Path.Combine(_dataSourceFolder, resolution.ResolutionToLower());
                var dateStr = date.ToString("yyyyMMdd");

                return Directory.EnumerateDirectories(basePath)
                    .Select(directory => tickTypesLower
                        .Select(tickTypeLower => Path.Combine(directory, $"{dateStr}_{tickTypeLower}.zip"))
                        .Where(fileName => File.Exists(fileName))
                        .FirstOrDefault())
                    .Where(fileName => fileName != null);
            }
            // Support for resolutions higher than minute, just for Lean local repo data generation
            else
            {
                try
                {
                    return Directory.EnumerateFiles(Path.Combine(_dataSourceFolder, resolution.ResolutionToLower()), $"*.zip")
                        .Select(fileName =>
                        {
                            var fileInfo = new FileInfo(fileName);
                            var fileNameParts = Path.GetFileNameWithoutExtension(fileInfo.Name).Split('_');
                            var tickTypeIndex = Array.IndexOf(tickTypesLower, fileNameParts[1]);

                            return (fileName, ticker: fileNameParts[0], tickTypeIndex);
                        })
                        // Get only supported tick type data
                        .Where(tuple => tuple.tickTypeIndex > -1)
                        // For each ticker get the first matching tick type file
                        .OrderBy(tuple => tuple.tickTypeIndex)
                        .GroupBy(tuple => tuple.ticker)
                        .Select(group => group.First().fileName);
                }
                catch (DirectoryNotFoundException)
                {
                    return Enumerable.Empty<string>();
                }
            }
        }
    }
}