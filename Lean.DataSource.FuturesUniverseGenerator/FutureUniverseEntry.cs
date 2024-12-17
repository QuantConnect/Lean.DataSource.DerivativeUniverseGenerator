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

using QuantConnect.Data.UniverseSelection;
using QuantConnect.DataSource.DerivativeUniverseGenerator;

namespace QuantConnect.DataSource.FuturesUniverseGenerator
{
    /// <summary>
    /// Representation of a future contract universe entry
    /// </summary>
    public class FutureUniverseEntry : BaseContractUniverseFileEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FutureUniverseEntry"/> class.
        /// </summary>
        /// <param name="symbol"></param>
        public FutureUniverseEntry(Symbol symbol)
           : base(symbol)
        {
        }

        /// <summary>
        /// Returns a CSV representation of the future contract's data.
        /// </summary>
        public override string ToCsv()
        {
            // Use Lean's FutureUniverse class to generate the CSV to avoid writing/reading mistakes
            return FutureUniverse.ToCsv(Symbol, Open, High, Low, Close, Volume, OpenInterest);
        }

        /// <summary>
        /// Gets the header of the CSV file
        /// </summary>
        public override string GetHeader()
        {
            return FutureUniverse.CsvHeader;
        }
    }
}