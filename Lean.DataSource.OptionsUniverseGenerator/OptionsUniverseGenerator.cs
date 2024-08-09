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
using System.Collections.Generic;
using QuantConnect.Logging;

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

        /// <summary>
        /// Generates and the derivative universe entries for the specified canonical symbol.
        /// </summary>
        protected override IEnumerable<IDerivativeUniverseFileEntry> GenerateDerivativeEntries(Symbol canonicalSymbol, List<Symbol> symbols,
            MarketHoursDatabase.Entry marketHoursEntry, List<Slice> underlyingHistory, IDerivativeUniverseFileEntry underlyingEntry)
        {
            var entries = new List<OptionUniverseEntry>();
            var entriesWithMissingIv = new List<OptionUniverseEntry>();
            // Enumerate the base entries to materialize them and check whether IVs are missing and need to be interpolated
            foreach (OptionUniverseEntry entry in base.GenerateDerivativeEntries(canonicalSymbol, symbols, marketHoursEntry, underlyingHistory, underlyingEntry))
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