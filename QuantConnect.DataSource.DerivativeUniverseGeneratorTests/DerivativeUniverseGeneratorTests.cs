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

        private class TestDerivativeUniverseGenerator : DerivativeUniverseGeneratorBase
        {
            public TestDerivativeUniverseGenerator(DateTime processingDate, SecurityType securityType, string market, string outputFolderRoot,
                IHistoryProvider historyProvider = null)
                : base(processingDate, securityType, market, outputFolderRoot, outputFolderRoot, null, null, historyProvider)
            {
            }

            public new string GetUniverseFileName(Symbol canonicalSymbol)
            {
                return base.GetUniverseFileName(canonicalSymbol);
            }

            protected override Dictionary<Symbol, List<Symbol>> FilterSymbols(Dictionary<Symbol, List<Symbol>> symbols,
                HashSet<string> symbolsToProcess)
            {
                return symbols;
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
    }
}
