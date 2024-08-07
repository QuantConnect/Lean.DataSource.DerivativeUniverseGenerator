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

using Lean.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Representation of an option contract universe entry
    /// </summary>
    public class OptionUniverseEntry : BaseDerivativeUniverseFileEntry
    {
        private GreeksIndicators _greeksIndicators;

        /// <summary>
        /// Option contract's open interest on the processing date.
        /// </summary>
        public decimal? OpenInterest { get; set; }

        /// <summary>
        /// Option contract's implied volatility on the processing date.
        /// </summary>
        public decimal? ImpliedVolatility => _greeksIndicators?.ImpliedVolatility;

        /// <summary>
        /// Option contract's greeks on the processing date.
        /// </summary>
        public Greeks Greeks => _greeksIndicators?.GetGreeks();

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionUniverseEntry"/> class.
        /// </summary>
        /// <param name="symbol"></param>
        public OptionUniverseEntry(Symbol symbol)
           : base(symbol)
        {
            // Options universes contain a line for the underlying: we don't need greeks for it
            if (symbol.SecurityType.IsOption())
            {
                var mirrorOptionSymbol = OptionsUniverseGeneratorUtils.GetMirrorOptionSymbol(symbol);
                _greeksIndicators = new GreeksIndicators(symbol, mirrorOptionSymbol);
            }
        }

        /// <summary>
        /// Updates the option contract's prices, open interest, implied volatility and greeks with the provided data.
        /// </summary>
        public override void Update(Slice slice)
        {
            if (!Symbol.SecurityType.IsOption())
            {
                base.Update(slice);
            }
            else
            {
                if (slice.TryGet<OpenInterest>(Symbol, out var openInterest))
                {
                    OpenInterest = openInterest.Value;
                }

                if (slice.Bars.TryGetValue(Symbol, out var tradeBar))
                {
                    Volume = tradeBar.Volume;
                }

                if (slice.QuoteBars.TryGetValue(Symbol, out var quoteBar))
                {
                    Open = quoteBar.Open;
                    High = quoteBar.High;
                    Low = quoteBar.Low;
                    Close = quoteBar.Close;
                }

                if (slice.Bars.TryGetValue(Symbol.Underlying, out var underlyingTrade))
                {
                    _greeksIndicators.Update(underlyingTrade);
                }

                foreach (var quote in slice.QuoteBars.Values)
                {
                    _greeksIndicators.Update(quote);
                }
            }
        }

        /// <summary>
        /// Returns a CSV representation of the option contract's data.
        /// </summary>
        public override string ToCsv()
        {
            // Use Lean's OptionUniverse class to generate the CSV to avoid writing/reading mistakes
            var optionUniverse = new OptionUniverse(Time.BeginningOfTime, Symbol, Open, High, Low, Close, Volume,
                OpenInterest, ImpliedVolatility, Greeks);

            return optionUniverse.ToCsv();
        }

        internal void SetGreeksIndicators(GreeksIndicators greeksIndicators)
        {
            _greeksIndicators = greeksIndicators;
        }

        /// <summary>
        /// Gets the header of the CSV file
        /// </summary>
        public override string GetHeader()
        {
            return OptionUniverse.CsvHeader;
        }
    }
}