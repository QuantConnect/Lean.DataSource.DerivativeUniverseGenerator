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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lean.DataSource.DerivativeUniverseGenerator;
using NodaTime;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// Derivatives Universe Generator
    /// </summary>
    public abstract class DerivativeUniverseGenerator
    {
        protected readonly DateTime _processingDate;
        protected readonly SecurityType _securityType;
        protected readonly string _market;
        protected readonly string _dataFolderRoot;
        protected readonly string _outputFolderRoot;
        protected readonly string _dataSourceFolder;
        protected readonly string _universesOutputFolderRoot;

        protected TickType _symbolsDataTickType;
        protected int _historyBarCount;

        private readonly IDataProvider _dataProvider;
        private readonly ZipDataCacheProvider _dataCacheProvider;
        private IMapFileProvider _mapFileProvider;
        private IFactorFileProvider _factorFileProvider;
        private IHistoryProvider _historyProvider;
        private IHistoryProvider _underlyingHistoryProvider;
        private Lazy<IHistoryProvider> _secondaryHistoryProvider;
        private Lazy<IHistoryProvider> _secondaryUnderlyingHistoryProvider;

        protected readonly MarketHoursDatabase _marketHoursDatabase;

        /// <summary>
        /// Careful: using other resolutions might introduce a look-ahead bias. For instance, if Daily resolution is used,
        /// yearly files will be used and the options chain read from disk would contain contracts from all year round,
        /// without considering the actual IPO date of the contract at X point in time of the year.
        /// Made virtual to allow for customization for Lean local repo options universe generator.
        /// </summary>
        protected virtual Resolution[] Resolutions { get; } = new[] { Resolution.Minute };

        protected virtual Resolution[] HistoryResolutions { get; } = new[] { Resolution.Daily };

        /// <summary>
        /// Initializes a new instance of the <see cref="DerivativeUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="securityType">Derivative security type to process</param>
        /// <param name="market">Market of data to process</param>
        /// <param name="dataFolderRoot">Path to the data folder</param>
        /// <param name="outputFolderRoot">Path to the output folder</param>
        public DerivativeUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot)
        {
            _processingDate = processingDate;
            _securityType = securityType;
            _market = market;
            _dataFolderRoot = dataFolderRoot;
            _outputFolderRoot = outputFolderRoot;

            _symbolsDataTickType = TickType.Quote;
            _historyBarCount = 200;

            _dataSourceFolder = Path.Combine(_dataFolderRoot,
                _securityType.SecurityTypeToLower(),
                _market);

            _universesOutputFolderRoot = Path.Combine(_outputFolderRoot,
                _securityType.SecurityTypeToLower(),
                _market,
                "universes");

            _dataProvider = new DefaultDataProvider();
            _dataCacheProvider = new ZipDataCacheProvider(_dataProvider);
            _historyProvider = GetHistoryProvider(_dataProvider, _dataCacheProvider, out _mapFileProvider, out _factorFileProvider);
            Composer.Instance.AddPart(_mapFileProvider);
            Composer.Instance.AddPart(_factorFileProvider);
            _secondaryHistoryProvider = new(GetSecondaryHistoryProvider);
            _underlyingHistoryProvider = GetUnderlyingHistoryProvider(_dataProvider, _dataCacheProvider);
            _secondaryUnderlyingHistoryProvider = new(GetSecondaryUnderlyingHistoryProvider);

            _marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
        }

        /// <summary>
        /// Runs this instance.
        /// </summary>
        public bool Run()
        {
            Log.Trace($"DerivativeUniverseGenerator.Run(): Processing {_securityType}-{_market} universes for date {_processingDate:yyyy-MM-dd}");

            try
            {
                var symbols = GetSymbols();
                GenerateUniverses(symbols);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    $"DerivativeUniverseGenerator.Run(): Error processing {_securityType}-{_market} universes for date {_processingDate:yyyy-MM-dd}");
                return false;
            }
        }

        /// <summary>
        /// Gets all the available symbols keyed by the canonical symbol from the available price data in the data folder.
        /// </summary>
        private Dictionary<Symbol, List<Symbol>> GetSymbols()
        {
            var result = new Dictionary<Symbol, List<Symbol>>();

            // TODO: This could be removed since it's only for Lean repo data generation locally, but won't hurt to keep it
            foreach (var resolution in Resolutions)
            {
                var zipFileNames = GetZipFileNames(_processingDate, resolution);

            foreach (var zipFileName in zipFileNames)
            {
                LeanData.TryParsePath(zipFileName, out var canonicalSymbol, out var _, out var _, out var _, out var _);
                if (!canonicalSymbol.IsCanonical())
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
                    if (symbols != null)
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
        private IEnumerable<string> GetZipFileNames(DateTime date, Resolution resolution)
        {
            var tickTypeLower = _symbolsDataTickType.TickTypeToLower();
            var dateStr = date.ToString(resolution == Resolution.Minute ? "yyyyMMdd" : "yyyy");

            return Directory.EnumerateFiles(Path.Combine(_dataSourceFolder, resolution.ResolutionToLower()), "*.zip", SearchOption.AllDirectories)
                .Where(fileName =>
                {
                    var fileInfo = new FileInfo(fileName);
                    var fileNameParts = fileInfo.Name.Split('_');

                    if (resolution == Resolution.Minute)
                    {
                    return fileNameParts.Length == 3 &&
                           fileNameParts[0] == dateStr &&
                           fileNameParts[1] == tickTypeLower;
                    }

                    return fileNameParts.Length == 4 &&
                               fileNameParts[1] == dateStr &&
                               fileNameParts[2] == tickTypeLower;
                });
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

            return zipEntries
                .Select(zipEntry => LeanData.ReadSymbolFromZipEntry(canonicalSymbol, resolution, zipEntry))
                // do not return expired contracts
                .Where(symbol => _processingDate.Date <= symbol.ID.Date.Date)
                .OrderBy(symbol => symbol.ID.OptionRight)
                .ThenBy(symbol => symbol.ID.StrikePrice)
                .ThenBy(symbol => symbol.ID.Date)
                .ThenBy(symbol => symbol.ID)
                .ToList();
                }

        /// <summary>
        /// Generates the universes for each given canonical symbol and its constituents (options, future contracts, etc).
        /// </summary>
        /// <param name="symbols">The symbols keyed by their canonical</param>
        private void GenerateUniverses(Dictionary<Symbol, List<Symbol>> symbols)
        {
            Parallel.ForEach(symbols, kvp =>
            {
                var canonicalSymbol = kvp.Key;
                var contractsSymbols = kvp.Value;
                var underlyingSymbol = canonicalSymbol.Underlying;

                var universeFileName = GetUniverseFileName(canonicalSymbol);

                var underlyingMarketHoursEntry = _marketHoursDatabase.GetEntry(underlyingSymbol.ID.Market, underlyingSymbol,
                    underlyingSymbol.SecurityType);
                var optionMarketHoursEntry = _marketHoursDatabase.GetEntry(canonicalSymbol.ID.Market, canonicalSymbol, canonicalSymbol.SecurityType);

                if (!underlyingMarketHoursEntry.ExchangeHours.IsDateOpen(_processingDate) ||
                    !optionMarketHoursEntry.ExchangeHours.IsDateOpen(_processingDate))
                {
                    Log.Trace($"Market is closed on {_processingDate:yyyy/MM/dd} for {underlyingSymbol}, universe file will not be generated.");
                    return;
                }

                Log.Trace($"DerivativeUniverseGenerator.GenerateUniverses(): " +
                    $"Generating universe for {underlyingSymbol} on {_processingDate:yyyy/MM/dd} with {contractsSymbols.Count} constituents.");

                using var writer = new StreamWriter(universeFileName);

                GenerateUnderlyingLine(underlyingSymbol, underlyingMarketHoursEntry, writer, out var underlyingHistory);
                GenerateDerivativeLines(canonicalSymbol, contractsSymbols, optionMarketHoursEntry, underlyingHistory, writer);
            });
        }

        /// <summary>
        /// Generates the file name where the derivative's universe entry will be saved.
        /// </summary>
        protected abstract string GetUniverseFileName(Symbol canonicalSymbol);

        /// <summary>
        /// Generates the derivative's underlying universe entry and gets the historical data used for the entry generation.
        /// </summary>
        /// <returns>
        /// The historical data used for the underlying universe entry generation.
        /// </returns>
        /// <remarks>
        /// This method should be overridden to return an empty list (and skipping writing to the stream) if no underlying entry is needed.
        /// </remarks>
        protected virtual void GenerateUnderlyingLine(Symbol underlyingSymbol, MarketHoursDatabase.Entry marketHoursEntry,
            StreamWriter writer, out List<Slice> history)
        {
            GetHistoryTimeRange(HistoryResolutions[0], marketHoursEntry, out var historyEnd, out var historyStart);
            var underlyingHistoryRequest = new HistoryRequest(
                historyStart,
                historyEnd,
                typeof(TradeBar),
                underlyingSymbol,
                HistoryResolutions[0],
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                HistoryResolutions[0],
                true,
                false,
                DataNormalizationMode.ScaledRaw,
                LeanData.GetCommonTickTypeForCommonDataTypes(typeof(TradeBar), _securityType));

            var entry = CreateUniverseEntry(underlyingSymbol);
            history = GetHistory(new[] { underlyingHistoryRequest }, _underlyingHistoryProvider, _secondaryUnderlyingHistoryProvider,
                marketHoursEntry.ExchangeHours.TimeZone, marketHoursEntry);

            if (history == null || history.Count == 0)
            {
                Log.Error($"DerivativeUniverseGenerator.GenerateUnderlyingLine(): " +
                    $"No historical data found for underlying {underlyingSymbol} on {_processingDate:yyyy/MM/dd}. Prices will be set to zero.");

                writer.WriteLine(entry.ToCsv());
                    return;
            }

            entry.Update(history[^1]);
            writer.WriteLine(entry.ToCsv());
        }

        private List<Slice> GetHistory(HistoryRequest[] historyRequests, IHistoryProvider historyProvider,
            Lazy<IHistoryProvider> secondaryHistoryProvider, DateTimeZone sliceTimeZone, MarketHoursDatabase.Entry marketHoursEntry)
        {
            List<Slice> history = null;

            foreach (var resolution in HistoryResolutions)
            {
                GetHistoryTimeRange(resolution, marketHoursEntry, out var historyEnd, out var historyStart);

                var resolutionHistoryRequests = historyRequests.Select(x =>
                {
                    var request = new HistoryRequest(x, x.Symbol, historyStart, historyEnd);
                    request.Resolution = resolution;
                    request.FillForwardResolution = resolution;
                    return request;
                }).ToArray();

                history = historyProvider.GetHistory(resolutionHistoryRequests, sliceTimeZone).ToList();
                if (history != null && history.Count > 0)
                {
                    return history;
                }
            }

            // Finally let's try with the secondary history provider
            if ((history == null || history.Count == 0) && secondaryHistoryProvider != null)
            {
                history = secondaryHistoryProvider.Value.GetHistory(historyRequests, sliceTimeZone).ToList();
            }

            return history;
        }

        private void GetHistoryTimeRange(Resolution resolution, MarketHoursDatabase.Entry marketHoursEntry, out DateTime historyEnd, out DateTime historyStart)
        {
            historyEnd = resolution != Resolution.Daily ? _processingDate : _processingDate.AddDays(1);
            historyStart = Time.GetStartTimeForTradeBars(marketHoursEntry.ExchangeHours, historyEnd, resolution.ToTimeSpan(),
                _historyBarCount, true, marketHoursEntry.DataTimeZone);
        }

        /// <summary>
        /// Generates and writes the derivative universe entries for the specified canonical symbol.
        /// </summary>
        /// <remarks>The underlying history is a List to avoid multiple enumerations of the history</remarks>
        protected virtual void GenerateDerivativeLines(Symbol canonicalSymbol, IEnumerable<Symbol> symbols,
            MarketHoursDatabase.Entry marketHoursEntry, List<Slice> underlyingHistory, StreamWriter writer)
        {
            foreach (var symbol in symbols)
            {
                Log.Debug($"Generating universe entry for {symbol.Value}");

                GetHistoryTimeRange(HistoryResolutions[0], marketHoursEntry, out var historyEnd, out var historyStart);
                var historyRequests = GetDerivativeHistoryRequests(symbol, historyEnd, historyStart, marketHoursEntry);

                IDerivativeUniverseFileEntry entry;
                if (historyRequests == null || historyRequests.Length == 0)
                {
                    entry = GenerateDerivativeEntry(symbol, Enumerable.Empty<Slice>(), Enumerable.Empty<Slice>().ToList());
                }
                else
                {
                    var history = GetHistory(historyRequests, _historyProvider, _secondaryHistoryProvider,
                        marketHoursEntry.ExchangeHours.TimeZone, marketHoursEntry);
                    entry = GenerateDerivativeEntry(symbol, history, underlyingHistory);
                }

                writer.WriteLine(entry.ToCsv());
            }
        }

        /// <summary>
        /// Creates the requests to get the data to be used to generate the universe entry for the given derivative symbol
        /// </summary>
        protected virtual HistoryRequest[] GetDerivativeHistoryRequests(Symbol symbol, DateTime start, DateTime end,
            MarketHoursDatabase.Entry marketHoursEntry)
        {
            var dataTypes = new[] { typeof(QuoteBar), typeof(OpenInterest) };
            return dataTypes.Select(dataType => new HistoryRequest(
                start,
                end,
                dataType,
                symbol,
                HistoryResolutions[0],
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                HistoryResolutions[0],
                true,
                false,
                DataNormalizationMode.ScaledRaw,
                LeanData.GetCommonTickTypeForCommonDataTypes(dataType, _securityType))).ToArray();
        }

        /// <summary>
        /// Generates the given symbol universe entry from the provided historical data
        /// </summary>
        protected virtual IDerivativeUniverseFileEntry GenerateDerivativeEntry(Symbol symbol, IEnumerable<Slice> history, List<Slice> underlyingHistory)
        {
            var entry = CreateUniverseEntry(symbol);
            using var enumerator = new SynchronizingSliceEnumerator(history.GetEnumerator(), underlyingHistory.GetEnumerator());

            while (enumerator.MoveNext())
            {
                entry.Update(enumerator.Current);
            }

            return entry;
        }

        /// <summary>
        /// Factory method to create an instance of <see cref="IDerivativeUniverseFileEntry"/> for the given <paramref name="symbol"/>
        /// </summary>
        protected abstract IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol);

        /// <summary>
        /// Gets the history provider to be used to retrieve the historical data for the universe generation
        /// </summary>
        /// <remarks>
        /// Made virtual to allow for easier test data generation.
        /// </remarks>
        protected virtual IHistoryProvider GetHistoryProvider(IDataProvider dataProvider, ZipDataCacheProvider dataCacheProvider,
            out IMapFileProvider mapFileProvider, out IFactorFileProvider factorFileProvider)
        {
            if (_historyProvider != null)
            {
                mapFileProvider = _mapFileProvider;
                factorFileProvider = _factorFileProvider;
                return _historyProvider;
            }

            _mapFileProvider = mapFileProvider = new LocalZipMapFileProvider();
            mapFileProvider.Initialize(dataProvider);

            _factorFileProvider = factorFileProvider = new LocalZipFactorFileProvider();
            factorFileProvider.Initialize(mapFileProvider, dataProvider);

            var api = Composer.Instance.GetExportedValueByTypeName<IApi>(Config.Get("api-handler"));

            _historyProvider = new HistoryProviderManager();
            var parameters = new HistoryProviderInitializeParameters(null, api, dataProvider, dataCacheProvider, mapFileProvider,
                factorFileProvider, (_) => { }, true, new DataPermissionManager(), null, new AlgorithmSettings());
            _historyProvider.Initialize(parameters);

            return _historyProvider;
        }

        /// <summary>
        /// Gets the history provider to be used to retrieve the historical data for the underlying securities.
        /// It defaults to the same history provider as the one used for the derivatives,
        /// by calling <see cref="GetHistoryProvider(IDataProvider, ZipDataCacheProvider, out IMapFileProvider, out IFactorFileProvider)"/>
        /// </summary>
        protected virtual IHistoryProvider GetUnderlyingHistoryProvider(IDataProvider dataProvider, ZipDataCacheProvider dataCacheProvider)
        {
            return GetHistoryProvider(dataProvider, dataCacheProvider, out _, out _);
        }

        /// <summary>
        /// Gets a history provider to be used to retrieve the historical data for the derivative securities.
        /// If not null, it will be used to retrieve the historical data when the primary one returns no data.
        /// </summary>
        /// <returns></returns>
        protected virtual IHistoryProvider GetSecondaryHistoryProvider()
        {
            return null;
        }

        /// <summary>
        /// Gets a history provider to be used to retrieve the historical data for the underlying securities.
        /// If not null, it will be used to retrieve the historical data when the primary one returns no data.
        /// It defaults to the same secondary history provider as the one used for the derivatives,
        /// by calling <see cref="GetSecondaryHistoryProvider"/>
        /// </summary>
        /// <returns></returns>
        protected virtual IHistoryProvider GetSecondaryUnderlyingHistoryProvider()
        {
            return GetSecondaryHistoryProvider();
        }
    }
}