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
using QuantConnect.Indicators;
using System.Linq;

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
            base.Update(slice);

            if (Symbol.SecurityType.IsOption())
            {
                if (slice.TryGet<OpenInterest>(Symbol, out var openInterest))
                {
                    OpenInterest = openInterest.Value;
                }
            }

            if (_greeksIndicators != null)
            {
                foreach (var data in slice.AllData.OfType<IBaseDataBar>())
                {
                    _greeksIndicators.Update(data);
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

        /// <summary>
        /// Helper class that holds and updates the greeks indicators
        /// </summary>
        private class GreeksIndicators
        {
            private readonly static IRiskFreeInterestRateModel _interestRateProvider = new InterestRateProvider();

            private readonly Symbol _optionSymbol;
            private readonly Symbol _mirrorOptionSymbol;

            private readonly Delta _delta;
            private readonly Gamma _gamma;
            private readonly Vega _vega;
            private readonly Theta _theta;
            private readonly Rho _rho;

            public GreeksIndicators(Symbol optionSymbol, Symbol mirrorOptionSymbol)
            {
                _optionSymbol = optionSymbol;
                _mirrorOptionSymbol = mirrorOptionSymbol;

                var dividendYieldModel = DividendYieldProvider.CreateForOption(_optionSymbol);

                _delta = new Delta(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol,
                    optionModel: OptionPricingModelType.ForwardTree, ivModel: OptionPricingModelType.ForwardTree);
                _gamma = new Gamma(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol,
                    optionModel: OptionPricingModelType.ForwardTree, ivModel: OptionPricingModelType.ForwardTree);
                _vega = new Vega(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol,
                    optionModel: OptionPricingModelType.ForwardTree, ivModel: OptionPricingModelType.ForwardTree);
                _theta = new Theta(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol,
                    optionModel: OptionPricingModelType.ForwardTree, ivModel: OptionPricingModelType.ForwardTree);
                _rho = new Rho(_optionSymbol, _interestRateProvider, dividendYieldModel, _mirrorOptionSymbol,
                    optionModel: OptionPricingModelType.ForwardTree, ivModel: OptionPricingModelType.ForwardTree);
            }

            public void Update(IBaseDataBar data)
            {
                var point = new IndicatorDataPoint(data.Symbol, data.EndTime, data.Close);

                UpdateIndicator(_delta, point);
                UpdateIndicator(_gamma, point);
                UpdateIndicator(_vega, point);
                UpdateIndicator(_theta, point);
                UpdateIndicator(_rho, point);
            }

            public void UpdateIndicator(OptionGreeksIndicatorBase indicator, IndicatorDataPoint point)
            {
                try
                {
                    indicator.Update(point);
                }
                catch { }
            }

            public Greeks GetGreeks()
            {
                return new Greeks(_delta, _gamma, _vega, _theta, _rho, 0m);
            }

            public decimal ImpliedVolatility => _delta.ImpliedVolatility;
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