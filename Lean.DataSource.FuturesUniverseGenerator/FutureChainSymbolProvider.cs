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

using QuantConnect.DataSource.DerivativeUniverseGenerator;
using QuantConnect.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuantConnect.DataSource.FuturesUniverseGenerator
{
    /// <summary>
    /// Futures chain symbol provider used for fetching the futures chain from data file names
    /// </summary>
    public class FutureChainSymbolProvider : ChainSymbolProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FutureChainSymbolProvider"/> class
        /// </summary>
        public FutureChainSymbolProvider(IDataCacheProvider dataCacheProvider, DateTime processingDate, SecurityType securityType,
            string market, string dataFolderRoot)
            : base(dataCacheProvider, processingDate, securityType, market, dataFolderRoot)
        {
        }

        protected override IEnumerable<string> GetZipFileNames(DateTime date, Resolution resolution, TickType tickType)
        {
            var tickTypeLower = tickType.TickTypeToLower();

            if (resolution == Resolution.Minute)
            {
                var basePath = Path.Combine(_dataSourceFolder, resolution.ResolutionToLower());
                var dateStr = date.ToString("yyyyMMdd");

                return Directory.EnumerateDirectories(basePath, "*", new EnumerationOptions() { RecurseSubdirectories = true, MaxRecursionDepth = 1 })
                    .Select(directory => Path.Combine(directory, $"{dateStr}_{tickTypeLower}.zip"))
                    .Where(fileName => File.Exists(fileName));
            }
            // Support for resolutions higher than minute, just for Lean local repo data generation
            else
            {
                try
                {
                    return Directory.EnumerateFiles(Path.Combine(_dataSourceFolder, resolution.ResolutionToLower()), $"*_{tickTypeLower}.zip")
                        .Where(fileName =>
                        {
                            var fileInfo = new FileInfo(fileName);
                            var fileNameParts = Path.GetFileNameWithoutExtension(fileInfo.Name).Split('_');
                            return fileNameParts.Length == 2 && fileNameParts[1] == tickTypeLower;
                        });
                }
                catch (DirectoryNotFoundException)
                {
                    return Enumerable.Empty<string>();
                }
            }
        }
    }
}