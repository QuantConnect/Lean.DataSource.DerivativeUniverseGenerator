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
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.DataSource.LeanRepoOptionsUniverseGenerator
{
    /// <summary>
    /// Options Universe generator
    /// </summary>
    public class LeanRepoOptionsUniverseGenerator : OptionsUniverseGenerator.OptionsUniverseGenerator
    {
        private IHistoryProvider _historyProvider;

        protected override Resolution[] Resolutions { get; } = new[] { Resolution.Minute, Resolution.Hour, Resolution.Daily };

        protected override Resolution HistoryResolution { get; } = Resolution.Minute;

        /// <summary>
        /// Initializes a new instance of the <see cref="LeanRepoOptionsUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="securityType">Option security type to process</param>
        /// <param name="market">Market of data to process</param>
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        public LeanRepoOptionsUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot)
            : base(processingDate, securityType, market, dataFolderRoot, outputFolderRoot)
        {
        }

        /// <summary>
        /// Gets the history provider to be used to retrieve the historical data for the universe generation
        /// </summary>
        protected override IHistoryProvider GetSecondaryUnderlyingHistoryProvider()
        {
            if (_historyProvider != null)
            {
                return _historyProvider;
            }

            var api = Composer.Instance.GetExportedValueByTypeName<IApi>(Config.Get("api-handler", "Api"));
            api.Initialize(Globals.UserId, Globals.UserToken, "./ApiInputData");

            var dataProvider = new ApiDataProvider();
            //Composer.Instance.AddPart<IDataProvider>(dataProviderToUse);

            var mapFileProvider = new LocalZipMapFileProvider();
            mapFileProvider.Initialize(dataProvider);

            var factorFileProvider = new LocalZipFactorFileProvider();
            factorFileProvider.Initialize(mapFileProvider, dataProvider);

            _historyProvider = new SubscriptionDataReaderHistoryProvider();
            var parameters = new HistoryProviderInitializeParameters(null, null, dataProvider, new ZipDataCacheProvider(dataProvider),
                mapFileProvider, factorFileProvider, (_) => { }, true, new DataPermissionManager(), null, new AlgorithmSettings());
            _historyProvider.Initialize(parameters);

            return _historyProvider;
        }

        /// <summary>
        /// Adds a request for the mirror option symbol to the base list of requests.
        /// </summary>
        protected override HistoryRequest[] GetDerivativeHistoryRequests(Symbol symbol, DateTime start, DateTime end, MarketHoursDatabase.Entry marketHoursEntry)
        {
            // To avoid derivatives history requests, return an empty array
             return Array.Empty<HistoryRequest>();
        }

        ///// <summary>
        ///// Generates the derivative's underlying universe entry and gets the historical data used for the entry generation.
        ///// </summary>
        ///// <returns>
        ///// The historical data used for the underlying universe entry generation.
        ///// </returns>
        ///// <remarks>
        ///// This method should be overridden to return an empty list (and skipping writing to the stream) if no underlying entry is needed.
        ///// </remarks>
        //protected override void GenerateUnderlyingLine(Symbol underlyingSymbol, MarketHoursDatabase.Entry marketHoursEntry,
        //    StreamWriter writer, out List<Slice> history)
        //{
        //    var historyEnd = _processingDate; // To use the close price of the previous day
        //    var historyStart = Time.GetStartTimeForTradeBars(marketHoursEntry.ExchangeHours, historyEnd, _historyResolution.ToTimeSpan(),
        //        _historyBarCount, true, marketHoursEntry.DataTimeZone);

        //    var underlyingHistoryRequest = new HistoryRequest(
        //        historyStart,
        //        historyEnd,
        //        typeof(TradeBar),
        //        underlyingSymbol,
        //        _historyResolution,
        //        marketHoursEntry.ExchangeHours,
        //        marketHoursEntry.DataTimeZone,
        //        _historyResolution,
        //        true,
        //        false,
        //        DataNormalizationMode.ScaledRaw,
        //        LeanData.GetCommonTickTypeForCommonDataTypes(typeof(TradeBar), _securityType));

        //    history = _historyProvider.GetHistory(new[] { underlyingHistoryRequest },
        //        marketHoursEntry.ExchangeHours.TimeZone).ToList();

        //    var entry = CreateUniverseEntry(underlyingSymbol);

        //    if (history == null || history.Count == 0)
        //    {
        //        // TODO: This is here only for Lean repo data generation. This shouldn't be reached if data is guaranteed to be available.
        //        history = ApiHistoryProvider.Instance.Value.GetHistory(new[] { underlyingHistoryRequest },
        //            marketHoursEntry.ExchangeHours.TimeZone).ToList();

        //        if (history == null || history.Count == 0)
        //        {
        //            underlyingHistoryRequest = new HistoryRequest(underlyingHistoryRequest,
        //                newSymbol: underlyingHistoryRequest.Symbol,
        //                newStartTimeUtc: underlyingHistoryRequest.StartTimeUtc.AddDays(1),
        //                newEndTimeUtc: underlyingHistoryRequest.EndTimeUtc.AddDays(1));

        //            history = _historyProvider.GetHistory(new[] { underlyingHistoryRequest },
        //                marketHoursEntry.ExchangeHours.TimeZone).ToList();

        //            if (history == null || history.Count == 0)
        //            {
        //                writer.WriteLine(entry.ToCsv());
        //                return;
        //            }

        //            entry.Update(history[0]);
        //            writer.WriteLine(entry.ToCsv());
        //            return;
        //        }
        //    }

        //    entry.Update(history[^1]);
        //    writer.WriteLine(entry.ToCsv());
        //}
    }
}