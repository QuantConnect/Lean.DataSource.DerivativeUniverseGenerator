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
using QuantConnect.Data.Market;
using QuantConnect.DataSource.OptionsUniverseGenerator;
using QuantConnect.Indicators;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuantConnect.DataSource.DerivativeUniverseGeneratorTests
{
    [TestFixture]
    public class ImpliedVolatilityInterpolatorTests
    {
        private const string _testFile = "TestData/test.csv";
        private const string _sidHeader = "#symbol_id";
        private const string _tickerHeader = "symbol_value";
        private const string _priceHeader = "close";
        private const string _ivHeader = "implied_volatility";
        private const int _validCount = 236;
        private readonly DateTime _currentDate = new DateTime(2024, 2, 7);

        private List<OptionUniverseEntry> _data;
        private TestImpliedVolatilityInterpolator _interpolator;

        [SetUp]
        public void SetUp()
        {
            var lines = File.ReadAllLines(_testFile);
            var headers = lines[0].Split(',');
            var sidIndex = Array.IndexOf(headers, _sidHeader);
            var tickerIndex = Array.IndexOf(headers, _tickerHeader);
            var priceIndex = Array.IndexOf(headers, _priceHeader);
            var ivIndex = Array.IndexOf(headers, _ivHeader);

            var underlying = lines[1].Split(',');
            var undSymbol = new Symbol(SecurityIdentifier.Parse(underlying[sidIndex]), underlying[tickerIndex]);
            var underlyingPrice = decimal.Parse(underlying[priceIndex]);

            _data = lines.Skip(2)
                .Select(line =>
                {
                    var items = line.Split(',');
                    var symbol = new Symbol(SecurityIdentifier.Parse(items[sidIndex]), items[tickerIndex]);
                    var entry = new OptionUniverseEntry(symbol);
                    entry.Close = decimal.Parse(items[priceIndex]);
                    return entry;
                })
                .ToList();

            foreach (var entry in _data)
            {
                var mirrorSymbol = OptionsUniverseGeneratorUtils.GetMirrorOptionSymbol(entry.Symbol);
                var mirrorEntry = _data.SingleOrDefault(x => x.Symbol == mirrorSymbol);
                if (mirrorEntry == null || entry.Close == 0m || mirrorEntry.Close == 0m) continue;

                var greeks = new GreeksIndicators(entry.Symbol, mirrorSymbol);
                greeks.Update(new TradeBar { Symbol = entry.Symbol, EndTime = _currentDate, Close = entry.Close });
                greeks.Update(new TradeBar { Symbol = mirrorSymbol, EndTime = _currentDate, Close = mirrorEntry.Close });
                greeks.Update(new TradeBar { Symbol = undSymbol, EndTime = _currentDate, Close = underlyingPrice });

                entry.SetGreeksIndicators(greeks);
            }

            _interpolator = new TestImpliedVolatilityInterpolator(_currentDate, _data, underlyingPrice, _validCount);
        }

        [Test]
        public void IvInterpolationAndGreeksGenerationTest()
        {
            // Test on zero IV cases
            var zeroVolatilitySymbols = _data.Where(x => x.ImpliedVolatility == 0m)
                .Select(x => x.Symbol)
                .ToArray();

            foreach (var symbol in zeroVolatilitySymbols)
            {
                var interpolatedIv = _interpolator.Interpolate(symbol.ID.StrikePrice, symbol.ID.Date);

                Assert.Greater(interpolatedIv, 0m);         // domain of IV :-> (0, inf]

                var greekIndicator = _interpolator.GetUpdatedGreeksIndicators(symbol, interpolatedIv, OptionPricingModelType.BlackScholes, 
                    OptionPricingModelType.BlackScholes);
                var greeks = greekIndicator.GetGreeks();

                Assert.NotZero(greeks.Delta);
                // Assert.NotZero(greeks.Gamma);            // Gamma can be zero at very ITM options
                Assert.GreaterOrEqual(greeks.Vega, 0m);     // domain of Vega :-> [0, inf)
                Assert.Less(greeks.Theta, 0m);              // domain of Theta :-> [-price, 0)
                Assert.NotZero(greeks.Rho);
            }
        }

        [TestCase(493.98, 365, 0.5, true, 0)]
        [TestCase(493.98 * Math.E, 365, 0.5, true, 2)]
        [TestCase(493.98 * Math.E, 365, 1, true, 1)]
        [TestCase(493.98 * Math.E, 365 * 4, 0.5, true, 1)]
        [TestCase(0, 365, 0.5, true, double.NegativeInfinity)]          // Log(0)
        [TestCase(500, 0, 0.5, true, double.PositiveInfinity)]          // zero division
        [TestCase(500, 365, 0, true, double.PositiveInfinity)]          // zero division
        [TestCase(-500, 365, 0.5, false, 0)]                            // Log(Neg)
        [TestCase(500, -365, 0.5, false, 0)]                            // Sqrt(Neg)
        public void GetMoneynessTest(decimal strike, int daysTillExpiry, decimal iv, bool success, double expectedMoneyness)
        {
            var expiry = _currentDate.AddDays(daysTillExpiry);
            var actualMoneyness = _interpolator.TestGetMoneyness(strike, expiry, iv);

            if (success)
            {
                Assert.AreEqual(expectedMoneyness, actualMoneyness, 1e-8d);
            }
            else
            {
                Assert.IsNaN(actualMoneyness);
            }
        }
    }

    public class TestImpliedVolatilityInterpolator : ImpliedVolatilityInterpolator
    {
        public TestImpliedVolatilityInterpolator(DateTime referenceDate, List<OptionUniverseEntry> entries, decimal underlyingPrice, int numberOfEntriesWithValidIv)
            : base(referenceDate, entries, underlyingPrice, numberOfEntriesWithValidIv)
        {
        }

        public double TestGetMoneyness(decimal strike, DateTime expiry, decimal iv)
        {
            return GetMoneyness(strike, expiry, iv);
        }
    }
}
