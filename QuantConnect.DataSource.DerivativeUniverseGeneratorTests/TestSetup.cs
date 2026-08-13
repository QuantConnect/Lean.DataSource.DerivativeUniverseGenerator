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
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Util;

namespace QuantConnect.DataSource.DerivativeUniverseGeneratorTests
{
    /// <summary>
    /// Assembly-level test setup
    /// </summary>
    [SetUpFixture]
    public class TestSetup
    {
        /// <summary>
        /// Registers the map file and factor file providers in the composer before any test runs.
        /// Lean resolves them with <see cref="Composer.GetPart{T}"/> (e.g. <see cref="SecurityIdentifier"/> for symbol mapping
        /// and <see cref="Data.DividendYieldProvider"/> for corporate events), which does not compose new instances,
        /// so without this registration those code paths would fail with null providers.
        /// </summary>
        [OneTimeSetUp]
        public void SetUp()
        {
            var dataProvider = Composer.Instance.GetExportedValueByTypeName<IDataProvider>(
                Config.Get("data-provider", "DefaultDataProvider"));
            var mapFileProvider = Composer.Instance.GetExportedValueByTypeName<IMapFileProvider>(
                Config.Get("map-file-provider", "LocalDiskMapFileProvider"));
            mapFileProvider.Initialize(dataProvider);
            var factorFileProvider = Composer.Instance.GetExportedValueByTypeName<IFactorFileProvider>(
                Config.Get("factor-file-provider", "LocalDiskFactorFileProvider"));
            factorFileProvider.Initialize(mapFileProvider, dataProvider);
        }
    }
}
