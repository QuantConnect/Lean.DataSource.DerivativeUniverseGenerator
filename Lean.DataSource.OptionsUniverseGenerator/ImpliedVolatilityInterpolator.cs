/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2024 QuantConnect Corporation.
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
using QuantConnect.Indicators;
using QuantConnect.Data.Market;
using Accord.Statistics.Models.Regression.Linear;
using MathNet.Numerics.RootFinding;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Interpolates implied volatility for options with missing values
    /// </summary>
    internal class ImpliedVolatilityInterpolator
    {
        private decimal _underlyingPrice;
        private DateTime _referenceDate;
        private MultipleLinearRegression _model;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpliedVolatilityInterpolator"/> class
        /// </summary>
        /// <param name="referenceDate">The reference date of the data</param>
        /// <param name="entries">The original universe entries</param>
        /// <param name="underlyingPrice">The underlying price at the processing time</param>
        /// <param name="numberOfEntriesWithValidIv">The number of entries with missing IV</param>
        public ImpliedVolatilityInterpolator(DateTime referenceDate, List<OptionUniverseEntry> entries, decimal underlyingPrice, int numberOfEntriesWithValidIv)
        {
            if (entries.Count <= numberOfEntriesWithValidIv)
            {
                throw new ArgumentException("The number of entries with valid implied volatility must be less than the total number of entries.");
            }

            _underlyingPrice = underlyingPrice;
            _referenceDate = referenceDate;

            var modelInputs = new double[numberOfEntriesWithValidIv][];
            var modelOutputs = new double[numberOfEntriesWithValidIv];
            for (int i = 0, j = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var iv = entry.ImpliedVolatility ?? 0;
                if (iv == 0) continue;
                modelInputs[j] = GetModelInput(entry.Symbol.ID.StrikePrice, entry.Symbol.ID.Date, iv);
                modelOutputs[j++] = (double)iv;
            }

            var ols = new OrdinaryLeastSquares() { UseIntercept = true };
            _model = ols.Learn(modelInputs, modelOutputs);
        }

        /// <summary>
        /// Interpolates the implied volatility for the given strike and expiry
        /// </summary>
        public decimal Interpolate(decimal strike, DateTime expiry)
        {
            Func<double, double> f = (volatility) =>
            {
                var input = GetModelInput(strike, expiry, Convert.ToDecimal(volatility));
                var calculatedIv = _model.Transform(input);
                return volatility - calculatedIv;
            };
            return Convert.ToDecimal(Brent.FindRoot(f, 1e-7d, 4.0d, 1e-4d, 100));
        }

        /// <summary>
        /// Gets an updated instance of <see cref="GreeksIndicators"/> with the interpolated implied volatility
        /// </summary>
        public GreeksIndicators GetUpdatedGreeksIndicators(Symbol option, decimal interpolatedIv, OptionPricingModelType? optionModel = null,
            OptionPricingModelType? ivModel = null)
        {
            var greeksIndicators = new GreeksIndicators(option, null, optionModel, ivModel);
            var timeTillExpiry = Convert.ToDecimal(OptionGreekIndicatorsHelper.TimeTillExpiry(option.ID.Date, _referenceDate));
            var interest = greeksIndicators.InterestRate;
            var dividend = greeksIndicators.DividendYield;

            // Use BSM for speed
            var optionPrice = OptionGreekIndicatorsHelper.BlackTheoreticalPrice(interpolatedIv, _underlyingPrice, option.ID.StrikePrice,
                timeTillExpiry, interest, dividend, option.ID.OptionRight);

            greeksIndicators.Update(new TradeBar { Symbol = option.Underlying, EndTime = _referenceDate, Close = _underlyingPrice });
            greeksIndicators.Update(new TradeBar { Symbol = option, EndTime = _referenceDate, Close = optionPrice });

            return greeksIndicators;
        }

        /// <summary>
        /// Gets a properly formatted input for the numerical model based on the given strike, expiry and implied volatility
        /// </summary>
        private double[] GetModelInput(decimal strike, DateTime expiry, decimal iv)
        {
            var moneyness = GetMoneyness(strike, expiry, iv);
            var timeTillExpiry = OptionGreekIndicatorsHelper.TimeTillExpiry(expiry, _referenceDate);
            return new double[]
            {
                moneyness,
                timeTillExpiry,
                moneyness * moneyness,
                timeTillExpiry * timeTillExpiry,
                timeTillExpiry * moneyness
            };
        }

        private double GetMoneyness(decimal strike, DateTime expiry, decimal iv)
        {
            var timeTillExpiry = OptionGreekIndicatorsHelper.TimeTillExpiry(expiry, _referenceDate);
            return Math.Log((double)(strike / _underlyingPrice)) / (double)iv / Math.Sqrt(timeTillExpiry);
        }
    }
}
