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
using Lean.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Options Universe generator
    /// </summary>
    public class OptionsUniverseGenerator : DerivativeUniverseGenerator.DerivativeUniverseGenerator
    {
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
        }

        protected override IDerivativeUniverseFileEntry CreateDefaultUniverseEntry(Symbol symbol)
        {
            return new OptionUniverseEntry(symbol);
        }

        protected override IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol, Slice slice)
        {
            return new OptionUniverseEntry(symbol, slice);
        }

        protected override HistoryRequest[] GetDerivativeHistoryRequests(Symbol symbol, DateTime start, DateTime end, MarketHoursDatabase.Entry marketHoursEntry)
        {
            var requests = base.GetDerivativeHistoryRequests(symbol, start, end, marketHoursEntry);

            var mirrorOptionSymbol = GetMirrorOptionSymbol(symbol);
            var mirrorOptionHistoryRequest = new HistoryRequest(
                start,
                end,
                typeof(QuoteBar),
                mirrorOptionSymbol,
                _historyResolution,
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                _historyResolution,
                true,
                false,
                DataNormalizationMode.ScaledRaw,
                TickType.Quote);

            return requests.Concat(new[] { mirrorOptionHistoryRequest }).ToArray();
        }

        private static Symbol GetMirrorOptionSymbol(Symbol symbol)
        {
            return Symbol.CreateOption(symbol.Underlying.Value,
                symbol.ID.Market,
                symbol.ID.OptionStyle,
                symbol.ID.OptionRight == OptionRight.Call ? OptionRight.Put : OptionRight.Call,
                symbol.ID.StrikePrice,
                symbol.ID.Date);
        }

        // TODO: This might not be neccessary: why no have IDerivativeUniverseFileEntry have an Update(Slice) method, and the base class calls it on every slice.
        //       The base entry would update price data and the option universe entry would update greeks and implied volatility.
        protected override IDerivativeUniverseFileEntry GenerateDerivativeEntry(Symbol symbol, List<Slice> history, List<Slice> underlyingHistory)
        {
            if (history.Count == 0)
            {
                return CreateDefaultUniverseEntry(symbol);
            }

            var entry = base.GenerateDerivativeEntry(symbol, history, underlyingHistory) as OptionUniverseEntry;

            // Now we add greeks and implied volatility to the entry

            var mirrorOptionSymbol = GetMirrorOptionSymbol(symbol);
            var greeksIndicators = new GreeksIndicators(symbol, mirrorOptionSymbol);

            var enumerator = new SynchronizingSliceEnumerator(history.GetEnumerator(), underlyingHistory.GetEnumerator());

            while (enumerator.MoveNext())
            {
                var currentSlice = enumerator.Current;

                if (currentSlice.QuoteBars.TryGetValue(symbol, out var optionQuoteBar))
                {
                    greeksIndicators.Update(optionQuoteBar);
                }

                if (currentSlice.QuoteBars.TryGetValue(mirrorOptionSymbol, out var mirrorOptionQuoteBar))
                {
                    greeksIndicators.Update(mirrorOptionQuoteBar);
                }

                if (currentSlice.TryGetValue(symbol.Underlying, out var underlyingData))
                {
                    greeksIndicators.Update(underlyingData);
                }
            }

            entry.ImpliedVolatility = greeksIndicators.ImpliedVolatility;
            entry.Greeks = greeksIndicators.GetGreeks();

            return entry;
        }

        /// <summary>
        /// Helper class for the <see cref="OptionsUniverseGenerator" /> generator.
        /// </summary>
        private class OptionUniverseEntry : BaseDerivativeUniverseFileEntry
        {
            public decimal? OpenInterest { get; set; }

            public decimal? ImpliedVolatility { get; set; }

            public Greeks? Greeks { get; set; }

            public OptionUniverseEntry(Symbol symbol)
               : base(symbol)
            {
            }

            public OptionUniverseEntry(Symbol symbol, Slice data)
                : base(symbol, data)
            {
                if (data.Get<OpenInterest>().TryGetValue(symbol, out var openInterest))
                {
                    OpenInterest = openInterest.Value;
                }
            }

            public string ToCsv()
            {
                var csv = base.ToCsv();

                return csv + $"{OpenInterest},{ImpliedVolatility},{Greeks?.Delta},{Greeks?.Gamma},{Greeks?.Vega},{Greeks?.Theta},{Greeks?.Rho}";
            }
        }

        /// <summary>
        /// Helper class that holds and updates the greeks indicators
        /// </summary>
        private class GreeksIndicators
        {
            private readonly Delta _delta;
            private readonly Gamma _gamma;
            private readonly Vega _vega;
            private readonly Theta _theta;
            private readonly Rho _rho;

            public GreeksIndicators(Symbol optionSymbol, Symbol mirrorOptionSymbol)
            {
                var riskFreeInterestRateModel = new InterestRateProvider();
                var funcRiskFreeInterestRateModel = new FuncRiskFreeRateInterestRateModel(
                    (datetime) => riskFreeInterestRateModel.GetInterestRate(datetime));

                var dividendYieldModel = optionSymbol.SecurityType == SecurityType.FutureOption || optionSymbol.SecurityType == SecurityType.IndexOption
                    ? new DividendYieldProvider()
                    : new DividendYieldProvider(optionSymbol.Underlying);

                _delta = new Delta(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _gamma = new Gamma(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _vega = new Vega(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _theta = new Theta(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _rho = new Rho(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
            }

            public void Update(IBaseData data)
            {
                var point = new IndicatorDataPoint(data.Symbol, data.EndTime, data.Price);
                _delta.Update(point);
                _gamma.Update(point);
                _vega.Update(point);
                _theta.Update(point);
                _rho.Update(point);
            }

            public Greeks GetGreeks()
            {
                return new Greeks(_delta, _gamma, _vega, _theta, _rho, 0m);
            }

            public decimal ImpliedVolatility => _delta.ImpliedVolatility;
        }
    }
}