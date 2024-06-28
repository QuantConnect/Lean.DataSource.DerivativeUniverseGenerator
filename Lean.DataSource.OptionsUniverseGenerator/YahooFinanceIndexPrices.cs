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

using System.Collections.Generic;
using Newtonsoft.Json;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Yahoo Finance historical prices from Chart API response
    /// </summary>
    [JsonConverter(typeof(YahooFinanceIndexPricesJsonConverter))]
    public class YahooFinanceIndexPrices
    {
        public List<long> Timestamps;

        public List<decimal> OpenPrices;

        public List<decimal> HighPrices;

        public List<decimal> LowPrices;

        public List<decimal> ClosePrices;

        public List<decimal> Volumes;
    }
}