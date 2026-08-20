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
using System.Linq;
using QuantConnect.Util;
using QuantConnect.Interfaces;
using System.Collections.Generic;
using QuantConnect.Configuration;
using QuantConnect.DataSource.DerivativeUniverseGenerator;

namespace QuantConnect.DataSource.OptionsUniverseGenerator
{
    /// <summary>
    /// Options chain symbol provider used for fetching the option chains from data file names
    /// </summary>
    public class OptionChainSymbolProvider : ChainSymbolProvider
    {
        private readonly IOptionChainProvider _optionChainProvider;
        private readonly string _market;

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionChainSymbolProvider"/> class
        /// </summary>
        public OptionChainSymbolProvider(IDataCacheProvider dataCacheProvider, DateTime processingDate, SecurityType securityType,
            string market, string dataFolderRoot)
            : base(dataCacheProvider, processingDate, securityType, market, dataFolderRoot)
        {
            _market = market;

            if (Config.TryGetValue<string>("universe-option-chain-provider", out var optionChainProviderStr) &&
                !string.IsNullOrEmpty(optionChainProviderStr))
            {
                _optionChainProvider = Composer.Instance.GetExportedValueByTypeName<IOptionChainProvider>(optionChainProviderStr);
            }
        }

        /// <summary>
        /// Gets all the available symbols keyed by the canonical symbol from the available price data in the data folder.
        /// </summary>
        public override Dictionary<Symbol, List<Symbol>> GetSymbols()
        {
            if (_optionChainProvider == null)
            {
                return base.GetSymbols();
            }

            // A tickerless dummy symbol fetches the contracts of every canonical of the
            // generator's security type and market the provider finds
            var contracts = _optionChainProvider.GetOptionContractList(CreateChainsRequestSymbol(), _processingDate)?.ToList();
            if (contracts == null || contracts.Count == 0)
            {
                // The custom chain provider failed, fallback to the file-based chains
                return base.GetSymbols();
            }

            return contracts
                .Where(symbol => symbol.SecurityType == _securityType
                    && symbol.ID.Market == _market
                    // do not return expired contracts
                    && _processingDate.Date < symbol.ID.Date.Date)
                .Distinct()
                .GroupBy(symbol => symbol.Canonical)
                .ToDictionary(group => group.Key, group => OrderSymbols(group, _securityType).ToList());
        }

        /// <summary>
        /// Creates the tickerless dummy symbol used to request the chains of every canonical of the
        /// generator's security type and market from the custom chain provider
        /// </summary>
        private Symbol CreateChainsRequestSymbol()
        {
            Symbol underlying;
            switch (_securityType)
            {
                case SecurityType.Option:
                    // equity SID generation must skip mapping, which rejects empty tickers
                    underlying = new Symbol(SecurityIdentifier.GenerateEquity(string.Empty, _market, mapSymbol: false), string.Empty);
                    break;
                case SecurityType.IndexOption:
                    underlying = Symbol.Create(string.Empty, SecurityType.Index, _market);
                    break;
                default:
                    throw new NotSupportedException($"OptionChainSymbolProvider.CreateChainsRequestSymbol(): " +
                        $"unsupported security type {_securityType}");
            }

            return Symbol.CreateCanonicalOption(underlying);
        }
    }
}
