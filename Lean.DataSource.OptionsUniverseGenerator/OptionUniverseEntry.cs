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

using QuantConnect.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Representation of an option contract universe entry
    /// </summary>
    public class OptionUniverseEntry : BaseContractUniverseFileEntry
    {
        private GreeksIndicators _greeksIndicators;

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
            // Options universes contain a line for the underlying: we don't need greeks for it.
            // Future options don't have greeks either.
            if (HasGreeks(symbol.SecurityType))
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
            base.Update(slice);

            if (_greeksIndicators != null)
            {
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
            return OptionUniverse.ToCsv(Symbol, Open, High, Low, Close, Volume, OpenInterest, ImpliedVolatility, Greeks);
        }

        /// <summary>
        /// Gets the header of the CSV file
        /// </summary>
        public override string GetHeader()
        {
            return OptionUniverse.CsvHeader(Symbol.SecurityType);
        }

        /// <summary>
        /// Sets the greeks indicators for the option contract.
        /// </summary>
        /// <remarks>Internal usage only, in case we need to override greeks and IV, like when interpolating</remarks>
        public void SetGreeksIndicators(GreeksIndicators greeksIndicators)
        {
            _greeksIndicators = greeksIndicators;
        }

        /// <summary>
        /// Returns true if the symbol has greeks.
        /// </summary>
        public static bool HasGreeks(SecurityType securityType)
        {
            return securityType.IsOption() && securityType != SecurityType.FutureOption;
        }
    }
}