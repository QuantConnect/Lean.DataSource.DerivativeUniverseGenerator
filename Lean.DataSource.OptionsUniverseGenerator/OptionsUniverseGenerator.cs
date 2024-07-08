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

using System;
using System.IO;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Util;
using QuantConnect.Securities;
using Lean.DataSource.DerivativeUniverseGenerator;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Options Universe generator
    /// </summary>
    public class OptionsUniverseGenerator : DerivativeUniverseGenerator.DerivativeUniverseGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="securityType">Option security type to process</param>
        /// <param name="market">Market of data to process</param>
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        public OptionsUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot)
            : base(processingDate, securityType, market, dataFolderRoot, outputFolderRoot)
        {
            if (securityType != SecurityType.Option && securityType != SecurityType.IndexOption)
            {
                throw new ArgumentException($"Only {nameof(SecurityType.Option)} and {nameof(SecurityType.IndexOption)} are supported", nameof(securityType));
            }
        }

        protected override IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol)
        {
            return new OptionUniverseEntry(symbol);
        }

        /// <summary>
        /// Generates the file name where the derivative's universe entry will be saved.
        /// </summary>
        protected override string GetUniverseFileName(Symbol canonicalSymbol)
        {
            var universeDirectory = _securityType switch
            {
                SecurityType.Option => Path.Combine(_universesOutputFolderRoot, canonicalSymbol.Underlying.Value.ToLowerInvariant()),
                SecurityType.IndexOption => Path.Combine(_universesOutputFolderRoot, canonicalSymbol.ID.Symbol.ToLowerInvariant()),
                SecurityType.FutureOption => Path.Combine(_universesOutputFolderRoot,
                    canonicalSymbol.ID.Symbol.ToLowerInvariant(),
                    $"{canonicalSymbol.ID.Date:yyyyMMdd}"),
                _ => throw new ArgumentOutOfRangeException(nameof(canonicalSymbol), $"Unsupported security type: {_securityType}")
            };

            Directory.CreateDirectory(universeDirectory);
            return Path.Combine(universeDirectory, $"{_processingDate:yyyyMMdd}.csv");
        }

        /// <summary>
        /// Adds a request for the mirror option symbol to the base list of requests.
        /// </summary>
        protected override HistoryRequest[] GetDerivativeHistoryRequests(Symbol symbol, DateTime start, DateTime end, MarketHoursDatabase.Entry marketHoursEntry)
        {
            var requests = base.GetDerivativeHistoryRequests(symbol, start, end, marketHoursEntry);

            var mirrorOptionSymbol = OptionsUniverseGeneratorUtils.GetMirrorOptionSymbol(symbol);
            var mirrorOptionHistoryRequests = base.GetDerivativeHistoryRequests(mirrorOptionSymbol, start, end, marketHoursEntry);

            return requests.Concat(mirrorOptionHistoryRequests).ToArray();
        }
    }
}