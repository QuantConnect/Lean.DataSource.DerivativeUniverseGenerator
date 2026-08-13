/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2024 QuantConnect Corporation.
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

using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System;

using QuantConnect.Configuration;
using QuantConnect.Logging;
using QuantConnect.Util;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.HistoricalData;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// Entry point abstract class with common functionalities for derivatives universe generator programs.
    /// </summary>
    /// <param name="args">
    /// All CLI argument are optional, if defined they will override the ones defined in config.json
    /// Possible arguments are:
    ///     "--security-type="          : Option security type to process.
    ///     "--market="                 : Market of data to process.
    /// </param>
    /// <remarks>
    /// To use the base implementation, just instantiate your program class and call
    /// the <see cref="MainImpl(string[])"/> method in the static Main method.
    ///
    /// To override the initialization, implement the <see cref="Initialize(string[], out SecurityType, out string, out string, out string)"/> method.
    /// To add new command line arguments, another Initialize method could be added, calling the base method and adding the new arguments.
    /// </remarks>
    public abstract class Program
    {
        private static readonly string DataFleetDeploymentDateEnvVariable = "QC_DATAFLEET_DEPLOYMENT_DATE";

        protected virtual void MainImpl(string[] args, string[] argNamesToIgnore = null)
        {
            Initialize(args, out var securityType, out var markets, out var dataFolderRoot, out var outputFolderRoot,
                argNamesToIgnore ?? Array.Empty<string>());

            var symbolsStr = Config.Get("universe-generation-symbols", "[]");
            var symbols = JsonConvert.DeserializeObject<string[]>(symbolsStr);
            DerivativeUniverseGenerator.SetSymbolsToProcess(symbols);

            Log.Trace($"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): " +
                $"Security type: {securityType}. Markets: {string.Join(", ", markets)}. Data folder: {dataFolderRoot}. Output folder: {outputFolderRoot}");
            Log.DebuggingEnabled = Config.GetBool("debug-mode");

            var dateStr = Environment.GetEnvironmentVariable(DataFleetDeploymentDateEnvVariable) ?? $"{DateTime.UtcNow.Date:yyyyMMdd}";
            var processingDate = DateTime.ParseExact(dateStr, DateFormat.EightCharacter, CultureInfo.InvariantCulture);

            var dataProvider = Composer.Instance.GetExportedValueByTypeName<IDataProvider>(Config.Get("data-provider", "DefaultDataProvider"));

            var mapFileProvider = Composer.Instance.GetExportedValueByTypeName<IMapFileProvider>(Config.Get("map-file-provider", "LocalZipMapFileProvider"));
            mapFileProvider.Initialize(dataProvider);

            var factorFileProvider = Composer.Instance.GetExportedValueByTypeName<IFactorFileProvider>(Config.Get("factor-file-provider", "LocalZipFactorFileProvider"));
            factorFileProvider.Initialize(mapFileProvider, dataProvider);
            var api = new Api.Api();
            api.Initialize(Globals.UserId, Globals.UserToken, Globals.DataFolder);

            var dataCacheProvider = new ZipDataCacheProvider(dataProvider);
            var parameters = new HistoryProviderInitializeParameters(null, api, dataProvider, dataCacheProvider, mapFileProvider,
                factorFileProvider, (_) => { }, true, new DataPermissionManager(), null, new AlgorithmSettings());
            var (underlyingHistoryProvider, derivativeHistoryProvider) = CreateHistoryProviders(parameters);

            var timer = new Stopwatch();
            timer.Start();

            foreach (var market in markets)
            {
                var universeGenerator = GetUniverseGenerator(securityType, market, dataFolderRoot, outputFolderRoot, processingDate,
                    dataProvider, dataCacheProvider, underlyingHistoryProvider, derivativeHistoryProvider);

                try
                {
                    if (!universeGenerator.Run())
                    {
                        Log.Error($"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): Failed to generate universe.");
                        Environment.Exit(1);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): Error generating universe.");
                    Environment.Exit(1);
                }
            }

            Log.Trace($"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): DONE in {timer.Elapsed:g}");

            Environment.Exit(0);
        }

        protected abstract DerivativeUniverseGenerator GetUniverseGenerator(SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot, DateTime processingDate, IDataProvider dataProvider, IDataCacheProvider dataCacheProvider,
            HistoryProviderManager underlyingHistoryProvider, HistoryProviderManager derivativeHistoryProvider);

        /// <summary>
        /// Creates the history providers to use for the underlying securities and for the derivative contracts.
        /// The "universe-generation-underlying-history-provider" and "universe-generation-derivative-history-provider" configs
        /// allow overriding the history providers (from the "history-provider" config) to use for each of them.
        /// When a config is not set, the corresponding history provider falls back to the "history-provider" config,
        /// so by default a single shared history provider is used for both, just like before these configs existed.
        /// </summary>
        private static (HistoryProviderManager UnderlyingHistoryProvider, HistoryProviderManager DerivativeHistoryProvider) CreateHistoryProviders(
            HistoryProviderInitializeParameters parameters)
        {
            var underlyingHistoryProviders = Config.Get("universe-generation-underlying-history-provider");
            var derivativeHistoryProviders = Config.Get("universe-generation-derivative-history-provider");

            HistoryProviderManager defaultHistoryProvider = null;
            HistoryProviderManager GetDefaultHistoryProvider() => defaultHistoryProvider ??= CreateHistoryProvider(null, parameters);

            var underlyingHistoryProvider = underlyingHistoryProviders.DeserializeList().IsNullOrEmpty()
                ? GetDefaultHistoryProvider()
                : CreateHistoryProvider(underlyingHistoryProviders, parameters);

            var derivativeHistoryProvider = derivativeHistoryProviders.DeserializeList().IsNullOrEmpty()
                ? GetDefaultHistoryProvider()
                : derivativeHistoryProviders == underlyingHistoryProviders
                    ? underlyingHistoryProvider
                    : CreateHistoryProvider(derivativeHistoryProviders, parameters);

            return (underlyingHistoryProvider, derivativeHistoryProvider);
        }

        /// <summary>
        /// Creates and initializes a history provider manager for the given history providers,
        /// or for the "history-provider" config if none are given.
        /// </summary>
        private static HistoryProviderManager CreateHistoryProvider(string historyProviders, HistoryProviderInitializeParameters parameters)
        {
            var historyProviderManager = new HistoryProviderManager();
            if (string.IsNullOrEmpty(historyProviders))
            {
                historyProviderManager.Initialize(parameters);
                return historyProviderManager;
            }

            // The history provider manager reads the history providers to wrap from the "history-provider" config,
            // so we temporarily override it while initializing this instance
            var originalHistoryProviders = Config.Get("history-provider", "SubscriptionDataReaderHistoryProvider");
            Config.Set("history-provider", historyProviders);
            try
            {
                historyProviderManager.Initialize(parameters);
            }
            finally
            {
                Config.Set("history-provider", originalHistoryProviders);
            }

            return historyProviderManager;
        }

        /// <summary>
        /// Validate and extract command line args and configuration options.
        /// </summary>
        protected virtual void Initialize(string[] args, out SecurityType securityType, out string[] markets, out string dataFolderRoot,
            out string outputFolderRoot, string[] argNamesToIgnore)
        {
            var argsData = args.Select(x => x.Split('=')).ToDictionary(x => x[0], x => x.Length > 1 ? x[1] : null);

            if (!argNamesToIgnore.Contains("security-type"))
            {
                if (!argsData.TryGetValue("--security-type", out var securityTypeStr) ||
                !Enum.TryParse(securityTypeStr, true, out securityType) ||
                !Enum.IsDefined(typeof(SecurityType), securityType))
                {
                    if (!Config.TryGetValue("security-type", SecurityType.Option, out securityType))
                    {
                        throw new ArgumentException("Invalid or missing security type.");
                    }
                }
            }
            else
            {
                securityType = default;
            }

            if (!argsData.TryGetValue("--market", out var marketsStr) &&
                !Config.TryGetValue("market", out marketsStr) || string.IsNullOrEmpty(marketsStr))
            {
                markets = [Market.USA];
                Log.Trace($"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): no market given, defaulting to '{Market.USA}'");
            }
            else
            {
                markets = marketsStr.Split(",").Select(x => x.Trim()).ToArray();
            }

            // TODO: Should we set the "data-folder" config to "processed-data-directory"?
            dataFolderRoot = Config.Get("processed-data-directory", Globals.DataFolder);
            outputFolderRoot = Config.Get("temp-output-folder", "/temp-output-directory");
        }
    }
}
