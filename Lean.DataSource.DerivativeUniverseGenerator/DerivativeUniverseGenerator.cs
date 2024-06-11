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
using Lean.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Configuration;
using QuantConnect.Data;
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
    /// Options Universe generator
    /// </summary>
    public abstract class DerivativeUniverseGenerator
    {
        protected readonly DateTime _processingDate;
        protected readonly SecurityType _securityType;
        protected readonly string _market;
        protected readonly string _dataFolderRoot;
        protected readonly string _outputFolderRoot;
        protected readonly string _optionsDataSourceFolder;
        protected readonly string _optionsOutputFolderRoot;

        protected Resolution _symbolsDataResolution;
        protected TickType _symbolsDataTickType;
        protected Resolution _historyResolution;
        protected int _historyBarCount;

        protected readonly IDataProvider _dataProvider;
        protected readonly ZipDataCacheProvider _dataCacheProvider;
        protected readonly SubscriptionDataReaderHistoryProvider _historyProvider;
        protected readonly MarketHoursDatabase _marketHoursDatabase;

        /// <summary>
        /// Initializes a new instance of the <see cref="DerivativeUniverseGenerator" /> class.
        /// </summary>
        /// <param name="processingDate">The processing date</param>
        /// <param name="securityType">Option security type to process</param>
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

            _symbolsDataResolution = Resolution.Minute;
            _symbolsDataTickType = TickType.Quote;
            _historyResolution = Resolution.Minute;
            _historyBarCount = 200;

            _optionsDataSourceFolder = Path.Combine(_dataFolderRoot,
                _securityType.SecurityTypeToLower(),
                _market,
                _symbolsDataResolution.ResolutionToLower());

            _optionsOutputFolderRoot = Path.Combine(_outputFolderRoot,
                _securityType.SecurityTypeToLower(),
                _market,
                "universes");

            //_dataProvider = new ProcessedDataProvider();
            var api = Composer.Instance.GetExportedValueByTypeName<IApi>(Config.Get("api-handler", "Api"));
            api.Initialize(Globals.UserId, Globals.UserToken, dataFolderRoot);
            _dataProvider = Composer.Instance.GetExportedValueByTypeName<IDataProvider>(
                Config.Get("data-provider", "ApiDataProvider"), forceTypeNameOnExisting: false);
            Composer.Instance.AddPart<IDataProvider>(_dataProvider);

            _dataCacheProvider = new ZipDataCacheProvider(_dataProvider);

            var mapFileProvider = Composer.Instance.GetExportedValueByTypeName<IMapFileProvider>(
                Config.Get("map-file-provider", "LocalZipMapFileProvider"), forceTypeNameOnExisting: false);
            mapFileProvider.Initialize(_dataProvider);

            var factorFileProvider = Composer.Instance.GetExportedValueByTypeName<IFactorFileProvider>(
                Config.Get("factor-file-provider", "LocalZipFactorFileProvider"), forceTypeNameOnExisting: false);
            factorFileProvider.Initialize(mapFileProvider, _dataProvider);

            var dataPermissionManager = new DataPermissionManager();

            _historyProvider = new SubscriptionDataReaderHistoryProvider();
            _historyProvider.Initialize(new HistoryProviderInitializeParameters(null, null, _dataProvider, _dataCacheProvider, mapFileProvider,
                factorFileProvider, (_) => { }, true, dataPermissionManager, null, new AlgorithmSettings()));

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
        private Dictionary<Symbol, IEnumerable<Symbol>> GetSymbols()
        {
            var result = new Dictionary<Symbol, IEnumerable<Symbol>>();

            var zipFileNames = GetZipFileNames(_processingDate);
            foreach (var zipFileName in zipFileNames)
            {
                LeanData.TryParsePath(zipFileName, out var canonicalSymbol, out var _, out var _, out var _, out var _);
                if (!canonicalSymbol.IsCanonical())
                {
                    // Skip non-canonical symbols. Should not happen.
                    continue;
                }

                result[canonicalSymbol] = GetSymbolsFromZipEntryNames(zipFileName, canonicalSymbol);
            }

            return result;
        }

        /// <summary>
        /// Get s the zip file names for the canonical symbols where the contracts or universe constituents will be read from.
        /// </summary>
        private IEnumerable<string> GetZipFileNames(DateTime date)
        {
            var tickTypeLower = _symbolsDataTickType.TickTypeToLower();
            var dateStr = date.ToString("yyyyMMdd");

            return Directory.EnumerateFiles(_optionsDataSourceFolder, "*.zip", SearchOption.AllDirectories)
                // TODO: make this filter a virtual method or just get rid of it
                .Where(fileName =>
                {
                    var fileInfo = new FileInfo(fileName);
                    var fileNameParts = fileInfo.Name.Split('_');
                    return fileNameParts.Length == 3 &&
                           fileNameParts[0] == dateStr &&
                           fileNameParts[1] == tickTypeLower;
                });
        }

        /// <summary>
        /// Reads the symbols from the zip entry names for the given canonical symbol.
        /// </summary>
        private IEnumerable<Symbol> GetSymbolsFromZipEntryNames(string zipFileName, Symbol canonicalSymbol)
        {
            var zipEntries = _dataCacheProvider.GetZipEntries(zipFileName);

            foreach (var zipEntry in zipEntries)
            {
                var symbol = LeanData.ReadSymbolFromZipEntry(canonicalSymbol, _symbolsDataResolution, zipEntry);

                // TODO: Should check for SecurityIdentifier.DefaultDate? Should we have a virtual IsContractExpired method?
                // do not return expired contracts
                if (_processingDate.Date <= symbol.ID.Date.Date)
                {
                    yield return symbol;
                }
            }
        }

        /// <summary>
        /// Generates the universes for each given canonical symbol and its constituents (options, future contracts, etc).
        /// </summary>
        /// <param name="symbols">The symbols keyed by their canonical</param>
        private void GenerateUniverses(Dictionary<Symbol, IEnumerable<Symbol>> symbols)
        {
            foreach (var kvp in symbols)
            {
                var canonicalSymbol = kvp.Key;
                var contractsSymbols = kvp.Value;
                var underlyingSymbol = canonicalSymbol.Underlying;

                string universeFileName = GetUniverseFileName(underlyingSymbol);

                using var writer = new StreamWriter(universeFileName);

                var underlyingHistory = GenerateUnderlyingLine(underlyingSymbol, writer);

                Log.Trace($"DerivativeUniverseGenerator.GenerateUniverses(): " +
                    $"Generating universe for {underlyingSymbol} with {contractsSymbols.Count()} options");

                GenerateDerivativeLines(canonicalSymbol, contractsSymbols, underlyingHistory, writer);
            }
        }

        /// <summary>
        /// Generates the file name where the derivative's universe entry will be saved.
        /// </summary>
        private string GetUniverseFileName(Symbol underlyingSymbol)
        {
            var universeFileName = _optionsOutputFolderRoot;
            if (_securityType == SecurityType.FutureOption)
            {
                universeFileName = Path.Combine(universeFileName,
                    underlyingSymbol.ID.Symbol.ToLowerInvariant(),
                    $"{underlyingSymbol.ID.Date:yyyyMMdd}");
            }
            else
            {
                universeFileName = Path.Combine(universeFileName, underlyingSymbol.Value.ToLowerInvariant());
            }

            Directory.CreateDirectory(universeFileName);
            universeFileName = Path.Combine(universeFileName, $"{_processingDate.AddDays(-1):yyyyMMdd}.csv");

            return universeFileName;
        }

        /// <summary>
        /// Generates the derivative's underlying universe entry and gets the historical data used for the entry generation.
        /// </summary>
        /// <returns>
        /// The historical data used for the underlying universe entry generation.
        /// </returns>
        /// <remarks>
        /// This method should be overridden to return an empty list (and skipping writing to the stream) if no underlying entry is needed.
        /// </remarks>
        protected virtual List<Slice> GenerateUnderlyingLine(Symbol underlyingSymbol, StreamWriter writer)
        {
            var underlyingMarketHoursEntry = _marketHoursDatabase.GetEntry(underlyingSymbol.ID.Market, underlyingSymbol,
                underlyingSymbol.SecurityType);

            var historyEnd = _processingDate.AddDays(1);
            var historyStart = Time.GetStartTimeForTradeBars(underlyingMarketHoursEntry.ExchangeHours, historyEnd, _historyResolution.ToTimeSpan(),
                _historyBarCount, true, underlyingMarketHoursEntry.DataTimeZone);

            var underlyingHistoryRequest = new HistoryRequest(
                historyStart,
                historyEnd,
                typeof(TradeBar),
                underlyingSymbol,
                _historyResolution,
                underlyingMarketHoursEntry.ExchangeHours,
                underlyingMarketHoursEntry.DataTimeZone,
                _historyResolution,
                true,
                false,
                DataNormalizationMode.ScaledRaw,
                LeanData.GetCommonTickTypeForCommonDataTypes(typeof(TradeBar), _securityType));

            var underlyingHistory = _historyProvider.GetHistory(new[] { underlyingHistoryRequest },
                underlyingMarketHoursEntry.ExchangeHours.TimeZone).ToList();

            var entry = CreateUniverseEntry(underlyingSymbol);

            if (underlyingHistory == null || underlyingHistory.Count == 0)
            {
                writer.WriteLine(entry.ToCsv());
                return new List<Slice>();
            }

            entry.Update(underlyingHistory[^1]);
            writer.WriteLine(entry.ToCsv());

            return underlyingHistory;
        }

        /// <summary>
        /// Generates and writes the derivative universe entries for the specified canonical symbol.
        /// </summary>
        /// <remarks>The underlying history is a List to avoid multiple enumerations of the history</remarks>
        protected virtual void GenerateDerivativeLines(Symbol canonicalSymbol, IEnumerable<Symbol> symbols, List<Slice> underlyingHistory,
            StreamWriter writer)
        {
            var marketHoursEntry = _marketHoursDatabase.GetEntry(canonicalSymbol.ID.Market, canonicalSymbol, canonicalSymbol.SecurityType);

            var historyEnd = _processingDate.AddDays(1);
            var historyStart = Time.GetStartTimeForTradeBars(marketHoursEntry.ExchangeHours, historyEnd, _historyResolution.ToTimeSpan(),
                _historyBarCount, false, marketHoursEntry.DataTimeZone);

            foreach (var symbol in symbols)
            {
                Log.Trace($"Generating option universe entry for {symbol.Value}");

                var historyRequests = GetDerivativeHistoryRequests(symbol, historyStart, historyEnd, marketHoursEntry);
                var history = _historyProvider.GetHistory(historyRequests, marketHoursEntry.ExchangeHours.TimeZone);
                var entry = GenerateDerivativeEntry(symbol, history, underlyingHistory);

                writer.WriteLine(entry.ToCsv());
            }
        }

        /// <summary>
        /// Creates the requests to get the data to be used to generate the universe entry for the given derivative symbol
        /// </summary>
        protected virtual HistoryRequest[] GetDerivativeHistoryRequests(Symbol symbol, DateTime start, DateTime end,
            MarketHoursDatabase.Entry marketHoursEntry)
        {
            // TODO: This could be a virtual property
            var dataTypes = new[] { typeof(QuoteBar), typeof(OpenInterest) };

            return dataTypes.Select(dataType => new HistoryRequest(
                start,
                end,
                dataType,
                symbol,
                _historyResolution,
                marketHoursEntry.ExchangeHours,
                marketHoursEntry.DataTimeZone,
                _historyResolution,
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
            var enumerator = new SynchronizingSliceEnumerator(history.GetEnumerator(), underlyingHistory.GetEnumerator());

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
    }
}