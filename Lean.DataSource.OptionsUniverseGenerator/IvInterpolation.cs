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
    internal class IvInterpolation
    {
        private decimal _underlyingPrice;
        private DateTime _referenceDate;
        private MultipleLinearRegression _model;

        public IvInterpolation(DateTime referenceDate, List<OptionUniverseEntry> entries, decimal underlyingPrice, int numberOfEntriesWithValidIv)
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

        private double[] GetModelInput(decimal strike, DateTime expiry, decimal iv)
        {
            var moneyness = GetMoneyness(strike, expiry, iv);
            var ttm = GetTimeTillMaturity(expiry);
            return new double[]
            {
                moneyness,
                ttm,
                moneyness * moneyness,
                ttm * ttm,
                ttm * moneyness
            };
        }

        private double GetMoneyness(decimal strike, DateTime expiry, decimal iv)
        {
            var ttm = GetTimeTillMaturity(expiry);
            return Math.Log((double)(strike / _underlyingPrice)) / (double)iv / Math.Sqrt(ttm);
        }

        private double GetTimeTillMaturity(DateTime expiry)
        {
            return (expiry - _referenceDate).TotalDays / 365d;
        }

        public decimal GetInterpolatedIv(decimal strike, DateTime expiry)
        {
            Func<double, double> f = (vol) =>
            {
                var input = GetModelInput(strike, expiry, Convert.ToDecimal(vol));
                var iv = _model.Transform(input);
                return vol - iv;
            };
            return Convert.ToDecimal(Brent.FindRoot(f, 1e-7d, 4.0d, 1e-4d, 100));
        }

        public GreeksIndicators GetUpdatedGreeksIndicators(Symbol option, decimal interpolatedIv, OptionPricingModelType? optionModel = null,
            OptionPricingModelType? ivModel = null)
        {
            var greeksIndicators = new GreeksIndicators(option, null, optionModel, ivModel);
            var timeToExpiration = Convert.ToDecimal((option.ID.Date - _referenceDate).TotalDays / 365d);
            var interest = greeksIndicators.InterestRate;
            var dividend = greeksIndicators.DividendYield;

            // Use BSM for speed
            var optionPrice = OptionGreekIndicatorsHelper.BlackTheoreticalPrice(interpolatedIv, _underlyingPrice, option.ID.StrikePrice, timeToExpiration,
                interest, dividend, option.ID.OptionRight);

            greeksIndicators.Update(new TradeBar { Symbol = option.Underlying, EndTime = _referenceDate, Close = _underlyingPrice });
            greeksIndicators.Update(new TradeBar { Symbol = option, EndTime = _referenceDate, Close = optionPrice });

            return greeksIndicators;
        }
    }
}
