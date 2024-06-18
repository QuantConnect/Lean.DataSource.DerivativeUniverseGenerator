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
        protected static string DataFleetDeploymentDateEnvVariable = "QC_DATAFLEET_DEPLOYMENT_DATE";

        protected virtual void MainImpl(string[] args)
        {
            Initialize(args, out var securityType, out var market, out var dataFolderRoot, out var outputFolderRoot);

            Log.Trace($"Security type: {securityType}. Market: {market}. Data folder: {dataFolderRoot}. Output folder: {outputFolderRoot}");

            var dateStr = Environment.GetEnvironmentVariable(DataFleetDeploymentDateEnvVariable) ?? $"{DateTime.UtcNow.Date:yyyyMMdd}";
            var processingDate = DateTime.ParseExact(dateStr, DateFormat.EightCharacter, CultureInfo.InvariantCulture);

            var optionsUniverseGenerator = GetUniverseGenerator(securityType, market, dataFolderRoot, outputFolderRoot, processingDate);

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

        protected abstract DerivativeUniverseGenerator GetUniverseGenerator(SecurityType securityType, string market, string dataFolderRoot,
            string outputFolderRoot, DateTime processingDate);

        /// <summary>
        /// Validate and extract command line args and configuration options.
        /// </summary>
        protected virtual void Initialize(string[] args, out SecurityType securityType, out string market, out string dataFolderRoot,
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
