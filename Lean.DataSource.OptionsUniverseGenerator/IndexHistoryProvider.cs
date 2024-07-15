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
using NodaTime;
using RestSharp;
using Newtonsoft.Json;
using System.Threading;
using QuantConnect.Data;
using QuantConnect.Logging;
using QuantConnect.Data.Market;
using System.Collections.Generic;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Util;
using QuantConnect.Securities;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Index history provider, will use yahoo finance to fetch latest prices
    /// </summary>
    public partial class IndexHistoryProvider : SynchronizingHistoryProvider
    {
        private bool _securityTypeLog;
        private bool _resolutionLog;
        private bool _dataTypeLog;
        private bool _useDailyPreciseEndTime;
        private readonly static string YahooFinanceApiUrl = "https://query1.finance.yahoo.com/v8/finance";

        private readonly RestClient _restClient = new(YahooFinanceApiUrl);

        /// <summary>
        /// Initializes this history provider to work for the specified job
        /// </summary>
        /// <param name="parameters">The initialization parameters</param>
        public override void Initialize(HistoryProviderInitializeParameters parameters)
        {
            _useDailyPreciseEndTime = parameters.AlgorithmSettings.DailyPreciseEndTime;
            AlgorithmSettings = parameters.AlgorithmSettings;
        }

        /// <summary>
        /// Gets the history for the requested securities
        /// </summary>
        /// <param name="requests">The historical data requests</param>
        /// <param name="sliceTimeZone">The time zone used when time stamping the slice instances</param>
        /// <returns>An enumerable of the slices of data covering the span specified in each request</returns>
        public override IEnumerable<Slice> GetHistory(IEnumerable<HistoryRequest> requests, DateTimeZone sliceTimeZone)
        {
            var subscriptions = new List<Subscription>();
            foreach (var request in requests)
            {
                var history = GetHistory(request);
                if (history == null)
                {
                    continue;
                }
                var subscription = CreateSubscription(request, history);
                subscriptions.Add(subscription);
            }

            if (subscriptions.Count == 0)
            {
                return null;
            }

            return CreateSliceEnumerableFromSubscriptions(subscriptions, sliceTimeZone);
        }

        /// <summary>
        /// Gets the history for the requested security
        /// </summary>
        /// <param name="request">The historical data request</param>
        /// <returns>An enumerable of BaseData points</returns>
        public IEnumerable<BaseData> GetHistory(HistoryRequest request)
        {
            if (request.Symbol.SecurityType != SecurityType.Index)
            {
                if (!_securityTypeLog)
                {
                    _securityTypeLog = true;
                    Log.Error($"IndexHistoryProvider.GetHistory(): Invalid security type. " +
                        $"The {nameof(IndexHistoryProvider)} can only provide history for {SecurityType.Index} securities.");
                }
                return null;
            }

            if (request.Resolution != Resolution.Daily)
            {
                if (!_resolutionLog)
                {
                    _resolutionLog = true;
                    Log.Error($"IndexHistoryProvider.GetHistory(): Invalid resolution. " +
                        $"The {nameof(IndexHistoryProvider)} can only provide history for {Resolution.Daily} resolution.");
                }
                return null;
            }

            if (request.TickType != TickType.Trade)
            {
                if (!_dataTypeLog)
                {
                    _dataTypeLog = true;
                    Log.Error($"IndexHistoryProvider.GetHistory(): Invalid tick type. " +
                        $"The {nameof(IndexHistoryProvider)} can only provide history for {TickType.Trade} tick type.");
                }
                return null;
            }

            var symbol = $"^{request.Symbol.Value}";
            var start = Time.DateTimeToUnixTimeStamp(request.StartTimeUtc);
            var end = Time.DateTimeToUnixTimeStamp(request.EndTimeUtc);

            // let's retry on failure
            for (var retryCount = 0; retryCount < 5; retryCount++)
            {
                var restRequest = new RestRequest($"chart/{symbol}");
                restRequest.AddQueryParameter("period1", start.ToString());
                restRequest.AddQueryParameter("period2", end.ToString());
                restRequest.AddQueryParameter("interval", "1d");
                restRequest.AddQueryParameter("includePrePost", request.IncludeExtendedMarketHours.ToString());

                try
                {
                    var response = _restClient.Get(restRequest);
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Log.Error($"IndexHistoryProvider.GetHistory(): Failed to get history for {symbol}. Status code: {response.StatusCode}.");
                        Thread.Sleep(Time.OneSecond);
                        continue;
                    }

                    var content = response.Content;
                    var indexPrices = JsonConvert.DeserializeObject<YahooFinanceIndexPrices>(content);
                    if (indexPrices == null)
                    {
                        Log.Error($"IndexHistoryProvider.GetHistory(): Failed to deserialize response for {symbol}.");
                        Thread.Sleep(Time.OneSecond);
                        continue;
                    }
                    return ParseHistory(request.Symbol, indexPrices, request.ExchangeHours, request.DataTimeZone);
                }
                catch (Exception exception)
                {
                    Log.Error($"IndexHistoryProvider.GetHistory(): Failed to parse response for {symbol}. Exception: {exception}");
                    Thread.Sleep(Time.OneSecond);
                    continue;
                }
            }
            return null;
        }

        private IEnumerable<BaseData> ParseHistory(Symbol symbol, YahooFinanceIndexPrices indexPrices, SecurityExchangeHours exchange, DateTimeZone dataTimeZone)
        {
            for (int i = 0; i < indexPrices.Timestamps.Count; i++)
            {
                var time = Time.UnixTimeStampToDateTime(indexPrices.Timestamps[i]).ConvertTo(dataTimeZone, exchange.TimeZone);
                var endTime = DateTime.MaxValue;
                if (!_useDailyPreciseEndTime)
                {
                    time = time.Date;
                    endTime = time.AddDays(1);
                }
                else
                {
                    endTime = LeanData.GetNextDailyEndTime(symbol, time, exchange);
                }

                var open = indexPrices.OpenPrices[i];
                var high = indexPrices.HighPrices[i];
                var low = indexPrices.LowPrices[i];
                var close = indexPrices.ClosePrices[i];
                var volume = indexPrices.Volumes[i];

                yield return new TradeBar(time.ConvertTo(dataTimeZone, exchange.TimeZone), symbol, open, high, low, close, volume) { EndTime = endTime };
            }
        }
    }
}