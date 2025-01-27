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
                out var symbolsToProcess, argNamesToIgnore ?? Array.Empty<string>());

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
            var historyProvider = new HistoryProviderManager();
            var parameters = new HistoryProviderInitializeParameters(null, api, dataProvider, dataCacheProvider, mapFileProvider,
                factorFileProvider, (_) => { }, true, new DataPermissionManager(), null, new AlgorithmSettings());
            historyProvider.Initialize(parameters);

            var timer = new Stopwatch();
            timer.Start();

            foreach (var market in markets)
            {
                var optionsUniverseGenerator = GetUniverseGenerator(securityType, market, symbolsToProcess, dataFolderRoot,
                    outputFolderRoot, processingDate, dataProvider, dataCacheProvider, historyProvider);

                try
                {
                    if (!optionsUniverseGenerator.Run())
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

        protected abstract DerivativeUniverseGenerator GetUniverseGenerator(
            SecurityType securityType,
            string market,
            string[] symbolsToProcess,
            string dataFolderRoot,
            string outputFolderRoot,
            DateTime processingDate,
            IDataProvider dataProvider,
            IDataCacheProvider dataCacheProvider,
            HistoryProviderManager historyProvider);

        /// <summary>
        /// Validate and extract command line args and configuration options.
        /// </summary>
        protected virtual void Initialize(string[] args, out SecurityType securityType, out string[] markets, out string dataFolderRoot,
            out string outputFolderRoot, out string[] symbols, string[] argNamesToIgnore)
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

            var symbolsStr = Config.Get("symbols", "[]");
            symbols = JsonConvert.DeserializeObject<string[]>(symbolsStr);
        }
    }
}
