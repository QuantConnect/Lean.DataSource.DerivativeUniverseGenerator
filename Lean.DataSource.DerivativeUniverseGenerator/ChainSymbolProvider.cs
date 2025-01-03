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
 *
*/

using System;
using System.IO;
using System.Linq;
using QuantConnect.Util;
using QuantConnect.Interfaces;
using System.Collections.Generic;
using QuantConnect.Configuration;

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// File based symbol chain provider
    /// </summary>
    public class ChainSymbolProvider
    {
        private readonly IDataCacheProvider _dataCacheProvider;
        protected readonly DateTime _processingDate;
        protected readonly string _dataSourceFolder;
        protected readonly SecurityType _securityType;

        // 99% of cases will use quote zip files to get the contracts, but in rear cases we may need to use trade zip files. e.g EUREX data
        protected TickType[] _symbolsDataTickTypes = { TickType.Quote, TickType.OpenInterest, TickType.Trade };

        /// <summary>
        /// Careful: using other resolutions might introduce a look-ahead bias. For instance, if Daily resolution is used,
        /// yearly files will be used and the options chain read from disk would contain contracts from all year round,
        /// without considering the actual IPO date of the contract at X point in time of the year.
        /// Made virtual to allow for customization for Lean local repo options universe generator.
        /// </summary>
        protected virtual Resolution[] SymbolSourceResolutions { get; } = new[] { Enum.Parse<Resolution>(Config.Get("symbol-source-resolutions", "minute"), true) };

        /// <summary>
        /// Creates a new instance
        /// </summary>
        public ChainSymbolProvider(IDataCacheProvider dataCacheProvider, DateTime processingDate, SecurityType securityType, string market,
            string dataFolderRoot)
        {
            _processingDate = processingDate;
            _securityType = securityType;
            _dataSourceFolder = Path.Combine(dataFolderRoot, securityType.SecurityTypeToLower(), market);
            _dataCacheProvider = dataCacheProvider;
        }

        /// <summary>
        /// Gets all the available symbols keyed by the canonical symbol from the available price data in the data folder.
        /// </summary>
        public Dictionary<Symbol, List<Symbol>> GetSymbols()
        {
            var result = new Dictionary<Symbol, List<Symbol>>();

            // TODO: This could be removed since it's only for Lean repo data generation locally, but won't hurt to keep it
            foreach (var resolution in SymbolSourceResolutions)
            {
                var zipFileNames = GetZipFileNames(_processingDate, resolution);

                foreach (var zipFileName in zipFileNames)
                {
                    if (!LeanData.TryParsePath(zipFileName, out var canonicalSymbol, out var _, out var _, out var _, out var _) || !canonicalSymbol.IsCanonical())
                    {
                        // Skip non-canonical symbols. Should not happen.
                        continue;
                    }

                    // Skip if we already have the symbols for this canonical symbol using a higher resolution
                    if (result.ContainsKey(canonicalSymbol))
                    {
                        continue;
                    }

                    var symbols = GetSymbolsFromZipEntryNames(zipFileName, canonicalSymbol, resolution);
                    if (symbols != null && symbols.Count > 0)
                    {
                        result[canonicalSymbol] = symbols;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the zip file names for the canonical symbols where the contracts or universe constituents will be read from.
        /// </summary>
        protected virtual IEnumerable<string> GetZipFileNames(DateTime date, Resolution resolution)
        {
            var tickTypesLower = _symbolsDataTickTypes.Select(tickType => tickType.TickTypeToLower()).ToArray();

            if (resolution == Resolution.Minute)
            {
                var basePath = Path.Combine(_dataSourceFolder, resolution.ResolutionToLower());
                var directories = _securityType != SecurityType.FutureOption
                    ? Directory.EnumerateDirectories(basePath)
                    : Directory.EnumerateDirectories(basePath, "*", new EnumerationOptions() { RecurseSubdirectories = true, MaxRecursionDepth = 1 });

                var dateStr = date.ToString("yyyyMMdd");
                var optionStyleLower = _securityType.DefaultOptionStyle().OptionStyleToLower();

                return directories
                    .Select(directory => tickTypesLower
                        .Select(tickTypeLower => Path.Combine(directory, $"{dateStr}_{tickTypeLower}_{optionStyleLower}.zip"))
                        .Where(fileName => File.Exists(fileName))
                        .FirstOrDefault())
                    .Where(fileName => fileName != null);
            }
            // Support for resolutions higher than minute, just for Lean local repo data generation
            else
            {
                var dateStr = date.ToString("yyyy");
                try
                {
                    return Directory.EnumerateFiles(
                        Path.Combine(_dataSourceFolder, resolution.ResolutionToLower()),
                        $"*{dateStr}*.zip",
                        SearchOption.AllDirectories)
                        .Select(fileName =>
                        {
                            var fileInfo = new FileInfo(fileName);
                            var fileNameParts = fileInfo.Name.Split('_');
                            var tickTypeIndex = Array.IndexOf(tickTypesLower, fileNameParts[2]);

                            return (fileName, directoryName: fileInfo.DirectoryName, tickTypeIndex);
                        })
                        // Get only supported tick type data
                        .Where(tuple => tuple.tickTypeIndex > -1)
                        // For each contract get the first matching tick type file
                        .OrderBy(tuple => tuple.tickTypeIndex)
                        .GroupBy(tuple => tuple.directoryName)
                        .Select(group => group.First().fileName);
                }
                catch (DirectoryNotFoundException)
                {
                    return Enumerable.Empty<string>();
                }
            }
        }

        /// <summary>
        /// Reads the symbols from the zip entry names for the given canonical symbol.
        /// </summary>
        private List<Symbol> GetSymbolsFromZipEntryNames(string zipFileName, Symbol canonicalSymbol, Resolution resolution)
        {
            List<string> zipEntries;

            try
            {
                zipEntries = _dataCacheProvider.GetZipEntries(zipFileName);
            }
            catch
            {
                return null;
            }

            var symbols = zipEntries
                .Select(zipEntry => LeanData.ReadSymbolFromZipEntry(canonicalSymbol, resolution, zipEntry))
                // do not return expired contracts
                .Where(symbol => _processingDate.Date < symbol.ID.Date.Date)
                .Distinct();

            if (canonicalSymbol.SecurityType.IsOption())
            {
                symbols = symbols.OrderBy(symbol => symbol.ID.OptionRight)
                    .ThenBy(symbol => symbol.ID.StrikePrice)
                    .ThenBy(symbol => symbol.ID.Date)
                    .ThenBy(symbol => symbol.ID);
            }
            else
            {
                symbols = symbols.OrderBy(symbol => symbol.ID.Date).ThenBy(symbol => symbol.ID);
            }

            return symbols.ToList();
        }
    }
}
