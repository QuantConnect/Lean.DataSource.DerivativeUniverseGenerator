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

using NodaTime;
using NUnit.Framework;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DerivativeUniverseGeneratorBase = QuantConnect.DataSource.DerivativeUniverseGenerator.DerivativeUniverseGenerator;
using BaseDerivativeUniverseFileEntry = QuantConnect.DataSource.DerivativeUniverseGenerator.BaseDerivativeUniverseFileEntry;
using IDerivativeUniverseFileEntry = QuantConnect.DataSource.DerivativeUniverseGenerator.IDerivativeUniverseFileEntry;

namespace QuantConnect.DataSource.DerivativeUniverseGeneratorTests
{
    [TestFixture]
    public class DerivativeUniverseGeneratorTests
    {
        private string _outputFolder;

        [SetUp]
        public void SetUp()
        {
            _outputFolder = Path.Combine(Path.GetTempPath(), $"universe-generator-tests-{Guid.NewGuid():N}");
        }

        [TearDown]
        public void TearDown()
        {
            Config.Set("universe-generation-backup-files", "false");
            if (Directory.Exists(_outputFolder))
            {
                Directory.Delete(_outputFolder, true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratesUniverseFilesAsBackupFilesWhenConfigured(bool generateBackupFiles)
        {
            Config.Set("universe-generation-backup-files", generateBackupFiles ? "true" : "false");

            var processingDate = new DateTime(2026, 08, 10);
            var generator = new TestDerivativeUniverseGenerator(processingDate, SecurityType.Option, Market.USA, _outputFolder);
            var underlying = new Symbol(SecurityIdentifier.GenerateEquity("SPY", Market.USA, mapSymbol: false), "SPY");
            var canonicalSymbol = Symbol.CreateCanonicalOption(underlying);

            var universeFileName = generator.GetUniverseFileName(canonicalSymbol);

            var expectedFileName = generateBackupFiles ? $"{processingDate:yyyyMMdd}.csv.backup" : $"{processingDate:yyyyMMdd}.csv";
            Assert.AreEqual(expectedFileName, Path.GetFileName(universeFileName));
        }

        [Test]
        public void UnderlyingAndDerivativeHistoryRequestsShareASingleProviderByDefault()
        {
            var historyProvider = new RecordingHistoryProvider();
            var generator = new TestDerivativeUniverseGenerator(new DateTime(2026, 08, 10), SecurityType.Option, Market.USA,
                _outputFolder, historyProvider);

            var (underlying, contract) = GenerateHistoryForBothLegs(generator);

            var underlyingRequests = historyProvider.Requests.Where(x => x.Symbol == underlying).ToList();
            var derivativeRequests = historyProvider.Requests.Where(x => x.Symbol == contract).ToList();
            Assert.AreEqual(1, underlyingRequests.Count);
            Assert.AreEqual(3, derivativeRequests.Count);
            Assert.AreEqual(historyProvider.Requests.Count, underlyingRequests.Count + derivativeRequests.Count);
        }

        [Test]
        public void UnderlyingAndDerivativeHistoryRequestsAreRoutedToTheirOwnProviders()
        {
            var underlyingHistoryProvider = new RecordingHistoryProvider();
            var derivativeHistoryProvider = new RecordingHistoryProvider();
            var generator = new TestDerivativeUniverseGenerator(new DateTime(2026, 08, 10), SecurityType.Option, Market.USA,
                _outputFolder, underlyingHistoryProvider, derivativeHistoryProvider);

            var (underlying, contract) = GenerateHistoryForBothLegs(generator);

            Assert.AreEqual(1, underlyingHistoryProvider.Requests.Count);
            Assert.IsTrue(underlyingHistoryProvider.Requests.All(x => x.Symbol == underlying));

            Assert.AreEqual(3, derivativeHistoryProvider.Requests.Count);
            Assert.IsTrue(derivativeHistoryProvider.Requests.All(x => x.Symbol == contract));
        }

        /// <summary>
        /// Runs the underlying entry generation and the derivative entries generation for a single SPY option contract,
        /// so the requests each history provider received can be asserted on.
        /// </summary>
        private static (Symbol Underlying, Symbol Contract) GenerateHistoryForBothLegs(TestDerivativeUniverseGenerator generator)
        {
            var underlying = new Symbol(SecurityIdentifier.GenerateEquity("SPY", Market.USA, mapSymbol: false), "SPY");
            var contract = Symbol.CreateOption(underlying, Market.USA, OptionStyle.American, OptionRight.Call, 400m, new DateTime(2026, 08, 21));

            var marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
            var underlyingMarketHoursEntry = marketHoursDatabase.GetEntry(underlying.ID.Market, underlying, underlying.SecurityType);
            var derivativeMarketHoursEntry = marketHoursDatabase.GetEntry(contract.ID.Market, contract, contract.SecurityType);

            using var writer = new StreamWriter(Stream.Null);
            generator.TryGenerateAndWriteUnderlyingLine(underlying, underlyingMarketHoursEntry, writer);
            generator.GenerateDerivativeEntries(contract.Canonical, new List<Symbol> { contract }, derivativeMarketHoursEntry).ToList();

            return (underlying, contract);
        }

        private class TestDerivativeUniverseGenerator : DerivativeUniverseGeneratorBase
        {
            public TestDerivativeUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string outputFolderRoot,
                IHistoryProvider historyProvider = null)
                : base(processingDate, securityType, market, outputFolderRoot, outputFolderRoot, null, null, historyProvider)
            {
            }

            public TestDerivativeUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string outputFolderRoot,
                IHistoryProvider underlyingHistoryProvider, IHistoryProvider derivativeHistoryProvider)
                : base(processingDate, securityType, market, outputFolderRoot, outputFolderRoot, null, null,
                      underlyingHistoryProvider, derivativeHistoryProvider)
            {
            }

            public new string GetUniverseFileName(Symbol canonicalSymbol)
            {
                return base.GetUniverseFileName(canonicalSymbol);
            }

            public bool TryGenerateAndWriteUnderlyingLine(Symbol underlyingSymbol, MarketHoursDatabase.Entry marketHoursEntry,
                StreamWriter writer)
            {
                return base.TryGenerateAndWriteUnderlyingLine(underlyingSymbol, marketHoursEntry, writer, out _, out _);
            }

            public IEnumerable<IDerivativeUniverseFileEntry> GenerateDerivativeEntries(Symbol canonicalSymbol, List<Symbol> symbols,
                MarketHoursDatabase.Entry marketHoursEntry)
            {
                return base.GenerateDerivativeEntries(canonicalSymbol, symbols, marketHoursEntry, null, null);
            }

            protected override Dictionary<Symbol, List<Symbol>> FilterSymbols(Dictionary<Symbol, List<Symbol>> symbols,
                HashSet<string> symbolsToProcess)
            {
                return symbols;
            }

            protected override Dictionary<Symbol, List<Symbol>> GetSymbols()
            {
                return new Dictionary<Symbol, List<Symbol>>();
            }

            protected override IDerivativeUniverseFileEntry CreateUniverseEntry(Symbol symbol)
            {
                return new BaseDerivativeUniverseFileEntry(symbol);
            }

            protected override bool NeedsUnderlyingData()
            {
                return false;
            }
        }

        private class RecordingHistoryProvider : HistoryProviderBase
        {
            /// <summary>
            /// The history requests this provider has received, across all <see cref="GetHistory"/> calls.
            /// </summary>
            public List<HistoryRequest> Requests { get; } = new();

            public override int DataPointCount => 0;

            public override void Initialize(HistoryProviderInitializeParameters parameters)
            {
            }

            public override IEnumerable<Slice> GetHistory(IEnumerable<HistoryRequest> requests, DateTimeZone sliceTimeZone)
            {
                lock (Requests)
                {
                    Requests.AddRange(requests);
                }

                return Enumerable.Empty<Slice>();
            }
        }
    }
}
