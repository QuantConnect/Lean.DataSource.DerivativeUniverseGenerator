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
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Util;
using QuantConnect.Securities;
using QuantConnect.DataSource.DerivativeUniverseGenerator;
using System.Collections.Generic;
using QuantConnect.Logging;
using QuantConnect.Interfaces;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Options Universe generator
    /// </summary>
    public class OptionsUniverseGenerator : DerivativeUniverseGenerator.DerivativeUniverseGenerator
    {
        private static readonly SecurityType[] _supportedSecurityTypes = { SecurityType.Option, SecurityType.IndexOption, SecurityType.FutureOption };

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionsUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="securityType">Option security type to process</param>
        /// <param name="market">Market of data to process</param>
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        /// <param name="dataProvider">The data provider to use</param>
        /// <param name="dataCacheProvider">The data cache provider to use</param>
        /// <param name="historyProvider">The history provider to use</param>
        public OptionsUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot, IDataProvider dataProvider, IDataCacheProvider dataCacheProvider, IHistoryProvider historyProvider)
            : base(processingDate, securityType, market, dataFolderRoot, outputFolderRoot, dataProvider, dataCacheProvider, historyProvider)
        {
            if (!_supportedSecurityTypes.Contains(securityType))
            {
                throw new ArgumentException($"Only {string.Join(", ", _supportedSecurityTypes)} are supported", nameof(securityType));
            }
        }

        protected override IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol)
        {
            return new OptionUniverseEntry(symbol);
        }

        protected override bool NeedsUnderlyingData()
        {
            // We don't need underlying data for future options, since they don't have greeks, so no need for underlying data for calculation
            return _securityType != SecurityType.FutureOption;
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

        /// <summary>
        /// Generates and the derivative universe entries for the specified canonical symbol.
        /// </summary>
        protected override IEnumerable<IDerivativeUniverseFileEntry> GenerateDerivativeEntries(Symbol canonicalSymbol, List<Symbol> symbols,
            MarketHoursDatabase.Entry marketHoursEntry, List<Slice> underlyingHistory, IDerivativeUniverseFileEntry underlyingEntry)
        {
            var generatedEntries = base.GenerateDerivativeEntries(canonicalSymbol, symbols, marketHoursEntry, underlyingHistory, underlyingEntry);

            if (!OptionUniverseEntry.HasGreeks(canonicalSymbol))
            {
                return generatedEntries;
            }

            var entries = new List<OptionUniverseEntry>();
            var entriesWithMissingIv = new List<OptionUniverseEntry>();
            // Enumerate the base entries to materialize them and check whether IVs are missing and need to be interpolated
            foreach (OptionUniverseEntry entry in generatedEntries)
            {
                entries.Add(entry);
                if (!entry.ImpliedVolatility.HasValue || entry.ImpliedVolatility == 0)
                {
                    // We keep the entries with missing IVs to interpolate them later and avoid iterating through the whole list again
                    entriesWithMissingIv.Add(entry);
                }
            }

            if (entriesWithMissingIv.Count > 0)
            {
                // Interpolate missing IVs and re-generate greeks
                var ivInterpolator = ImpliedVolatilityInterpolator.Create(_processingDate, entries, (underlyingEntry as OptionUniverseEntry).Close,
                    entries.Count - entriesWithMissingIv.Count);

                if (ivInterpolator == null)
                {
                    Log.Error($"Failed to set up IV interpolator for {canonicalSymbol}.");
                }
                else
                {
                    var failedInterpolationsCount = 0;
                    foreach (var entry in entriesWithMissingIv)
                    {
                        var interpolatedIv = 0m;
                        try
                        {
                            interpolatedIv = ivInterpolator.Interpolate(entry.Symbol.ID.StrikePrice, entry.Symbol.ID.Date);
                        }
                        catch
                        {
                            Log.Error($"Failed interpolating IV for {entry.Symbol.Value} :: Underlying price: {(underlyingEntry as OptionUniverseEntry).Close}");
                            failedInterpolationsCount++;
                        }

                        if (interpolatedIv != 0)
                        {
                            var updatedGreeks = ivInterpolator.GetUpdatedGreeksIndicators(entry.Symbol, interpolatedIv);
                            entry.SetGreeksIndicators(updatedGreeks);
                        }
                    }

                    if (failedInterpolationsCount > 0)
                    {
                        Log.Error($"Failed interpolating IV for {failedInterpolationsCount} out of {entriesWithMissingIv.Count} contracts for {canonicalSymbol}.");
                    }
                }
            }

            return entries;
        }
    }
}