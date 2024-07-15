/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 *
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Securities;
using QuantConnect.Util;
using System;
using System.Linq;
using QuantConnect.DataSource.OptionsUniverseGenerator;
using Fasterflect;

namespace QuantConnect.DataSource.DerivativeUniverseGeneratorTests
{
    [TestFixture]
    public class IndexHistoryProviderTests
    {
        [TestCase("SPX")]
        [TestCase("NDX")]
        [TestCase("VIX")]
        public void GetsDailyHistory(string ticker)
        {
            var historyProvider = new IndexHistoryProvider();
            historyProvider.Initialize(new HistoryProviderInitializeParameters(null, null, null, null, null, null, (_) => { }, true, null, null,
                new AlgorithmSettings() { DailyPreciseEndTime = true}));

            var symbol = Symbol.Create(ticker, SecurityType.Index, Market.USA);
            var marketHoursEntry = MarketHoursDatabase.FromDataFolder().GetEntry(symbol.ID.Market, symbol, symbol.SecurityType);

            var request = new HistoryRequest(
                new DateTime(2024, 01, 08, 13, 30, 0),
                new DateTime(2024, 02, 08, 13, 30, 0),
                typeof(TradeBar),
                symbol,
                Resolution.Daily,
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                Resolution.Daily,
                true,
                false,
                DataNormalizationMode.Adjusted,
                TickType.Trade);

            var history = historyProvider.GetHistory(new[] { request }, marketHoursEntry.ExchangeHours.TimeZone).ToList();

            Assert.That(history, Is.Not.Null.Or.Empty);
        }

        [Test]
        public void RespectsDailyStrictEndTime([Values] bool dailyPreciseEndTime)
        {
            var historyProvider = new IndexHistoryProvider();
            historyProvider.Initialize(new HistoryProviderInitializeParameters(null, null, null, null, null, null, (_) => { }, true, null, null,
                new AlgorithmSettings() { DailyPreciseEndTime = dailyPreciseEndTime }));

            var symbol = Symbol.Create("SPX", SecurityType.Index, Market.USA);
            var marketHoursEntry = MarketHoursDatabase.FromDataFolder().GetEntry(symbol.ID.Market, symbol, symbol.SecurityType);

            var startDate = new DateTime(2024, 01, 08);

            var request = new HistoryRequest(
                startDate,
                startDate.AddMonths(1),
                typeof(TradeBar),
                symbol,
                Resolution.Daily,
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                Resolution.Daily,
                true,
                false,
                DataNormalizationMode.Adjusted,
                TickType.Trade);

            var history = historyProvider.GetHistory(new[] { request }, marketHoursEntry.ExchangeHours.TimeZone).ToList();
            Assert.That(history, Is.Not.Null.Or.Empty);

            Assert.That(history, Is.Not.Null.Or.Empty.And.Matches<Slice>(slice =>
            {
                var tradeBar = slice.Bars.Values.Single();
                var expectedStart = dailyPreciseEndTime ? marketHoursEntry.ExchangeHours.GetNextMarketOpen(startDate.Date, true) : startDate.Date;
                var expectedEnd = dailyPreciseEndTime ? marketHoursEntry.ExchangeHours.GetNextMarketClose(expectedStart, true) : startDate.Date.AddDays(1);

                return tradeBar.Time == expectedStart && tradeBar.EndTime == expectedEnd;
            }));
        }
    }
}
