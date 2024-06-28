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

using QuantConnect.Configuration;
using QuantConnect.Logging;
using System;
using System.Diagnostics;
using System.Globalization;

namespace QuantConnect.DataSource.LeanRepoOptionsUniverseGenerator
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
    internal class Program : DerivativeUniverseGenerator.Program
    {
        public static void Main(string[] args)
        {
            Program program = new Program();
            program.MainImpl(args);
        }

        protected override void MainImpl(string[] args)
        {
            Initialize(args, out var securityType, out var market, out var dataFolderRoot, out var outputFolderRoot);

            dataFolderRoot = "./InputData";
            outputFolderRoot = "./OutputData";

            Config.Set("map-file-provider", "QuantConnect.Data.Auxiliary.LocalZipMapFileProvider");
            Config.Set("factor-file-provider", "QuantConnect.Data.Auxiliary.LocalZipFactorFileProvider");
            Config.Set("data-folder", dataFolderRoot);
            Config.Set("job-user-id", "200374");
            Config.Set("api-access-token", "2bf9e6154875a89a9dedf4f2a4a3fece8b180233750e4cb4adfc65dcf865ebda");
            Config.Set("job-organization-id", "d6d62db48592c72e67b534553413b691");

            Globals.Reset();

            Log.Trace($"Security type: {securityType}. Market: {market}. Data folder: {dataFolderRoot}. Output folder: {outputFolderRoot}");

            var timer = new Stopwatch();
            timer.Start();

            var start = new DateTime(2014, 06, 01);
            var end = new DateTime(2014, 06, 2);
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                //foreach (var secType in new[] { SecurityType.IndexOption })
                foreach (var secType in new[] { SecurityType.Option })
                {
                    securityType = secType;
                    var optionsUniverseGenerator = GetUniverseGenerator(securityType, market, dataFolderRoot, outputFolderRoot, date);

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

                    Composer.Instance.Reset();
                }
            }

            Log.Trace($"QuantConnect.DataSource.DerivativeUniverseGenerator.Program.Main(): DONE in {timer.Elapsed:g}");

            Environment.Exit(0);
        }

        protected override DerivativeUniverseGenerator.DerivativeUniverseGenerator GetUniverseGenerator(SecurityType securityType, string market,
            string dataFolderRoot, string outputFolderRoot, DateTime processingDate)
        {
            return new LeanRepoOptionsUniverseGenerator(processingDate, securityType, market, dataFolderRoot, outputFolderRoot);
        }
    }
}
