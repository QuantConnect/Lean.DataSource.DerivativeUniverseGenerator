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
using System.Linq;
using QuantConnect.Indicators;
using QuantConnect.Data.Market;
using Accord.Statistics.Models.Regression.Linear;
using MathNet.Numerics.RootFinding;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    public class IvInterpolation
    {
        private decimal _underlyingPrice;
        private DateTime _currentDate;
        private MultipleLinearRegression _model;

        public IvInterpolation(decimal underlyingPrice, DateTime currentDate, IEnumerable<(Symbol Symbol, decimal ImpliedVolatility)> data)
        {
            _underlyingPrice = underlyingPrice;
            _currentDate = currentDate;

            var inputs = data.Select(x => GetInput(x.Symbol.ID.StrikePrice, x.Symbol.ID.Date, x.ImpliedVolatility)).ToArray();
            var outputs = data.Select(x => (double)x.ImpliedVolatility).ToArray();

            var ols = new OrdinaryLeastSquares()
            {
                UseIntercept = true
            };
            _model = ols.Learn(inputs, outputs);
        }

        private double[] GetInput(decimal strike, DateTime expiry, decimal iv)
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
            return (expiry - _currentDate).TotalDays / 365d;
        }

        public decimal GetInterpolatedIv(decimal strike, DateTime expiry)
        {
            Func<double, double> f = (vol) =>
            {
                var input = GetInput(strike, expiry, Convert.ToDecimal(vol));
                var iv = _model.Transform(input);
                return vol - iv;
            };
            return Convert.ToDecimal(Brent.FindRoot(f, 1e-7d, 4.0d, 1e-4d, 100));
        }

        public Greeks GetUpdatedGreeks(Symbol option, decimal polatedIv, OptionPricingModelType? optionModel = null, OptionPricingModelType? ivModel = null)
        {
            var greeks = new OptionUniverseEntry.GreeksIndicators(option, null, optionModel, ivModel);

            var ttm = Convert.ToDecimal((option.ID.Date - _currentDate).TotalDays / 365d);
            var interest = greeks.InterestRate;
            var dividend = greeks.DividendYield;
            
            // Use BSM for speed
            var optionPrice = OptionGreekIndicatorsHelper.BlackTheoreticalPrice(polatedIv, _underlyingPrice, option.ID.StrikePrice, ttm,
                interest, dividend, option.ID.OptionRight);

            greeks.Update(new IndicatorDataPoint(option.Underlying, _currentDate, _underlyingPrice));
            greeks.Update(new IndicatorDataPoint(option, _currentDate, optionPrice));

            return greeks.GetGreeks();
        }
    }
}
