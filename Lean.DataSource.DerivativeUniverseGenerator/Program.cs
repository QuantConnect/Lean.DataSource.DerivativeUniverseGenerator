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

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// Entry point for derivatives universe generator.
    /// </summary>
    /// <param name="args">
    /// All CLI argument are optional, if defined they will override the ones defined in config.json
    /// Possible arguments are:
    ///     "--security-type="          : Option security type to process.
    ///     "--market="                 : Market of data to process.
    /// </param>
    internal class Program
    {
        private static string DataFleetDeploymentDateEnvVariable = "QC_DATAFLEET_DEPLOYMENT_DATE";

        static void Main(string[] args)
        {
            Initialize(args, out var securityType, out var market, out var dataFolderRoot, out var outputFolderRoot);

            dataFolderRoot = "./InputData";
            outputFolderRoot = "./OutputData";

            //Config.Set("map-file-provider", "QuantConnect.Data.Auxiliary.LocalZipMapFileProvider");
            //Config.Set("factor-file-provider", "QuantConnect.Data.Auxiliary.LocalZipFactorFileProvider");
            //Config.Set("data-provider", "QuantConnect.Lean.Engine.DataFeeds.ApiDataProvider");
            Config.Set("map-file-provider", "QuantConnect.Data.Auxiliary.LocalDiskMapFileProvider");
            Config.Set("factor-file-provider", "QuantConnect.Data.Auxiliary.LocalDiskFactorFileProvider");
            Config.Set("data-provider", "QuantConnect.Lean.Engine.DataFeeds.DefaultDataProvider");
            Config.Set("data-folder", dataFolderRoot);
            Config.Set("job-user-id", "200374");
            Config.Set("api-access-token", "2bf9e6154875a89a9dedf4f2a4a3fece8b180233750e4cb4adfc65dcf865ebda");
            Config.Set("job-organization-id", "d6d62db48592c72e67b534553413b691");

            Globals.Reset();

            Log.Trace($"Security type: {securityType}. Market: {market}. Data folder: {dataFolderRoot}. Output folder: {outputFolderRoot}");

            var dateStr = Environment.GetEnvironmentVariable(DataFleetDeploymentDateEnvVariable) ?? $"{DateTime.UtcNow.Date:yyyyMMdd}";
            var processingDate = DateTime.ParseExact(dateStr, DateFormat.EightCharacter, CultureInfo.InvariantCulture);

            // TODO: remove this next lines
            securityType = SecurityType.Option;
            processingDate = new DateTime(2015, 12, 24);
            // ----------
            //securityType = SecurityType.IndexOption;
            //processingDate = new DateTime(2021, 01, 01);
            // ----------
            //securityType = SecurityType.FutureOption; // DC
            //processingDate = new DateTime(2012, 01, 01);
            //market = "cme";
            // ----------
            //securityType = SecurityType.FutureOption; // ES
            //processingDate = new DateTime(2020, 01, 01);
            //market = "cme";

            var optionsUniverseGenerator = new DerivativeUniverseGenerator(processingDate, securityType, market, dataFolderRoot, outputFolderRoot);

            var timer = new Stopwatch();
            timer.Start();

            try
            {
                if (!optionsUniverseGenerator.Run())
                {
                    Log.Error($"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): Failed to generate options universe.");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): Error generating options universe.");
                Environment.Exit(1);
            }

            Log.Trace($"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): DONE in {timer.Elapsed:g}");

            Environment.Exit(0);
        }

        /// <summary>
        /// Validate and extract command line args and configuration options.
        /// </summary>
        private static void Initialize(string[] args, out SecurityType securityType, out string market, out string dataFolderRoot,
            out string outputFolderRoot)
        {
            var argsData = args.Select(x => x.Split('=')).ToDictionary(x => x[0], x => x.Length > 1 ? x[1] : null);

            if (!argsData.TryGetValue("--security-type", out var securityTypeStr) ||
                !Enum.TryParse(securityTypeStr, true, out securityType) ||
                !Enum.IsDefined(typeof(SecurityType), securityType))
            {
                if (!Config.TryGetValue("security-type", out securityType))
                {
                    throw new ArgumentException("Invalid or missing security type.");
                }
            }

            if (!argsData.TryGetValue("--market", out market) && !Config.TryGetValue("market", out market))
            {
                throw new ArgumentException("Missing market.");
            }

            dataFolderRoot = Config.Get("processed-data-directory", Globals.DataFolder);
            outputFolderRoot = Config.Get("temp-output-folder", "/temp-output-directory");
        }
    }
}
