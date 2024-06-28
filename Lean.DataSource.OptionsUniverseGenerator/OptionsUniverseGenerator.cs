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
using System.IO;
using System.Linq;
using Lean.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Options Universe generator
    /// </summary>
    public class OptionsUniverseGenerator : DerivativeUniverseGenerator.DerivativeUniverseGenerator
    {
        private IHistoryProvider _underlyingHistoryProvider;

        /// <summary>
        /// Use to force using the base history provider for underlying securities.
        /// If false, index prices will be fetched using the <see cref="IndexHistoryProvider"/>.
        /// </summary>
        protected virtual bool ForceUseBaseUnderlyingHistoryProvider => false;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="securityType">Option security type to process</param>
        /// <param name="market">Market of data to process</param>
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        public OptionsUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot)
            : base(processingDate, securityType, market, dataFolderRoot, outputFolderRoot)
        {
            if (securityType != SecurityType.Option && securityType != SecurityType.IndexOption)
            {
                throw new ArgumentException($"Only {nameof(SecurityType.Option)} and {nameof(SecurityType.IndexOption)} are supported", nameof(securityType));
            }
        }

        protected override IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol)
        {
            return new OptionUniverseEntry(symbol);
        }

        /// <summary>
        /// Generates the file name where the derivative's universe entry will be saved.
        /// </summary>
        protected override string GetUniverseFileName(Symbol canonicalSymbol)
        {
            var universeDirectory = _securityType switch
            {
                SecurityType.Option => Path.Combine(_universesOutputFolderRoot, canonicalSymbol.Underlying.Value.ToLowerInvariant()),
                SecurityType.IndexOption => Path.Combine(_universesOutputFolderRoot, canonicalSymbol.ID.Symbol.ToLowerInvariant()),
                SecurityType.FutureOption => Path.Combine(_universesOutputFolderRoot,
                    canonicalSymbol.ID.Symbol.ToLowerInvariant(),
                    $"{canonicalSymbol.ID.Date:yyyyMMdd}"),
                _ => throw new ArgumentOutOfRangeException(nameof(canonicalSymbol), $"Unsupported security type: {_securityType}")
            };

            Directory.CreateDirectory(universeDirectory);

            var marketHoursEntry = _marketHoursDatabase.GetEntry(canonicalSymbol.ID.Market, canonicalSymbol, canonicalSymbol.SecurityType);
            var previousTradingDate = Time.GetStartTimeForTradeBars(
                marketHoursEntry.ExchangeHours,
                _processingDate.ConvertTo(marketHoursEntry.DataTimeZone, marketHoursEntry.ExchangeHours.TimeZone),
                Time.OneDay,
                1,
                false,
                marketHoursEntry.DataTimeZone).ConvertTo(marketHoursEntry.ExchangeHours.TimeZone, marketHoursEntry.DataTimeZone);
            return Path.Combine(universeDirectory, $"{previousTradingDate:yyyyMMdd}.csv");
        }

        /// <summary>
        /// Adds a request for the mirror option symbol to the base list of requests.
        /// </summary>
        protected override HistoryRequest[] GetDerivativeHistoryRequests(Symbol symbol, DateTime start, DateTime end, MarketHoursDatabase.Entry marketHoursEntry)
        {
            // To avoid derivatives history requests, return an empty array
            // return Array.Empty<HistoryRequest>();

            var requests = base.GetDerivativeHistoryRequests(symbol, start, end, marketHoursEntry);

            var mirrorOptionSymbol = OptionsUniverseGeneratorUtils.GetMirrorOptionSymbol(symbol);
            var mirrorOptionHistoryRequests = base.GetDerivativeHistoryRequests(mirrorOptionSymbol, start, end, marketHoursEntry);

            return requests.Concat(mirrorOptionHistoryRequests).ToArray();
        }

        /// <summary>
        /// Gets the history provider to use for the underlying security prices.
        /// It will use the <see cref="IndexHistoryProvider"/> for indices.
        /// </summary>
        protected override IHistoryProvider GetUnderlyingHistoryProvider(IDataProvider dataProvider, ZipDataCacheProvider dataCacheProvider)
        {
            if (_underlyingHistoryProvider == null)
            {
                if (!ForceUseBaseUnderlyingHistoryProvider && _securityType == SecurityType.IndexOption)
                {
                    _underlyingHistoryProvider = new IndexHistoryProvider();
                    _underlyingHistoryProvider.Initialize(null);
                }
                else
                {
                    _underlyingHistoryProvider = base.GetUnderlyingHistoryProvider(dataProvider, dataCacheProvider);
                }
            }

            return _underlyingHistoryProvider;
        }
    }
}