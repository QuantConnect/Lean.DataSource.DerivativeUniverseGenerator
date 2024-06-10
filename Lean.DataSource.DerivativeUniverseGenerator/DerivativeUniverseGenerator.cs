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

using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
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
    public class DerivativeUniverseGenerator
    {
        private static readonly TradeBar _defaultTradeBar = new TradeBar(DateTime.MinValue, Symbol.None, 0, 0, 0, 0, 0);
        private static readonly QuoteBar _defaultQuoteBar = new QuoteBar(DateTime.MinValue, Symbol.None, new Bar(0, 0, 0, 0), 0,
            new Bar(0, 0, 0, 0), 0, Time.OneDay);
        private static readonly OpenInterest _defaultOpenInterest = new OpenInterest(DateTime.MinValue, Symbol.None, 0);

        private readonly DateTime _processingDate;
        private readonly SecurityType _securityType;
        private readonly string _market;
        private readonly string _dataFolderRoot;
        private readonly string _outputFolderRoot;
        private readonly string _optionsDataSourceFolder;
        private readonly string _optionsOutputFolderRoot;

        private readonly Resolution _optionsChainDataResolution;
        private readonly Resolution _historyResolution;
        private readonly int _historyBarCount;
        private readonly TickType _tickType;

        private readonly IDataProvider _dataProvider;
        private readonly ZipDataCacheProvider _dataCacheProvider;
        private readonly SubscriptionDataReaderHistoryProvider _historyProvider;
        private readonly MarketHoursDatabase _marketHoursDatabase;

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

            _optionsChainDataResolution = Resolution.Minute;
            _historyResolution = Resolution.Minute;
            _historyBarCount = 200;
            _tickType = TickType.Quote;

            _optionsDataSourceFolder = Path.Combine(_dataFolderRoot,
                _securityType.SecurityTypeToLower(),
                _market,
                _optionsChainDataResolution.ResolutionToLower());

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
                var options = GetOptions();
                GenerateUniverses(options);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    $"DerivativeUniverseGenerator.Run(): Error processing {_securityType}-{_market} universes for date {_processingDate:yyyy-MM-dd}");
                return false;
            }
        }

        private Dictionary<Symbol, IEnumerable<Symbol>> GetOptions()
        {
            var result = new Dictionary<Symbol, IEnumerable<Symbol>>();

            var zipFileNames = GetOptionsZipFiles(_processingDate);
            foreach (var zipFileName in zipFileNames)
            {
                LeanData.TryParsePath(zipFileName, out var canonicalSymbol, out var _, out var _, out var _, out var _);
                if (!canonicalSymbol.IsCanonical())
                {
                    throw new Exception($"Invalid canonical symbol: {canonicalSymbol}");
                }

                result[canonicalSymbol] = GetOptionsSymbolsFromZipEntries(zipFileName, canonicalSymbol);
            }

            return result;
        }

        private IEnumerable<string> GetOptionsZipFiles(DateTime date)
        {
            var tickTypeLower = _tickType.TickTypeToLower();
            var dateStr = date.ToString("yyyyMMdd");

            return Directory.EnumerateFiles(_optionsDataSourceFolder, "*.zip", SearchOption.AllDirectories)
                .Where(fileName =>
                {
                    var fileInfo = new FileInfo(fileName);
                    var fileNameParts = fileInfo.Name.Split('_');
                    return fileNameParts.Length == 3 &&
                           fileNameParts[0] == dateStr &&
                           fileNameParts[1] == tickTypeLower;
                });
        }

        private IEnumerable<Symbol> GetOptionsSymbolsFromZipEntries(string zipFileName, Symbol canonicalSymbol)
        {
            var zipEntries = _dataCacheProvider.GetZipEntries(zipFileName);

            foreach (var zipEntry in zipEntries)
            {
                var symbol = LeanData.ReadSymbolFromZipEntry(canonicalSymbol, _optionsChainDataResolution, zipEntry);

                // do not return expired contracts
                if (_processingDate.Date <= symbol.ID.Date.Date)
                {
                    yield return symbol;
                }
            }
        }

        private void GenerateUniverses(Dictionary<Symbol, IEnumerable<Symbol>> options)
        {
            foreach (var kvp in options)
            {
                var canonicalSymbol = kvp.Key;
                var optionSymbols = kvp.Value;
                var underlyingSymbol = canonicalSymbol.Underlying;

                string universeFileName = GetUniverseFileName(underlyingSymbol);

                // TODO: Check whether LeanDataWriter can be used instead
                using var writer = new StreamWriter(universeFileName);

                var underlyingHistory = GenerateUnderlyingLine(underlyingSymbol, writer);

                Log.Trace($"DerivativeUniverseGenerator.GenerateUniverses(): Generating universe for {underlyingSymbol} with {optionSymbols.Count()} options");

                GenerateOptionsLines(canonicalSymbol, optionSymbols, underlyingHistory, writer);
            }
        }

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

        private List<Slice> GenerateUnderlyingLine(Symbol underlyingSymbol, StreamWriter writer)
        {
            var underlyingMarketHoursEntry = _marketHoursDatabase.GetEntry(underlyingSymbol.ID.Market, underlyingSymbol,
                underlyingSymbol.SecurityType);

            var historyEnd = _processingDate.AddDays(1);
            var historyStart = Time.GetStartTimeForTradeBars(underlyingMarketHoursEntry.ExchangeHours, historyEnd, _historyResolution.ToTimeSpan(),
                _historyBarCount, false, underlyingMarketHoursEntry.DataTimeZone);

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

            if (underlyingHistory == null || underlyingHistory.Count == 0)
            {
                writer.WriteLine(new OptionUniverseEntry(underlyingSymbol, _defaultTradeBar).ToCsv());
                return new List<Slice>();
            }

            var lastSlice = underlyingHistory[^1];
            if (!lastSlice.Bars.TryGetValue(underlyingSymbol, out var tradeBar))
            {
                tradeBar = null;
            }
            if (!lastSlice.QuoteBars.TryGetValue(underlyingSymbol, out var quoteBar))
            {
                quoteBar = null;
            }

            writer.WriteLine(new OptionUniverseEntry(underlyingSymbol, tradeBar, quoteBar).ToCsv());

            return underlyingHistory;
        }

        private void GenerateOptionsLines(Symbol canonicalSymbol, IEnumerable<Symbol> optionSymbols, IEnumerable<Slice> underlyingHistory, StreamWriter writer)
        {
            var optionsMarketHoursEntry = _marketHoursDatabase.GetEntry(canonicalSymbol.ID.Market, canonicalSymbol, canonicalSymbol.SecurityType);
            var dataTypes = new[] { typeof(TradeBar), typeof(QuoteBar), typeof(OpenInterest) };

            var historyEnd = _processingDate.AddDays(1);
            var historyStart = Time.GetStartTimeForTradeBars(optionsMarketHoursEntry.ExchangeHours, historyEnd, _historyResolution.ToTimeSpan(),
                _historyBarCount, false, optionsMarketHoursEntry.DataTimeZone);

            foreach (var optionSymbol in optionSymbols)
            {
                var historyRequests = dataTypes.Select(dataType => new HistoryRequest(
                    historyStart,
                    historyEnd,
                    dataType,
                    optionSymbol,
                    _historyResolution,
                    optionsMarketHoursEntry.ExchangeHours,
                    optionsMarketHoursEntry.DataTimeZone,
                    _historyResolution,
                    true,
                    false,
                    DataNormalizationMode.ScaledRaw,
                    LeanData.GetCommonTickTypeForCommonDataTypes(dataType, _securityType))).ToList();

                var mirrorOptionSymbol = Symbol.CreateOption(optionSymbol.Underlying.Value,
                    optionSymbol.ID.Market,
                    optionSymbol.ID.OptionStyle,
                    optionSymbol.ID.OptionRight == OptionRight.Call ? OptionRight.Put : OptionRight.Call,
                    optionSymbol.ID.StrikePrice,
                    optionSymbol.ID.Date);
                historyRequests.Add(new HistoryRequest(
                    historyStart,
                    historyEnd,
                    typeof(QuoteBar),
                    mirrorOptionSymbol,
                    _historyResolution,
                    optionsMarketHoursEntry.ExchangeHours,
                    optionsMarketHoursEntry.DataTimeZone,
                    _historyResolution,
                    true,
                    false,
                    DataNormalizationMode.ScaledRaw,
                    TickType.Quote));

                var history = _historyProvider.GetHistory(historyRequests, optionsMarketHoursEntry.ExchangeHours.TimeZone).ToList();

                Log.Trace($"Generating option universe entry for {optionSymbol.Value}");

                var entry = GenerateOptionEntry(optionSymbol, mirrorOptionSymbol, history, underlyingHistory);
                writer.WriteLine(entry.ToCsv());
            }
        }

        private OptionUniverseEntry GenerateOptionEntry(Symbol optionSymbol, Symbol mirrorOptionSymbol, IEnumerable<Slice> history, IEnumerable<Slice> underlyingHistory)
        {
            var greeksIndicators = new GreeksIndicators(optionSymbol, mirrorOptionSymbol);

            var enumerator = new SynchronizingSliceEnumerator(history.GetEnumerator(), underlyingHistory.GetEnumerator());

            Slice lastSlice = null;
            while (enumerator.MoveNext())
            {
                var currentSlice = enumerator.Current;

                if (currentSlice.ContainsKey(optionSymbol))
                {
                    lastSlice = currentSlice;
                }

                if (currentSlice.QuoteBars.TryGetValue(optionSymbol, out var optionQuoteBar))
                {
                    greeksIndicators.Update(optionQuoteBar);
                }

                if (currentSlice.QuoteBars.TryGetValue(mirrorOptionSymbol, out var mirrorOptionQuoteBar))
                {
                    greeksIndicators.Update(mirrorOptionQuoteBar);
                }

                if (currentSlice.TryGetValue(optionSymbol.Underlying, out var underlyingData))
                {
                    greeksIndicators.Update(underlyingData);
                }
            }

            // The contract history was empty
            if (lastSlice == null)
            {
                return new OptionUniverseEntry(optionSymbol, _defaultTradeBar, _defaultQuoteBar, _defaultOpenInterest);
            }

            if (!lastSlice.Bars.TryGetValue(optionSymbol, out var tradeBar))
            {
                tradeBar = _defaultTradeBar;
            }
            if (!lastSlice.QuoteBars.TryGetValue(optionSymbol, out var quoteBar))
            {
                quoteBar = _defaultQuoteBar;
            }
            if (!lastSlice.Get<OpenInterest>().TryGetValue(optionSymbol, out var openInterest))
            {
                openInterest = _defaultOpenInterest;
            }

            var entry = new OptionUniverseEntry(optionSymbol, tradeBar, quoteBar, openInterest);
            entry.ImpliedVolatility = greeksIndicators.ImpliedVolatility;
            entry.Greeks = greeksIndicators.GetGreeks();

            return entry;
        }

        /// <summary>
        /// Helper class for the <see cref="DerivativeUniverseGenerator" /> generator.
        /// </summary>
        private class OptionUniverseEntry
        {
            public Symbol Symbol { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
            public decimal Volume { get; set; }
            public decimal? OpenInterest { get; set; }
            public decimal? ImpliedVolatility { get; set; }
            public Greeks? Greeks { get; set; }

            public OptionUniverseEntry(Symbol symbol, TradeBar? tradeBar = null, QuoteBar? quoteBar = null, OpenInterest? openInterest = null)
            {
                if (tradeBar == null && quoteBar == null)
                {
                    throw new ArgumentException("Both tradeBar and quoteBar are null.");
                }

                Symbol = symbol;
                Open = tradeBar?.Open != decimal.Zero ? tradeBar.Open : (quoteBar?.Open ?? decimal.Zero);
                High = tradeBar?.High != decimal.Zero ? tradeBar.High : (quoteBar?.High ?? decimal.Zero);
                Low = tradeBar?.Low != decimal.Zero ? tradeBar.Low : (quoteBar?.Low ?? decimal.Zero);
                Close = tradeBar?.Close != decimal.Zero ? tradeBar.Close : (quoteBar?.Close ?? decimal.Zero);
                Volume = tradeBar?.Volume ?? decimal.Zero;
                OpenInterest = openInterest?.Value;
            }

            public string ToCsv()
            {
                var sid = Symbol.ID.ToString();
                if (Symbol.SecurityType.IsOption())
                {
                    sid = sid.Replace($"|{Symbol.Underlying.ID}", "");
                }

                return $"{sid},{Symbol.Value},{Open},{High},{Low},{Close},{Volume},{OpenInterest},{ImpliedVolatility}," +
                    $"{Greeks?.Delta},{Greeks?.Gamma},{Greeks?.Vega},{Greeks?.Theta},{Greeks?.Rho}";
            }
        }

        /// <summary>
        /// Helper class that holds and updates the greeks indicators
        /// </summary>
        private class GreeksIndicators
        {
            private readonly Delta _delta;
            private readonly Gamma _gamma;
            private readonly Vega _vega;
            private readonly Theta _theta;
            private readonly Rho _rho;

            public GreeksIndicators(Symbol optionSymbol, Symbol mirrorOptionSymbol)
            {
                var riskFreeInterestRateModel = new InterestRateProvider();
                var funcRiskFreeInterestRateModel = new FuncRiskFreeRateInterestRateModel(
                    (datetime) => riskFreeInterestRateModel.GetInterestRate(datetime));

                var dividendYieldModel = optionSymbol.SecurityType == SecurityType.FutureOption || optionSymbol.SecurityType == SecurityType.IndexOption
                    ? new DividendYieldProvider()
                    : new DividendYieldProvider(optionSymbol.Underlying);

                _delta = new Delta(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _gamma = new Gamma(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _vega = new Vega(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _theta = new Theta(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
                _rho = new Rho(optionSymbol, funcRiskFreeInterestRateModel, dividendYieldModel, mirrorOptionSymbol);
            }

            public void Update(IBaseData data)
            {
                var point = new IndicatorDataPoint(data.Symbol, data.EndTime, data.Price);
                _delta.Update(point);
                _gamma.Update(point);
                _vega.Update(point);
                _theta.Update(point);
                _rho.Update(point);
            }

            public Greeks GetGreeks()
            {
                return new Greeks(_delta, _gamma, _vega, _theta, _rho, 0m);
            }

            public decimal ImpliedVolatility => _delta.ImpliedVolatility;
        }
    }
}