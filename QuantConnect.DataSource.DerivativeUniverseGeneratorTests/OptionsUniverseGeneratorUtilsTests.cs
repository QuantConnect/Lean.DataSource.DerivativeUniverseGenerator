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
using QuantConnect.DataSource.OptionsUniverseGenerator;
using QuantConnect.Interfaces;
using QuantConnect.Util;
using System;

namespace QuantConnect.DataSource.DerivativeUniverseGeneratorTests
{
    [TestFixture]
    public class OptionsUniverseGeneratorUtilsTests
    {
        private static TestCaseData[] MirrorOptionTestCases
        {
            get
            {
                var dataProvider = Composer.Instance.GetExportedValueByTypeName<IDataProvider>("DefaultDataProvider");

                var mapFileProvider = Composer.Instance.GetExportedValueByTypeName<IMapFileProvider>("LocalDiskMapFileProvider");
                mapFileProvider.Initialize(dataProvider);

                var factorFileProvider = Composer.Instance.GetExportedValueByTypeName<IFactorFileProvider>("LocalDiskFactorFileProvider");
                factorFileProvider.Initialize(mapFileProvider, dataProvider);

                var spy = Symbol.Create("SPY", SecurityType.Equity, Market.USA);
                var spx = Symbol.Create("SPX", SecurityType.Index, Market.USA);

                var strike = 100m;
                var expiry = new DateTime(2021, 1, 1);

                var spyCall = Symbol.CreateOption(spy, Market.USA, OptionStyle.American, OptionRight.Call, strike, expiry);
                var spyPut = Symbol.CreateOption(spy, Market.USA, OptionStyle.American, OptionRight.Put, strike, expiry);

                var spxCall = Symbol.CreateOption(spx, Market.USA, OptionStyle.European, OptionRight.Call, strike, expiry);
                var spxPut = Symbol.CreateOption(spx, Market.USA, OptionStyle.European, OptionRight.Put, strike, expiry);

                var spxwCall = Symbol.CreateOption(spx, "SPXW", Market.USA, OptionStyle.European, OptionRight.Call, strike, expiry);
                var spxwPut = Symbol.CreateOption(spx, "SPXW", Market.USA, OptionStyle.European, OptionRight.Put, strike, expiry);

                return new[]
                {
                    new TestCaseData(spyCall).Returns(spyPut),
                    new TestCaseData(spyPut).Returns(spyCall),

                    new TestCaseData(spxCall).Returns(spxPut),
                    new TestCaseData(spxPut).Returns(spxCall),

                    new TestCaseData(spxwCall).Returns(spxwPut),
                    new TestCaseData(spxwPut).Returns(spxwCall),
                };
            }
        }

        [TestCaseSource(nameof(MirrorOptionTestCases))]
        public Symbol GetsCorrectMirrorOption(Symbol optionSymbol)
        {
            return OptionsUniverseGeneratorUtils.GetMirrorOptionSymbol(optionSymbol);
        }
    }
}
